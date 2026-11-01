using SpeakerSplitter.Devices;

namespace SpeakerSplitter.App.Playback;

/// <summary>The shell's session boundary; Start, Stop and Dispose are called on the same UI thread.</summary>
public interface IPlaybackSession : IDisposable
{
    /// <summary>Raised on a worker thread when audio is lost; handlers must enqueue UI work without blocking.</summary>
    event Action<string>? Lost;

    /// <summary>Starts mirroring to the selected non-default endpoints, revalidating them in the engine.</summary>
    /// <param name="targets">The selected output snapshots.</param>
    /// <param name="initialVolumes">App-only gains keyed by selected endpoint ID, applied before playback starts.</param>
    void Start(IReadOnlyList<EndpointInfo> targets, IReadOnlyDictionary<string, float> initialVolumes);

    /// <summary>Adjusts only the selected mirrored stream's app-only gain while running.</summary>
    /// <param name="endpointId">The mirrored secondary's stable ID.</param>
    /// <param name="volume">Linear gain from zero to one.</param>
    void SetVolume(string endpointId, float volume);

    /// <summary>Stops audio and its callbacks; failures are reported to the caller.</summary>
    void Stop();
}
