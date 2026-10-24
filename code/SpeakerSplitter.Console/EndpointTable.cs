namespace SpeakerSplitter;

public static class EndpointTable
{
    private static readonly string[] Headers = ["Name", "Default", "Rate", "Ch", "Bits", "Encoding"];
    private const string Unavailable = "n/a";

    public static void Write(TextWriter writer, IReadOnlyList<EndpointInfo> endpoints)
    {
        var rows = endpoints.Select(endpoint => new[]
        {
            endpoint.FriendlyName,
            endpoint.IsDefault ? "yes" : "",
            endpoint.Format?.SampleRate.ToString() ?? Unavailable,
            endpoint.Format?.Channels.ToString() ?? Unavailable,
            endpoint.Format?.BitsPerSample.ToString() ?? Unavailable,
            endpoint.Format?.Encoding ?? Unavailable,
        }).ToList();

        var widths = Headers.Select((header, column) =>
            rows.Select(row => row[column].Length).Append(header.Length).Max()).ToArray();

        writer.WriteLine(FormatRow(Headers, widths));
        writer.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));
        foreach (var row in rows)
        {
            writer.WriteLine(FormatRow(row, widths));
        }
    }

    private static string FormatRow(string[] cells, int[] widths) =>
        string.Join(" | ", cells.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd();
}
