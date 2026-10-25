namespace SpeakerSplitter;

// Holds the loudest recent peak and decays it at a fixed dB per second. Decay is measured from the
// clock rather than from incoming buffers, because loopback capture can go quiet during silence.
public sealed class LevelMeter
{
    private const float DecayDbPerSecond = 60f;

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private float _level;
    private long _stamp;

    public LevelMeter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _stamp = _time.GetTimestamp();
    }

    public float Level
    {
        get
        {
            lock (_gate)
            {
                Decay();
                return _level;
            }
        }
    }

    // Attack is instant: a louder peak replaces the held level immediately.
    public void Push(float peak)
    {
        lock (_gate)
        {
            Decay();
            if (peak > _level)
            {
                _level = peak;
            }
        }
    }

    private void Decay()
    {
        var now = _time.GetTimestamp();
        var seconds = (float)_time.GetElapsedTime(_stamp, now).TotalSeconds;
        _stamp = now;
        _level *= MathF.Pow(10f, -DecayDbPerSecond * seconds / 20f);
    }
}
