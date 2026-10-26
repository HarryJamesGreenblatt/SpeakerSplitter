using NAudio.Wave;

namespace SpeakerSplitter.Devices;

/// <summary>
/// One endpoint's shared-mode mix format, reduced to the four values the console, the converter and the
/// peak meter care about.
/// </summary>
/// <param name="SampleRate">Samples per second, per channel.</param>
/// <param name="Channels">Number of channels.</param>
/// <param name="BitsPerSample">Bits in one sample.</param>
/// <param name="Encoding">Sample type as text, for example <c>IeeeFloat</c> or <c>Pcm</c>.</param>
public sealed record MixFormatInfo(int SampleRate, int Channels, int BitsPerSample, string Encoding)
{
    /// <summary>Summarizes a NAudio format, resolving WAVE_FORMAT_EXTENSIBLE to its real sample type.</summary>
    /// <param name="format">The format to summarize.</param>
    public static MixFormatInfo From(WaveFormat format) =>
        new(format.SampleRate, format.Channels, format.BitsPerSample, DescribeEncoding(format));

    /// <summary>One readable line, for example <c>48000 Hz, 2 ch, 32-bit IeeeFloat</c>.</summary>
    public string Describe() => $"{SampleRate} Hz, {Channels} ch, {BitsPerSample}-bit {Encoding}";

    /// <summary>
    /// Names the sample type of a format. Shared-mode mix formats are usually WAVE_FORMAT_EXTENSIBLE, whose
    /// <see cref="WaveFormat.Encoding"/> is only "Extensible"; the SubFormat says whether samples are PCM or float.
    /// </summary>
    private static string DescribeEncoding(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible)
        {
            return format.Encoding.ToString();
        }

        // Prefer the standard encoding when the extensible format maps onto one; otherwise fall back to the SubFormat.
        var standard = extensible.ToStandardWaveFormat();
        return standard.Encoding != WaveFormatEncoding.Extensible
            ? standard.Encoding.ToString()
            : extensible.SubFormat.ToString();
    }
}
