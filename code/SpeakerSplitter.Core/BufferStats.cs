namespace SpeakerSplitter;

// A point-in-time view of one device's buffer. M4 drift correction reads the fill level from here.
public readonly record struct BufferStats(
    int BufferedBytes,
    int BufferLength,
    int AverageBytesPerSecond,
    long Underruns,
    long Overflows)
{
    public double FillFraction => BufferLength == 0 ? 0 : (double)BufferedBytes / BufferLength;

    public double FillMilliseconds => AverageBytesPerSecond == 0 ? 0 : BufferedBytes * 1000.0 / AverageBytesPerSecond;
}

public readonly record struct DeviceBufferStats(string Name, BufferStats Stats);
