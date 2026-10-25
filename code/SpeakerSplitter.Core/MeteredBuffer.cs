using NAudio.Wave;

namespace SpeakerSplitter;

// Wraps a device's BufferedWaveProvider to count what it otherwise handles silently: reads it must
// zero-fill (underruns) and writes it must drop (overflows). The capture thread writes and the
// playback thread reads, so the counters use Interlocked.
// Loopback capture delivers no packets while nothing is playing, so silence also reads as underruns
// (about 100 per second at 10 ms periods). Only a rise while audio is playing marks a glitch.
public sealed class MeteredBuffer : IWaveProvider
{
    private readonly BufferedWaveProvider _buffer;
    private long _underruns;
    private long _overflows;

    public MeteredBuffer(WaveFormat format, TimeSpan duration)
    {
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = duration,
            // Reads are zero-filled here, not by the inner buffer, so a short read can be counted.
            ReadFully = false,
            DiscardOnBufferOverflow = true,
        };
    }

    public WaveFormat WaveFormat => _buffer.WaveFormat;

    public BufferStats Stats => new(
        _buffer.BufferedBytes,
        _buffer.BufferLength,
        WaveFormat.AverageBytesPerSecond,
        Interlocked.Read(ref _underruns),
        Interlocked.Read(ref _overflows));

    public void AddSamples(byte[] buffer, int offset, int count)
    {
        // The playback thread can only free space between this check and the write, so a drop is never
        // missed; a read landing in that gap can at worst count a drop that did not happen.
        if (count > _buffer.BufferLength - _buffer.BufferedBytes)
        {
            Interlocked.Increment(ref _overflows);
        }

        _buffer.AddSamples(buffer, offset, count);
    }

    // WasapiOut treats a zero-byte read as the end of the stream, so a starved read is padded with
    // silence and the full count is returned.
    public int Read(byte[] buffer, int offset, int count)
    {
        var read = _buffer.Read(buffer, offset, count);
        if (read < count)
        {
            Array.Clear(buffer, offset + read, count - read);
            Interlocked.Increment(ref _underruns);
        }

        return count;
    }
}
