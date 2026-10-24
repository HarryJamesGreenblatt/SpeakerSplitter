using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace SpeakerSplitter;

public sealed record EndpointInfo(string Id, string FriendlyName, bool IsDefault, MixFormatInfo? Format);

public static class EndpointCatalog
{
    public static IReadOnlyList<EndpointInfo> ListActive()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = GetDefaultRenderId(enumerator);

        // DeviceState.Active excludes disabled, unplugged and not-present endpoints.
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

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

    // Activating the audio client can fail per device (busy, exclusive mode, driver fault).
    // A null result is shown as n/a so one bad device never hides the others.
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

    // Role.Multimedia is the role WasapiLoopbackCapture uses for its default device,
    // so the endpoint flagged here is the one M2 will capture. Role.Console can differ.
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
