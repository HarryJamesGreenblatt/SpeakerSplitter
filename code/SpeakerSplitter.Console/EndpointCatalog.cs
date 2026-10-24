using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeakerSplitter;

public sealed record MixFormatInfo(int SampleRate, int Channels, int BitsPerSample, string Encoding);

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
            var format = device.AudioClient.MixFormat;
            return new MixFormatInfo(format.SampleRate, format.Channels, format.BitsPerSample, DescribeEncoding(format));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Shared-mode mix formats are usually WAVE_FORMAT_EXTENSIBLE, whose Encoding is only "Extensible";
    // the SubFormat says whether samples are PCM or float.
    private static string DescribeEncoding(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible)
        {
            return format.Encoding.ToString();
        }

        var standard = extensible.ToStandardWaveFormat();
        return standard.Encoding != WaveFormatEncoding.Extensible
            ? standard.Encoding.ToString()
            : extensible.SubFormat.ToString();
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
