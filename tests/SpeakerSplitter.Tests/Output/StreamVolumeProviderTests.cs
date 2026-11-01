using System.Runtime.InteropServices;
using NAudio.Wave;
using SpeakerSplitter.Output;

namespace SpeakerSplitter.Tests.Output;

public class StreamVolumeProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_UnityGainPreservesTheOriginalFormatAndBytes(bool pcm)
    {
        var format = pcm ? new WaveFormat(48000, 16, 2) : WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var source = new MeteredBuffer(format, TimeSpan.FromMilliseconds(20));
        byte[] packet = pcm
            ? MemoryMarshal.AsBytes(new short[] { short.MinValue, short.MaxValue, -1, 12345 }.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(new float[] { -1, 0.8f, 0.001f, -0.25f }.AsSpan()).ToArray();
        source.AddSamples(packet, 0, packet.Length);
        var gain = new StreamVolumeProvider(source);
        var actual = new byte[packet.Length];

        Assert.Equal(actual.Length, gain.Read(actual, 0, actual.Length));

        Assert.Same(format, gain.WaveFormat);
        Assert.Equal(packet, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_InitialGainIsAppliedToTheFirstFrameAndOnlyTheRequestedBufferRange(bool pcm)
    {
        var format = pcm ? new WaveFormat(48000, 16, 2) : WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var source = new MeteredBuffer(format, TimeSpan.FromMilliseconds(20));
        byte[] packet = pcm
            ? MemoryMarshal.AsBytes(new short[] { 20000, -20000 }.AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(new float[] { 0.8f, -0.4f }.AsSpan()).ToArray();
        source.AddSamples(packet, 0, packet.Length);
        var gain = new StreamVolumeProvider(source, 0.25f);
        var actual = Enumerable.Repeat((byte)99, packet.Length + 8).ToArray();

        gain.Read(actual, 4, packet.Length);

        Assert.All(actual[..4], value => Assert.Equal((byte)99, value));
        Assert.All(actual[(packet.Length + 4)..], value => Assert.Equal((byte)99, value));
        if (pcm)
        {
            Assert.Equal(new short[] { 5000, -5000 },
                MemoryMarshal.Cast<byte, short>(actual.AsSpan(4, packet.Length)).ToArray());
        }
        else
        {
            Assert.Equal(new float[] { 0.2f, -0.1f },
                MemoryMarshal.Cast<byte, float>(actual.AsSpan(4, packet.Length)).ToArray());
        }
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Volume_InvalidGainIsRejectedWithoutChangingThePreviousSetting(float value)
    {
        var source = new MeteredBuffer(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2), TimeSpan.FromMilliseconds(20));
        var gain = new StreamVolumeProvider(source, 0.5f);

        Assert.Throws<ArgumentOutOfRangeException>(() => gain.Volume = value);
        Assert.Equal(0.5f, gain.Volume);
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamVolumeProvider(source, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_LiveGainChangesRampByFrameAcrossReadBoundaries(bool pcm)
    {
        var format = pcm ? new WaveFormat(1000, 16, 2) : WaveFormat.CreateIeeeFloatWaveFormat(1000, 2);
        var source = new MeteredBuffer(format, TimeSpan.FromMilliseconds(20));
        var packet = pcm
            ? MemoryMarshal.AsBytes(Enumerable.Range(0, 10).Select(i => (short)(i % 2 == 0 ? 10000 : -10000)).ToArray().AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(Enumerable.Range(0, 10).Select(i => i % 2 == 0 ? 1f : -1f).ToArray().AsSpan()).ToArray();
        source.AddSamples(packet, 0, packet.Length);
        var gain = new StreamVolumeProvider(source);
        gain.Volume = 0.5f;
        var actual = new byte[packet.Length];

        gain.Read(actual, 0, 2 * format.BlockAlign);
        gain.Read(actual, 2 * format.BlockAlign, 3 * format.BlockAlign);

        float[] expected = [0.9f, 0.8f, 0.7f, 0.6f, 0.5f];
        for (var frame = 0; frame < expected.Length; frame++)
        {
            if (pcm)
            {
                var samples = MemoryMarshal.Cast<byte, short>(actual);
                Assert.InRange(Math.Abs(samples[frame * 2] - expected[frame] * 10000), 0, 1);
                Assert.Equal(-samples[frame * 2], samples[frame * 2 + 1]);
            }
            else
            {
                var samples = MemoryMarshal.Cast<byte, float>(actual);
                Assert.Equal(expected[frame], samples[frame * 2], 5);
                Assert.Equal(-samples[frame * 2], samples[frame * 2 + 1]);
            }
        }
    }

    [Fact]
    public void Read_RetargetsAnUnfinishedRampFromItsCurrentGain()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(1000, 1);
        var source = new MeteredBuffer(format, TimeSpan.FromMilliseconds(20));
        var packet = MemoryMarshal.AsBytes(Enumerable.Repeat(1f, 7).ToArray().AsSpan()).ToArray();
        source.AddSamples(packet, 0, packet.Length);
        var gain = new StreamVolumeProvider(source);
        gain.Volume = 0f;
        gain.Read(new byte[8], 0, 8);
        gain.Volume = 1f;
        var actual = new byte[20];

        gain.Read(actual, 0, actual.Length);

        var samples = MemoryMarshal.Cast<byte, float>(actual).ToArray();
        float[] expected = [0.68f, 0.76f, 0.84f, 0.92f, 1f];
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.Equal(expected[i], samples[i], 5);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_ZeroGainDrainsAudioAndUnmutingDoesNotReplayOldPackets(bool pcm)
    {
        var format = pcm ? new WaveFormat(1000, 16, 2) : WaveFormat.CreateIeeeFloatWaveFormat(1000, 2);
        var source = new MeteredBuffer(format, TimeSpan.FromMilliseconds(30));
        var oldPacket = pcm
            ? MemoryMarshal.AsBytes(Enumerable.Repeat((short)20000, 20).ToArray().AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(Enumerable.Repeat(0.8f, 20).ToArray().AsSpan()).ToArray();
        source.AddSamples(oldPacket, 0, oldPacket.Length);
        var gain = new StreamVolumeProvider(source, 0f);
        var silent = Enumerable.Repeat((byte)99, oldPacket.Length).ToArray();

        Assert.Equal(silent.Length, gain.Read(silent, 0, silent.Length));

        Assert.All(silent, sample => Assert.Equal((byte)0, sample));
        Assert.Equal(0, source.Stats.BufferedBytes);
        var newPacket = pcm
            ? MemoryMarshal.AsBytes(Enumerable.Repeat((short)5000, 20).ToArray().AsSpan()).ToArray()
            : MemoryMarshal.AsBytes(Enumerable.Repeat(0.2f, 20).ToArray().AsSpan()).ToArray();
        source.AddSamples(newPacket, 0, newPacket.Length);
        gain.Volume = 1f;
        var audible = new byte[newPacket.Length];
        Assert.Equal(audible.Length, gain.Read(audible, 0, audible.Length));
        if (pcm)
        {
            Assert.Equal(5000, MemoryMarshal.Cast<byte, short>(audible)[^1]);
        }
        else
        {
            Assert.Equal(0.2f, MemoryMarshal.Cast<byte, float>(audible)[^1], 5);
        }
    }

    [Fact]
    public void Read_ZeroGainClearsNonFiniteFloatSamplesInsteadOfMultiplyingThem()
    {
        var source = new MeteredBuffer(WaveFormat.CreateIeeeFloatWaveFormat(1000, 2), TimeSpan.FromMilliseconds(20));
        var packet = MemoryMarshal.AsBytes(new float[] { float.NaN, float.PositiveInfinity }.AsSpan()).ToArray();
        source.AddSamples(packet, 0, packet.Length);
        var gain = new StreamVolumeProvider(source, 0f);
        var actual = new byte[packet.Length];

        Assert.Equal(actual.Length, gain.Read(actual, 0, actual.Length));

        Assert.All(actual, value => Assert.Equal((byte)0, value));
    }
}
