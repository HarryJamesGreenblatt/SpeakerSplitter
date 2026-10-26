using System.Globalization;

namespace SpeakerSplitter;

/// <summary>Formats the once-per-second buffer line the console prints for each device.</summary>
public static class FillLog
{
    /// <summary>
    /// Formats one device's line, for example <c>[12:00:01] Speakers (USB): fill 42% (84 ms) under=0 over=0</c>.
    /// Culture-invariant, so the output is the same on every machine.
    /// </summary>
    /// <param name="time">The time to stamp the line with.</param>
    /// <param name="device">The device's name.</param>
    /// <param name="stats">The device's buffer statistics.</param>
    public static string Format(DateTime time, string device, BufferStats stats) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{time:HH:mm:ss}] {device}: fill {stats.FillFraction * 100:F0}% ({stats.FillMilliseconds:F0} ms) under={stats.Underruns} over={stats.Overflows}");
}
