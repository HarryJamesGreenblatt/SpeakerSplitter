using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SpeakerSplitter;

// Converts one device's audio to that device's own mix format. A device whose format already matches the
// capture never gets a converter, so nothing is resampled or remapped needlessly.
//
// WasapiOut in shared mode already asks Windows to convert (AUTOCONVERTPCM). On the development hardware a
// 44.1 kHz capture mirrored to a 48 kHz device played a 1000 Hz tone at a measured 1000.0 Hz with no
// converter here. The explicit stage keeps the result deterministic and testable instead of depending on
// the audio engine, which remains the fallback if a device's format changes while running.
public static class FormatConversion
{
    public static bool NeedsConversion(WaveFormat source, MixFormatInfo device) =>
        source.SampleRate != device.SampleRate || source.Channels != device.Channels;

    public static string Describe(WaveFormat source, MixFormatInfo device) =>
        $"{source.SampleRate} Hz {source.Channels} ch -> {device.SampleRate} Hz {device.Channels} ch";

    // The result is 32-bit float at the device's sample rate and channel count; sample type is left to WASAPI.
    public static IWaveProvider Create(IWaveProvider source, MixFormatInfo device)
    {
        var samples = source.ToSampleProvider();
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

    // Mono is centred and stereo folds down to the average of both sides. Any other layout keeps the
    // leading channels: extra output channels stay silent and extra input channels are dropped.
    private static ISampleProvider MapChannels(ISampleProvider source, int channels) =>
        (source.WaveFormat.Channels, channels) switch
        {
            (1, 2) => new MonoToStereoSampleProvider(source),
            (2, 1) => new StereoToMonoSampleProvider(source) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => KeepLeadingChannels(source, channels),
        };

    // The multiplexer leaves output channels without an input untouched rather than zeroed, so the
    // missing ones are fed from an explicit silent input.
    private static ISampleProvider KeepLeadingChannels(ISampleProvider source, int channels)
    {
        var missing = channels - source.WaveFormat.Channels;
        ISampleProvider[] inputs = missing > 0
            ? [source, new SilenceProvider(WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, missing)).ToSampleProvider()]
            : [source];
        return new MultiplexingSampleProvider(inputs, channels);
    }
}
