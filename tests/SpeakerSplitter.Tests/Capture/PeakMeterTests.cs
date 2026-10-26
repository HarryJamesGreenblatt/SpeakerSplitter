using System.Runtime.InteropServices;
using SpeakerSplitter.Capture;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Display;

namespace SpeakerSplitter.Tests.Capture;

public class PeakMeterTests
{
    private static readonly MixFormatInfo Float32 = new(48000, 2, 32, "IeeeFloat");
    private static readonly MixFormatInfo Pcm16 = new(48000, 2, 16, "Pcm");

    [Fact]
    public void Compute_Float32_ReturnsLargestAbsoluteSample()
    {
        var buffer = MemoryMarshal.AsBytes(new[] { 0.1f, -0.6f, 0.4f, 0.2f }.AsSpan());

        Assert.Equal(0.6f, PeakMeter.Compute(buffer, Float32));
    }

    [Fact]
    public void Compute_Pcm16_ScalesToUnitRange()
    {
        var buffer = MemoryMarshal.AsBytes(new short[] { 1000, -16384, 8000, 0 }.AsSpan());

        Assert.Equal(0.5f, PeakMeter.Compute(buffer, Pcm16));
    }

    [Fact]
    public void Compute_Silence_ReturnsZero()
    {
        var floats = MemoryMarshal.AsBytes(new float[8].AsSpan());
        var shorts = MemoryMarshal.AsBytes(new short[8].AsSpan());

        Assert.Equal(0f, PeakMeter.Compute(floats, Float32));
        Assert.Equal(0f, PeakMeter.Compute(shorts, Pcm16));
        Assert.Equal(0f, PeakMeter.Compute(ReadOnlySpan<byte>.Empty, Float32));
    }

    [Fact]
    public void Compute_FullScale_ReturnsOne()
    {
        var floats = MemoryMarshal.AsBytes(new[] { 0f, -1f }.AsSpan());
        var shorts = MemoryMarshal.AsBytes(new[] { (short)0, short.MinValue }.AsSpan());

        Assert.Equal(1f, PeakMeter.Compute(floats, Float32));
        Assert.Equal(1f, PeakMeter.Compute(shorts, Pcm16));
    }

    [Fact]
    public void Compute_UnsupportedFormat_Throws()
    {
        var format = new MixFormatInfo(48000, 2, 24, "Pcm");

        Assert.False(PeakMeter.IsSupported(format));
        Assert.Throws<NotSupportedException>(() => PeakMeter.Compute(new byte[12], format));
    }

    [Fact]
    public void Render_ShowsEmptyBarAtSilenceAndFullBarAtFullScale()
    {
        var silent = MeterBar.Render(0f);
        var full = MeterBar.Render(1f);

        Assert.Equal($"[{new string(' ', 40)}]   -inf dB", silent);
        Assert.Equal($"[{new string('#', 40)}]    0.0 dB", full);
        Assert.Equal(silent.Length, MeterBar.Render(0.05f).Length);
    }

    [Fact]
    public void LevelMeter_AttacksInstantlyAndDecaysToIdleWithinAboutOneSecond()
    {
        var clock = new ManualTimeProvider();
        var meter = new LevelMeter(clock);

        meter.Push(1f);
        Assert.Equal(1f, meter.Level, 3);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(MathF.Pow(10f, -30f / 20f), meter.Level, 3);

        meter.Push(0.9f);
        Assert.Equal(0.9f, meter.Level, 3);

        clock.Advance(TimeSpan.FromMilliseconds(1100));
        Assert.Equal("[" + new string(' ', 40) + "]   -inf dB", MeterBar.Render(meter.Level));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += (long)by.TotalMilliseconds;
    }
}
