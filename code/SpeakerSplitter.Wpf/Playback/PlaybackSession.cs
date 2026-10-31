using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Wpf.Playback;

/// <summary>A thin adapter over the same engine used by the CLI; no audio processing lives in the shell.</summary>
public sealed class PlaybackSession : IPlaybackSession
{
    #region Fields

    /// <summary>The owned engine, opened on the current default output.</summary>
    private readonly SplitterEngine _engine;

    #endregion

    #region Events

    /// <inheritdoc />
    public event Action<string>? Lost
    {
        add => _engine.Lost += value;
        remove => _engine.Lost -= value;
    }

    #endregion

    #region Constructor(s)

    /// <summary>Opens a fresh engine on the current default multimedia output.</summary>
    public PlaybackSession()
    {
        _engine = SplitterEngine.ForDefaultOutput();
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public void Start(IReadOnlyList<EndpointInfo> targets) => _engine.Start(targets);

    /// <inheritdoc />
    public void Stop() => _engine.Stop();

    /// <inheritdoc />
    public void Dispose() => _engine.Dispose();

    #endregion
}
