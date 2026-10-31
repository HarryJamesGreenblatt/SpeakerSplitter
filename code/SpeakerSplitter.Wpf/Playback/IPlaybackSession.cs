using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Wpf.Playback;

/// <summary>The shell's session boundary; Start, Stop and Dispose are called on the same UI thread.</summary>
public interface IPlaybackSession : IDisposable
{
    /// <summary>Raised on a worker thread when audio is lost; handlers must enqueue UI work without blocking.</summary>
    event Action<string>? Lost;

    /// <summary>Starts mirroring to the selected non-default endpoints, revalidating them in the engine.</summary>
    /// <param name="targets">The selected output snapshots.</param>
    void Start(IReadOnlyList<EndpointInfo> targets);

    /// <summary>Stops audio and its callbacks; failures are reported to the caller.</summary>
    void Stop();
}
