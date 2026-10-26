using System.Globalization;

namespace SpeakerSplitter;

/// <summary>The outcome of turning a user's device requests into endpoints.</summary>
/// <param name="Accepted">The endpoints that were chosen, in the order requested.</param>
/// <param name="Errors">One readable message per request that could not be honoured.</param>
public sealed record SelectionResult(IReadOnlyList<EndpointInfo> Accepted, IReadOnlyList<string> Errors)
{
    /// <summary>True when every request was honoured.</summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>Turns what a user typed or passed on the command line into the endpoints to mirror to.</summary>
public static class DeviceSelection
{
    /// <summary>
    /// Resolves each request to an endpoint. A request is the 1-based number <see cref="EndpointTable"/> prints,
    /// or a case-insensitive part of a device ID. The default endpoint is the one loopback captures, so an
    /// output on it would double or feed back the audio; it is refused here, before any output exists.
    /// </summary>
    /// <param name="endpoints">The active endpoints, in the order the table lists them.</param>
    /// <param name="requests">What the user asked for.</param>
    public static SelectionResult Select(IReadOnlyList<EndpointInfo> endpoints, IEnumerable<string> requests)
    {
        var accepted = new List<EndpointInfo>();
        var errors = new List<string>();

        // Check every request and keep going after a bad one, so the user sees all the problems at once.
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

    /// <summary>Splits what a user typed at the prompt (<c>2,3</c> or <c>2 3</c>) into separate requests.</summary>
    /// <param name="line">The line typed, or <see langword="null"/> when input ended.</param>
    public static IReadOnlyList<string> SplitList(string? line) =>
        (line ?? "").Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Finds the endpoint one request refers to: by its table number, or by a part of its ID that matches
    /// exactly one device.
    /// </summary>
    /// <param name="endpoints">The active endpoints, in the order the table lists them.</param>
    /// <param name="request">The trimmed request.</param>
    /// <returns>The endpoint, or <see langword="null"/> with a readable error.</returns>
    private static (EndpointInfo? Device, string? Error) Resolve(IReadOnlyList<EndpointInfo> endpoints, string request)
    {
        if (request.Length == 0)
        {
            return (null, "A device was selected with an empty value.");
        }

        // A number refers to a row of the table.
        if (int.TryParse(request, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return number >= 1 && number <= endpoints.Count
                ? (endpoints[number - 1], null)
                : (null, $"There is no device number {number}. Choose a number from 1 to {endpoints.Count}.");
        }

        // Anything else must identify exactly one device by part of its ID.
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
