using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Display;

/// <summary>Prints the active endpoints as an aligned table.</summary>
public static class EndpointTable
{
    #region Constants

    /// <summary>What a cell shows when the endpoint's mix format could not be read.</summary>
    private const string Unavailable = "n/a";

    #endregion

    #region Fields

    /// <summary>The column headings, in column order.</summary>
    private static readonly string[] Headers = ["#", "Name", "Default", "Rate", "Ch", "Bits", "Encoding"];

    #endregion

    #region Methods

    /// <summary>
    /// Writes the table. The number in the first column is the 1-based position in <paramref name="endpoints"/>,
    /// which is what <see cref="DeviceSelection"/> accepts.
    /// </summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="endpoints">The endpoints to list.</param>
    public static void Write(TextWriter writer, IReadOnlyList<EndpointInfo> endpoints)
    {
        // Turn each endpoint into a row of cell text first, so the column widths can be measured.
        var rows = endpoints.Select((endpoint, index) => new[]
        {
            (index + 1).ToString(),
            endpoint.FriendlyName,
            endpoint.IsDefault ? "yes" : "",
            endpoint.Format?.SampleRate.ToString() ?? Unavailable,
            endpoint.Format?.Channels.ToString() ?? Unavailable,
            endpoint.Format?.BitsPerSample.ToString() ?? Unavailable,
            endpoint.Format?.Encoding ?? Unavailable,
        }).ToList();

        // Each column is as wide as its longest cell or its heading.
        var widths = Headers.Select((header, column) =>
            rows.Select(row => row[column].Length).Append(header.Length).Max()).ToArray();

        writer.WriteLine(FormatRow(Headers, widths));
        writer.WriteLine(string.Join("-+-", widths.Select(width => new string('-', width))));
        foreach (var row in rows)
        {
            writer.WriteLine(FormatRow(row, widths));
        }
    }

    /// <summary>Pads each cell to its column's width and joins them with separators, without trailing spaces.</summary>
    /// <param name="cells">The cell text, in column order.</param>
    /// <param name="widths">The width of each column.</param>
    private static string FormatRow(string[] cells, int[] widths) =>
        string.Join(" | ", cells.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd();

    #endregion
}
