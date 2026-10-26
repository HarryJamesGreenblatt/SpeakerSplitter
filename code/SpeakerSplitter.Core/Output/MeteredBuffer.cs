using NAudio.Wave;

namespace SpeakerSplitter.Output;

/// <summary>
/// Wraps a device's <see cref="BufferedWaveProvider"/> to count what it otherwise handles silently: reads it
/// must zero-fill (underruns) and writes it must drop (overflows). The capture thread writes and the playback
/// thread reads, so the counters use <see cref="Interlocked"/>.
/// <para>
/// Loopback capture delivers no packets while nothing is playing, so silence also reads as underruns (about
/// 100 per second at 10 ms periods). Only a rise while audio is playing marks a glitch.
/// </para>
/// </summary>
public sealed class MeteredBuffer : IWaveProvider
{
    #region Fields

    /// <summary>The buffer that holds the audio between the capture thread and the playback thread.</summary>
    private readonly BufferedWaveProvider _buffer;

    /// <summary>Reads so far that were padded with silence; updated with <see cref="Interlocked"/>.</summary>
    private long _underruns;

    /// <summary>Writes so far that dropped audio; updated with <see cref="Interlocked"/>.</summary>
    private long _overflows;

    #endregion

    #region Properties

    /// <summary>The format of the buffered audio.</summary>
    public WaveFormat WaveFormat => _buffer.WaveFormat;

    /// <summary>The buffer's current fill and counters. Safe to read from any thread.</summary>
    public BufferStats Stats => new(
        _buffer.BufferedBytes,
        _buffer.BufferLength,
        WaveFormat.AverageBytesPerSecond,
        Interlocked.Read(ref _underruns),
        Interlocked.Read(ref _overflows));

    #endregion

    #region Constructor(s)

    /// <summary>Creates an empty buffer.</summary>
    /// <param name="format">The format of the audio that will be written.</param>
    /// <param name="duration">How much audio the buffer holds before it starts dropping.</param>
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

    #endregion

    #region Methods

    /// <summary>Appends captured audio, dropping whatever does not fit and counting the write as an overflow.</summary>
    /// <param name="buffer">The captured audio.</param>
    /// <param name="offset">Where the audio starts in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of valid bytes.</param>
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

    /// <summary>
    /// Fills the playback request, padding any shortfall with silence and counting the read as an underrun.
    /// <c>WasapiOut</c> treats a zero-byte read as the end of the stream, so the full count is always returned.
    /// </summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">Where to start writing in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of bytes requested.</param>
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

    #endregion
}
