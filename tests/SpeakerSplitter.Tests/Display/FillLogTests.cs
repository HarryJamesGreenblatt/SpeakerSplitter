using SpeakerSplitter.Display;
using SpeakerSplitter.Output;

namespace SpeakerSplitter.Tests.Display;

public class FillLogTests
{
    [Fact]
    public void Format_ShowsTimeDeviceFillPercentAndMilliseconds()
    {
        // 300 ms of 48 kHz stereo float is 115,200 bytes; 32,256 of them is 28% or 84 ms.
        var stats = new BufferStats(32_256, 115_200, 384_000, Underruns: 2, Overflows: 1);

        var line = FillLog.Format(new DateTime(2026, 10, 25, 12, 0, 1), "Speakers (USB)", stats);

        Assert.Equal("[12:00:01] Speakers (USB): fill 28% (84 ms) under=2 over=1", line);
    }

    [Fact]
    public void Format_ShowsAnEmptyBufferAsZero()
    {
        var line = FillLog.Format(new DateTime(2026, 10, 25, 12, 0, 2), "Monitor", new BufferStats(0, 115_200, 384_000, 0, 0));

        Assert.Equal("[12:00:02] Monitor: fill 0% (0 ms) under=0 over=0", line);
    }
}
