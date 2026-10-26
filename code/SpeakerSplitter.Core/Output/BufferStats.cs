namespace SpeakerSplitter.Output;

/// <summary>
/// A point-in-time view of one device's buffer. M4 drift correction reads the fill level from here.
/// </summary>
/// <param name="BufferedBytes">Bytes currently waiting in the buffer.</param>
/// <param name="BufferLength">Capacity of the buffer in bytes.</param>
/// <param name="AverageBytesPerSecond">Bytes one second of audio occupies in the buffered format; converts the fill to time.</param>
/// <param name="Underruns">Reads so far that found too little buffered audio and were padded with silence.</param>
/// <param name="Overflows">Writes so far that found too little free space and dropped audio.</param>
public readonly record struct BufferStats(
    int BufferedBytes,
    int BufferLength,
    int AverageBytesPerSecond,
    long Underruns,
    long Overflows)
{
    /// <summary>The buffer's fill on a 0..1 scale, or 0 when it has no capacity.</summary>
    public double FillFraction => BufferLength == 0 ? 0 : (double)BufferedBytes / BufferLength;

    /// <summary>How much audio is buffered, in milliseconds, or 0 when the byte rate is unknown.</summary>
    public double FillMilliseconds => AverageBytesPerSecond == 0 ? 0 : BufferedBytes * 1000.0 / AverageBytesPerSecond;
}

/// <summary>A device's name paired with its buffer statistics, as reported by <see cref="OutputFanOut.Snapshot"/>.</summary>
/// <param name="Name">The device's friendly name.</param>
/// <param name="Stats">The device's buffer statistics at the moment of the snapshot.</param>
public readonly record struct DeviceBufferStats(string Name, BufferStats Stats);
