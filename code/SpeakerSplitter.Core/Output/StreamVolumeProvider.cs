using System.Runtime.InteropServices;
using NAudio.Wave;
using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Output;

/// <summary>
/// Applies app-only gain to one rendered stream without touching endpoint or session master volume.
/// Reads belong to one playback thread; the owning host may update <see cref="Volume"/> concurrently.
/// </summary>
public sealed class StreamVolumeProvider : IWaveProvider
{
    #region Fields

    /// <summary>The device's buffered audio, after any sample-rate/channel conversion.</summary>
    private readonly IWaveProvider _source;

    /// <summary>Whether samples are 32-bit float rather than 16-bit PCM.</summary>
    private readonly bool _isFloat;

    /// <summary>The requested linear gain, from zero to one, exchanged with the host atomically.</summary>
    private float _volume;

    /// <summary>Approximately five milliseconds of output frames, shared by every channel in a frame.</summary>
    private readonly int _rampFrames;

    /// <summary>The gain applied to the last rendered frame; accessed only by the playback thread.</summary>
    private float _currentVolume;

    /// <summary>The target of the playback thread's current ramp.</summary>
    private float _rampTarget;

    /// <summary>The gain increment for each complete frame during the current ramp.</summary>
    private float _volumeStep;

    /// <summary>Frames left before the current ramp reaches its exact target.</summary>
    private int _remainingFrames;

    #endregion

    #region Properties

    /// <inheritdoc />
    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Requested app-only gain, 0-1, approached over about five milliseconds of rendered frames.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is non-finite or outside 0-1.</exception>
    public float Volume
    {
        get => Volatile.Read(ref _volume);
        set
        {
            ValidateVolume(value);
            Volatile.Write(ref _volume, value);
        }
    }

    #endregion

    #region Constructor(s)

    /// <summary>Wraps a stream with its initial gain, applied before its first playback read.</summary>
    /// <param name="source">32-bit float or 16-bit PCM audio to render.</param>
    /// <param name="volume">Initial linear gain, 0-1; omitted means byte-for-byte unity gain.</param>
    /// <exception cref="NotSupportedException">The source has an unsupported sample format.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The gain is non-finite or outside 0-1.</exception>
    public StreamVolumeProvider(IWaveProvider source, float volume = 1f)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateVolume(volume);
        var format = MixFormatInfo.From(source.WaveFormat);
        _isFloat = format is { Encoding: "IeeeFloat", BitsPerSample: 32 };
        if (!_isFloat && format is not { Encoding: "Pcm", BitsPerSample: 16 })
        {
            throw new NotSupportedException($"Stream volume does not support {format.Describe()}.");
        }

        _source = source;
        _volume = volume;
        _currentVolume = volume;
        _rampTarget = volume;
        _rampFrames = Math.Max(1, source.WaveFormat.SampleRate / 200);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public int Read(byte[] buffer, int offset, int count)
    {
        var destination = buffer.AsSpan(offset, count);
        if (count % WaveFormat.BlockAlign != 0)
        {
            throw new ArgumentException("Playback reads must contain whole audio frames.", nameof(count));
        }

        var read = _source.Read(buffer, offset, count);
        if (read == 0)
        {
            return read;
        }

        if (read % WaveFormat.BlockAlign != 0)
        {
            throw new InvalidOperationException("The audio source returned a partial sample frame.");
        }

        var volume = Volume;
        if (volume != _rampTarget)
        {
            _rampTarget = volume;
            _volumeStep = (volume - _currentVolume) / _rampFrames;
            _remainingFrames = _rampFrames;
        }

        if (_remainingFrames == 0 && _currentVolume == 1f)
        {
            return read;
        }

        var audio = destination[..read];
        if (_remainingFrames == 0 && _currentVolume == 0f)
        {
            audio.Clear();
        }
        else if (_isFloat)
        {
            var samples = MemoryMarshal.Cast<byte, float>(audio);
            for (var frame = 0; frame < samples.Length; frame += WaveFormat.Channels)
            {
                var gain = NextVolume();
                for (var channel = 0; channel < WaveFormat.Channels; channel++)
                {
                    samples[frame + channel] = gain == 0f ? 0f : samples[frame + channel] * gain;
                }
            }
        }
        else
        {
            var samples = MemoryMarshal.Cast<byte, short>(audio);
            for (var frame = 0; frame < samples.Length; frame += WaveFormat.Channels)
            {
                var gain = NextVolume();
                for (var channel = 0; channel < WaveFormat.Channels; channel++)
                {
                    samples[frame + channel] = (short)(samples[frame + channel] * gain);
                }
            }
        }

        return read;
    }

    /// <summary>Advances a ramp once per complete frame, retaining its position across playback reads.</summary>
    private float NextVolume()
    {
        if (_remainingFrames > 0)
        {
            _remainingFrames--;
            _currentVolume = _remainingFrames == 0
                ? _rampTarget
                : Math.Clamp(_currentVolume + _volumeStep, 0f, 1f);
        }

        return _currentVolume;
    }

    /// <summary>Rejects invalid gains before any output is opened or any stream state changes.</summary>
    /// <param name="volume">The requested linear gain.</param>
    /// <exception cref="ArgumentOutOfRangeException">The gain is non-finite or outside 0-1.</exception>
    internal static void ValidateVolume(float volume)
    {
        if (!float.IsFinite(volume) || volume < 0f || volume > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "App-only gain must be finite and between 0 and 1.");
        }
    }

    #endregion
}
