using System.Globalization;

namespace SpeakerSplitter;

public sealed record SelectionResult(IReadOnlyList<EndpointInfo> Accepted, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class DeviceSelection
{
    // A request is the 1-based number EndpointTable prints, or a case-insensitive part of a device ID.
    // The default endpoint is the one loopback captures. An output on it would double or feed back the
    // audio, so it is refused here, before any output exists.
    public static SelectionResult Select(IReadOnlyList<EndpointInfo> endpoints, IEnumerable<string> requests)
    {
        var accepted = new List<EndpointInfo>();
        var errors = new List<string>();

        foreach (var request in requests)
        {
            var (device, error) = Resolve(endpoints, request.Trim());
            if (device is null)
            {
                errors.Add(error!);
            }
            else if (device.IsDefault)
            {
                errors.Add(
                    $"{device.FriendlyName} is the default output device, which SpeakerSplitter captures. " +
                    "Mirroring to it would double or feed back the audio. Choose a different device.");
            }
            else if (accepted.Contains(device))
            {
                errors.Add($"{device.FriendlyName} was selected more than once.");
            }
            else
            {
                accepted.Add(device);
            }
        }

        return new SelectionResult(accepted, errors);
    }

    // Splits what a user typed at the prompt ("2,3" or "2 3") into separate requests.
    public static IReadOnlyList<string> SplitList(string? line) =>
        (line ?? "").Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static (EndpointInfo? Device, string? Error) Resolve(IReadOnlyList<EndpointInfo> endpoints, string request)
    {
        if (request.Length == 0)
        {
            return (null, "A device was selected with an empty value.");
        }

        if (int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number >= 1 && number <= endpoints.Count
                ? (endpoints[number - 1], null)
                : (null, $"There is no device number {number}. Choose a number from 1 to {endpoints.Count}.");
        }

        var matches = endpoints
            .Where(endpoint => endpoint.Id.Contains(request, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, $"No active device matches '{request}'. Disabled and unplugged devices are not listed."),
            _ => (null, $"'{request}' matches {matches.Count} devices. Use its number or a longer part of its ID."),
        };
    }
}
