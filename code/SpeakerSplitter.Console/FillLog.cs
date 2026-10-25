using System.Globalization;

namespace SpeakerSplitter;

public static class FillLog
{
    // One line per device, for example "[12:00:01] Speakers (USB): fill 42% (84 ms) under=0 over=0".
    public static string Format(DateTime time, string device, BufferStats stats) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[{time:HH:mm:ss}] {device}: fill {stats.FillFraction * 100:F0}% ({stats.FillMilliseconds:F0} ms) under={stats.Underruns} over={stats.Overflows}");
}
