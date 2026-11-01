namespace SpeakerSplitter.Devices;

/// <summary>A Windows endpoint's device-wide settings and reported hardware support, not calibrated loudness.</summary>
/// <param name="Volume">Windows audio-taper volume scalar from 0 to 1.</param>
/// <param name="Muted">Whether Windows mutes this endpoint.</param>
/// <param name="HardwareVolume">Whether Windows reports hardware volume support.</param>
/// <param name="HardwareMute">Whether Windows reports hardware mute support.</param>
public sealed record EndpointVolumeState(float Volume, bool Muted, bool HardwareVolume, bool HardwareMute);
