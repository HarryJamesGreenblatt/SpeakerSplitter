namespace SpeakerSplitter.Devices;

/// <summary>An owned device-wide control handle; callers read and release it on their lifecycle thread.</summary>
public interface IEndpointVolumeControl : IDisposable
{
    /// <summary>Reads current Windows settings; failures are reported rather than substituted with defaults.</summary>
    EndpointVolumeState GetState();

    /// <summary>Writes device-wide Windows volume and returns its actual readback; rejects the current primary.</summary>
    /// <param name="volume">Windows audio-taper scalar, 0-1.</param>
    EndpointVolumeState SetVolume(float volume);

    /// <summary>Writes device-wide Windows mute and returns its actual readback; rejects the current primary.</summary>
    /// <param name="muted">Whether to mute this secondary endpoint.</param>
    EndpointVolumeState SetMute(bool muted);
}
