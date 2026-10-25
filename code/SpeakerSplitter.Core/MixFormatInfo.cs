using NAudio.Wave;

namespace SpeakerSplitter;

public sealed record MixFormatInfo(int SampleRate, int Channels, int BitsPerSample, string Encoding)
{
    public static MixFormatInfo From(WaveFormat format) =>
        new(format.SampleRate, format.Channels, format.BitsPerSample, DescribeEncoding(format));

    public string Describe() => $"{SampleRate} Hz, {Channels} ch, {BitsPerSample}-bit {Encoding}";

    // Shared-mode mix formats are usually WAVE_FORMAT_EXTENSIBLE, whose Encoding is only "Extensible";
    // the SubFormat says whether samples are PCM or float.
    private static string DescribeEncoding(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible)
        {
            return format.Encoding.ToString();
        }

        var standard = extensible.ToStandardWaveFormat();
        return standard.Encoding != WaveFormatEncoding.Extensible
            ? standard.Encoding.ToString()
            : extensible.SubFormat.ToString();
    }
}
