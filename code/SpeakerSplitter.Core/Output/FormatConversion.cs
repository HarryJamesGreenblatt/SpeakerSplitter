using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Output;

/// <summary>
/// Converts one device's audio to that device's own mix format. A device whose format already matches the
/// capture never gets a converter, so nothing is resampled or remapped needlessly.
/// <para>
/// <c>WasapiOut</c> in shared mode already asks Windows to convert (AUTOCONVERTPCM). On the development
/// hardware a 44.1 kHz capture mirrored to a 48 kHz device played a 1000 Hz tone at a measured 1000.0 Hz with
/// no converter here. The explicit stage keeps the result deterministic and testable instead of depending on
/// the audio engine, which remains the fallback if a device's format changes while running.
/// </para>
/// </summary>
public static class FormatConversion
{
    /// <summary>Whether audio in the capture format must be converted before this device can play it.</summary>
    /// <param name="source">The capture format.</param>
    /// <param name="device">The device's mix format.</param>
    public static bool NeedsConversion(WaveFormat source, MixFormatInfo device) =>
        source.SampleRate != device.SampleRate || source.Channels != device.Channels;

    /// <summary>Describes a conversion in one line, for example <c>44100 Hz 2 ch -> 48000 Hz 2 ch</c>.</summary>
    /// <param name="source">The capture format.</param>
    /// <param name="device">The device's mix format.</param>
    public static string Describe(WaveFormat source, MixFormatInfo device) =>
        $"{source.SampleRate} Hz {source.Channels} ch -> {device.SampleRate} Hz {device.Channels} ch";

    /// <summary>
    /// Wraps <paramref name="source"/> in the stages that turn it into the device's sample rate and channel
    /// count. The result is 32-bit float; sample type is left to WASAPI.
    /// </summary>
    /// <param name="source">The audio to convert.</param>
    /// <param name="device">The device's mix format.</param>
    public static IWaveProvider Create(IWaveProvider source, MixFormatInfo device)
    {
        var samples = source.ToSampleProvider();

        // Fix the channel layout first, then the sample rate; either stage is skipped when it already matches.
        if (samples.WaveFormat.Channels != device.Channels)
        {
            samples = MapChannels(samples, device.Channels);
        }

        if (samples.WaveFormat.SampleRate != device.SampleRate)
        {
            samples = new WdlResamplingSampleProvider(samples, device.SampleRate);
        }

        return samples.ToWaveProvider();
    }

    /// <summary>
    /// Maps audio onto a different channel count. Mono is centred and stereo folds down to the average of both
    /// sides. Any other layout keeps the leading channels: extra output channels stay silent and extra input
    /// channels are dropped.
    /// </summary>
    /// <param name="source">The audio to remap.</param>
    /// <param name="channels">The channel count the device expects.</param>
    private static ISampleProvider MapChannels(ISampleProvider source, int channels) =>
        (source.WaveFormat.Channels, channels) switch
        {
            (1, 2) => new MonoToStereoSampleProvider(source),
            (2, 1) => new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => KeepLeadingChannels(source, channels),
        };

    /// <summary>
    /// Keeps the first channels of <paramref name="source"/> and silences or drops the rest. The multiplexer
    /// leaves output channels without an input untouched rather than zeroed, so the missing ones are fed from
    /// an explicit silent input.
    /// </summary>
    /// <param name="source">The audio to remap.</param>
    /// <param name="channels">The channel count the device expects.</param>
    private static ISampleProvider KeepLeadingChannels(ISampleProvider source, int channels)
    {
        var missing = channels - source.WaveFormat.Channels;

        // Add a silent input only when the device has more channels than the source.
        ISampleProvider[] inputs = missing > 0
            ? [source, new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, missing)).ToSampleProvider()]
            : [source];
        return new MultiplexingSampleProvider(inputs, channels);
    }
}
