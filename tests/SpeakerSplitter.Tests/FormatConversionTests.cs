using System.Runtime.InteropServices;
using NAudio.Wave;

namespace SpeakerSplitter.Tests;

public class FormatConversionTests
{
    private static readonly WaveFormat Capture = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    [Theory]
    [InlineData(44100, 2, false)]
    [InlineData(48000, 2, true)]
    [InlineData(44100, 1, true)]
    [InlineData(44100, 6, true)]
    [InlineData(96000, 6, true)]
    public void NeedsConversion_WhenTheSampleRateOrChannelCountDiffers(int deviceRate, int deviceChannels, bool expected)
    {
        var device = new MixFormatInfo(deviceRate, deviceChannels, 32, "IeeeFloat");

        Assert.Equal(expected, FormatConversion.NeedsConversion(Capture, device));
    }

    [Fact]
    public void Create_ProducesFloatAtTheDeviceRateAndChannelCount()
    {
        var source = new BufferedWaveProvider(Capture) { ReadFully = true };

        var converted = FormatConversion.Create(source, new MixFormatInfo(48000, 1, 32, "IeeeFloat"));

        Assert.Equal(WaveFormatEncoding.IeeeFloat, converted.WaveFormat.Encoding);
        Assert.Equal(48000, converted.WaveFormat.SampleRate);
        Assert.Equal(1, converted.WaveFormat.Channels);
        Assert.Equal(32, converted.WaveFormat.BitsPerSample);
    }

    [Fact]
    public void Create_CentresMonoOnBothStereoChannels()
    {
        var actual = ConvertSamples(1, [0.25f, -0.5f], 2, frames: 2);

        Assert.Equal([0.25f, 0.25f, -0.5f, -0.5f], actual);
    }

    [Fact]
    public void Create_FoldsStereoDownToTheAverageOfBothSides()
    {
        var actual = ConvertSamples(2, [0.5f, -0.25f, 1f, 0f], 1, frames: 2);

        Assert.Equal([0.125f, 0.5f], actual);
    }

    [Fact]
    public void Create_KeepsFrontLeftAndRightAndLeavesExtraChannelsSilent()
    {
        var actual = ConvertSamples(2, [0.5f, -0.25f], 6, frames: 1);

        Assert.Equal([0.5f, -0.25f, 0f, 0f, 0f, 0f], actual);
    }

    [Fact]
    public void Create_DropsChannelsBeyondTheDevicesLayout()
    {
        var actual = ConvertSamples(6, [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f], 2, frames: 1);

        Assert.Equal([0.1f, 0.2f], actual);
    }

    [Theory]
    [InlineData(44100, 48000, 1000)]
    [InlineData(48000, 44100, 440)]
    [InlineData(44100, 96000, 5000)]
    public void Create_KeepsPitchAndSpeedAcrossSampleRates(int captureRate, int deviceRate, double tone)
    {
        const double Seconds = 1.0;
        var frames = (int)(captureRate * Seconds);
        var input = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            input[2 * i] = input[(2 * i) + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * tone * i / captureRate));
        }

        var bytes = MemoryMarshal.AsBytes(input.AsSpan()).ToArray();
        var source = new RawSourceWaveStream(bytes, 0, bytes.Length, WaveFormat.CreateIeeeFloatWaveFormat(captureRate, 2));

        var left = ReadLeftChannel(FormatConversion.Create(source, new MixFormatInfo(deviceRate, 2, 32, "IeeeFloat")));

        // Speed: the converted audio lasts as long as the original. Pitch: the tone keeps its frequency.
        Assert.InRange(left.Length / (double)deviceRate, Seconds - 0.01, Seconds + 0.01);
        Assert.InRange(MeasureFrequency(left, deviceRate), tone - 1, tone + 1);
    }

    private static float[] ReadLeftChannel(IWaveProvider provider)
    {
        var channels = provider.WaveFormat.Channels;
        using var all = new MemoryStream();
        var chunk = new byte[provider.WaveFormat.AverageBytesPerSecond];
        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
        {
            all.Write(chunk, 0, read);
        }

        var samples = MemoryMarshal.Cast<byte, float>(all.ToArray());
        var left = new float[samples.Length / channels];
        for (var frame = 0; frame < left.Length; frame++)
        {
            left[frame] = samples[frame * channels];
        }

        return left;
    }

    // Counts positive-going zero crossings with linear interpolation, skipping 100 ms at each edge.
    private static double MeasureFrequency(float[] samples, int rate)
    {
        var edge = rate / 10;
        List<double> crossings = [];
        for (var i = edge + 1; i < samples.Length - edge; i++)
        {
            if (samples[i - 1] < 0 && samples[i] >= 0)
            {
                crossings.Add(i - 1 + (-samples[i - 1] / (samples[i] - samples[i - 1])));
            }
        }

        return (crossings.Count - 1) * rate / (crossings[^1] - crossings[0]);
    }

    // Reads into a buffer holding stale data, so a channel the converter fails to write cannot pass as silence.
    private static float[] ConvertSamples(int sourceChannels, float[] input, int deviceChannels, int frames)
    {
        var source = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, sourceChannels)) { ReadFully = true };
        var bytes = MemoryMarshal.AsBytes(input.AsSpan()).ToArray();
        source.AddSamples(bytes, 0, bytes.Length);
        var converted = FormatConversion.Create(source, new MixFormatInfo(44100, deviceChannels, 32, "IeeeFloat"));

        var raw = new byte[frames * deviceChannels * sizeof(float)];
        Array.Fill(raw, (byte)0x3F);
        Assert.Equal(raw.Length, converted.Read(raw, 0, raw.Length));
        return MemoryMarshal.Cast<byte, float>(raw).ToArray();
    }
}
