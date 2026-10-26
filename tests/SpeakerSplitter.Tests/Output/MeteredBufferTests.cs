using NAudio.Wave;
using SpeakerSplitter.Output;

namespace SpeakerSplitter.Tests.Output;

public class MeteredBufferTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private static MeteredBuffer NewBuffer() => new(Format, TimeSpan.FromMilliseconds(10));

    [Fact]
    public void Read_CountsEachZeroFilledReadAsOneUnderrun_AndPadsWithSilence()
    {
        var buffer = NewBuffer();
        buffer.AddSamples([1, 2, 3, 4], 0, 4);

        var satisfied = new byte[4];
        Assert.Equal(4, buffer.Read(satisfied, 0, 4));
        Assert.Equal(0, buffer.Stats.Underruns);

        var starved = Enumerable.Repeat((byte)255, 8).ToArray();
        Assert.Equal(8, buffer.Read(starved, 0, 8));
        Assert.Equal(8, buffer.Read(starved, 0, 8));

        Assert.Equal(new byte[8], starved);
        Assert.Equal(2, buffer.Stats.Underruns);
    }

    [Fact]
    public void Read_CountsAPartiallyFilledReadAsAnUnderrun()
    {
        var buffer = NewBuffer();
        buffer.AddSamples([1, 2, 3, 4], 0, 4);

        var actual = Enumerable.Repeat((byte)255, 8).ToArray();
        Assert.Equal(8, buffer.Read(actual, 0, 8));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 }, actual);
        Assert.Equal(1, buffer.Stats.Underruns);
    }

    [Fact]
    public void AddSamples_CountsOnlyWritesThatDropData_AndRecoversOnceDrained()
    {
        var buffer = NewBuffer();
        var capacity = buffer.Stats.BufferLength;

        buffer.AddSamples(new byte[capacity], 0, capacity);
        Assert.Equal(0, buffer.Stats.Overflows);

        buffer.AddSamples(new byte[8], 0, 8);
        buffer.AddSamples(new byte[8], 0, 8);
        Assert.Equal(2, buffer.Stats.Overflows);
        Assert.Equal(capacity, buffer.Stats.BufferedBytes);

        buffer.Read(new byte[capacity], 0, capacity);
        buffer.AddSamples(new byte[8], 0, 8);
        Assert.Equal(2, buffer.Stats.Overflows);
        Assert.Equal(0, buffer.Stats.Underruns);
    }
}
