using System.Globalization;
using SpeakerSplitter.Capture;

namespace SpeakerSplitter.Display;

/// <summary>Draws the console's peak meter row.</summary>
public static class MeterBar
{
    /// <summary>
    /// Renders one fixed-width line, so overwriting it with <c>\r</c> never leaves stray characters behind.
    /// The bar spans <see cref="PeakMeter.FloorDb"/> to 0 dB, followed by the level as a dB readout.
    /// </summary>
    /// <param name="level">The peak on a 0..1 scale, where 1.0 is digital full scale.</param>
    /// <param name="width">The number of characters between the brackets.</param>
    public static string Render(float level, int width = 40)
    {
        // Convert to dB, clamp to the meter's range, and map that range onto the bar's width.
        var db = level > 0f ? 20f * MathF.Log10(level) : float.NegativeInfinity;
        var clamped = Math.Clamp(db, PeakMeter.FloorDb, 0f);
        var filled = (int)MathF.Round((clamped - PeakMeter.FloorDb) / -PeakMeter.FloorDb * width);
        var readout = db <= PeakMeter.FloorDb ? "-inf" : db.ToString("F1", CultureInfo.InvariantCulture);

        return $"[{new string('#', filled)}{new string(' ', width - filled)}] {readout,6} dB";
    }
}
