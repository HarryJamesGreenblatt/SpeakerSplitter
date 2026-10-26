namespace SpeakerSplitter;

/// <summary>
/// Holds the loudest recent peak and decays it at a fixed dB per second. Decay is measured from the clock
/// rather than from incoming buffers, because loopback capture can go quiet during silence.
/// </summary>
public sealed class LevelMeter
{
    #region Constants

    /// <summary>How fast a held peak falls, in dB per second.</summary>
    private const float DecayDbPerSecond = 60f;

    #endregion

    #region Fields

    /// <summary>The clock the decay is measured against; replaceable so tests can step time by hand.</summary>
    private readonly TimeProvider _time;

    /// <summary>Guards <see cref="_level"/> and <see cref="_stamp"/> against the capture and timer threads.</summary>
    private readonly object _gate = new();

    /// <summary>The held peak on a 0..1 scale, as of <see cref="_stamp"/>.</summary>
    private float _level;

    /// <summary>The clock timestamp at which <see cref="_level"/> was last decayed.</summary>
    private long _stamp;

    #endregion

    #region Properties

    /// <summary>The held peak on a 0..1 scale, decayed up to this moment. Safe to read from any thread.</summary>
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

    #endregion

    #region Constructor(s)

    /// <summary>Creates a meter that starts silent.</summary>
    /// <param name="time">The clock to measure decay against; defaults to the system clock.</param>
    public LevelMeter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _stamp = _time.GetTimestamp();
    }

    #endregion

    #region Methods

    /// <summary>
    /// Offers a new peak. Attack is instant: a louder peak replaces the held level immediately, while a quieter
    /// one leaves it to decay.
    /// </summary>
    /// <param name="peak">The peak of the latest buffer, on a 0..1 scale.</param>
    public void Push(float peak)
    {
        lock (_gate)
        {
            // Bring the held level up to date first so the comparison is against what is audible now.
            Decay();
            if (peak > _level)
            {
                _level = peak;
            }
        }
    }

    /// <summary>Lowers the held level by the time elapsed since it was last decayed. Callers hold <see cref="_gate"/>.</summary>
    private void Decay()
    {
        var now = _time.GetTimestamp();
        var seconds = (float)_time.GetElapsedTime(_stamp, now).TotalSeconds;
        _stamp = now;
        _level *= MathF.Pow(10f, -DecayDbPerSecond * seconds / 20f);
    }

    #endregion
}
