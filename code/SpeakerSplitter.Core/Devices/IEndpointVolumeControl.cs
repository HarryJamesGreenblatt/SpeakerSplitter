namespace SpeakerSplitter.Devices;

/// <summary>An owned device-wide control handle; callers read and release it on their lifecycle thread.</summary>
public interface IEndpointVolumeControl : IDisposable
{
    /// <summary>Reads current Windows settings; failures are reported rather than substituted with defaults.</summary>
    EndpointVolumeState GetState();
}
