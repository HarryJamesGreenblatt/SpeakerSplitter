using System.Globalization;

namespace SpeakerSplitter;

// The console's peak meter row.
public static class MeterBar
{
    // One fixed-width line, so overwriting it with '\r' never leaves stray characters behind.
    public static string Render(float level, int width = 40)
    {
        var db = level > 0f ? 20f * MathF.Log10(level) : float.NegativeInfinity;
        var clamped = Math.Clamp(db, PeakMeter.FloorDb, 0f);
        var filled = (int)MathF.Round((clamped - PeakMeter.FloorDb) / -PeakMeter.FloorDb * width);
        var readout = db <= PeakMeter.FloorDb ? "-inf" : db.ToString("F1", CultureInfo.InvariantCulture);

        return $"[{new string('#', filled)}{new string(' ', width - filled)}] {readout,6} dB";
    }
}
