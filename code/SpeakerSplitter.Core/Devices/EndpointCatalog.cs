using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace SpeakerSplitter.Devices;

/// <summary>One active render endpoint as listed by <see cref="EndpointCatalog"/>.</summary>
/// <param name="Id">The endpoint's stable device ID.</param>
/// <param name="FriendlyName">The name Windows shows in its Sound settings.</param>
/// <param name="IsDefault">True for the default multimedia render endpoint, the one loopback captures.</param>
/// <param name="Format">The endpoint's mix format, or <see langword="null"/> when it could not be read.</param>
public sealed record EndpointInfo(string Id, string FriendlyName, bool IsDefault, MixFormatInfo? Format);

/// <summary>Lists the render endpoints a session can capture from or mirror to.</summary>
public static class EndpointCatalog
{
    /// <summary>
    /// Lists every active render endpoint, flagging the default one. A device whose mix format cannot be read
    /// is still listed, with a <see langword="null"/> format.
    /// </summary>
    public static IReadOnlyList<EndpointInfo> ListActive()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = GetDefaultRenderId(enumerator);

        // DeviceState.Active excludes disabled, unplugged and not-present endpoints.
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        // Read each device's format while it is open, then release it before moving on.
        var endpoints = new List<EndpointInfo>(devices.Count);
        foreach (var device in devices)
        {
            using (device)
            {
                endpoints.Add(new EndpointInfo(device.ID, device.FriendlyName, device.ID == defaultId, ReadMixFormat(device)));
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Reads a device's mix format. Activating the audio client can fail per device (busy, exclusive mode,
    /// driver fault), so a failure yields <see langword="null"/> and one bad device never hides the others.
    /// </summary>
    /// <param name="device">The device to read.</param>
    private static MixFormatInfo? ReadMixFormat(MMDevice device)
    {
        try
        {
            return MixFormatInfo.From(device.AudioClient.MixFormat);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the default render endpoint's ID, or <see langword="null"/> when there is none. It asks for
    /// <see cref="Role.Multimedia"/>, the role <c>WasapiLoopbackCapture</c> uses, so the endpoint flagged here is
    /// the one that gets captured; <see cref="Role.Console"/> can differ.
    /// </summary>
    /// <param name="enumerator">The enumerator to query.</param>
    private static string? GetDefaultRenderId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.ID;
        }
        catch (COMException)
        {
            // Windows throws when no active render endpoint exists; nothing is flagged.
            return null;
        }
    }
}
