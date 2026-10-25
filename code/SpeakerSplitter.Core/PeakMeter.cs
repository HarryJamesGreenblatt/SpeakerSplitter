using System.Runtime.InteropServices;

namespace SpeakerSplitter;

public static class PeakMeter
{
    public const float FloorDb = -60f;

    public static bool IsSupported(MixFormatInfo format) => IsFloat32(format) || IsPcm16(format);

    // Largest absolute sample in the buffer on a 0..1 scale where 1.0 is digital full scale.
    // Channels are not separated: the result is the loudest sample on any channel.
    public static float Compute(ReadOnlySpan<byte> buffer, MixFormatInfo format)
    {
        float peak = 0f;

        if (IsFloat32(format))
        {
            foreach (var sample in MemoryMarshal.Cast<byte, float>(buffer))
            {
                var abs = MathF.Abs(sample);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            return peak;
        }

        if (IsPcm16(format))
        {
            foreach (var sample in MemoryMarshal.Cast<byte, short>(buffer))
            {
                var abs = Math.Abs((int)sample) / 32768f;
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            return peak;
        }

        throw new NotSupportedException($"Peak meter does not support {format.Describe()}.");
    }

    private static bool IsFloat32(MixFormatInfo format) => format is { Encoding: "IeeeFloat", BitsPerSample: 32 };

    private static bool IsPcm16(MixFormatInfo format) => format is { Encoding: "Pcm", BitsPerSample: 16 };
}
