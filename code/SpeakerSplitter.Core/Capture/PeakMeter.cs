using System.Runtime.InteropServices;
using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Capture;

/// <summary>Measures how loud a buffer of captured audio is.</summary>
public static class PeakMeter
{
    #region Constants

    /// <summary>The level, in dB, at and below which a meter shows silence.</summary>
    public const float FloorDb = -60f;

    #endregion

    #region Methods

    /// <summary>Whether <see cref="Compute"/> can read audio in this format: 32-bit float or 16-bit PCM.</summary>
    /// <param name="format">The capture format.</param>
    public static bool IsSupported(MixFormatInfo format) => IsFloat32(format) || IsPcm16(format);

    /// <summary>
    /// Finds the largest absolute sample in the buffer, on a 0..1 scale where 1.0 is digital full scale.
    /// Channels are not separated: the result is the loudest sample on any channel.
    /// </summary>
    /// <param name="buffer">Captured audio in the given format.</param>
    /// <param name="format">The capture format.</param>
    /// <exception cref="NotSupportedException">The format is not 32-bit float or 16-bit PCM.</exception>
    public static float Compute(ReadOnlySpan<byte> buffer, MixFormatInfo format)
    {
        float peak = 0f;

        // Float samples are already on the 0..1 scale.
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

        // 16-bit samples are scaled by 32768 so that full scale reads as 1.0.
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

    /// <summary>Whether the format is 32-bit IEEE float.</summary>
    /// <param name="format">The format to test.</param>
    private static bool IsFloat32(MixFormatInfo format) => format is { Encoding: "IeeeFloat", BitsPerSample: 32 };

    /// <summary>Whether the format is 16-bit PCM.</summary>
    /// <param name="format">The format to test.</param>
    private static bool IsPcm16(MixFormatInfo format) => format is { Encoding: "Pcm", BitsPerSample: 16 };

    #endregion
}
