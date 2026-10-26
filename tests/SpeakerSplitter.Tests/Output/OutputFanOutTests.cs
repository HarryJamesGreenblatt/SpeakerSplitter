using NAudio.Wave;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Output;

namespace SpeakerSplitter.Tests.Output;

public class OutputFanOutTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private static readonly EndpointInfo Headphones = new("headphones", "Headphones", false, null);
    private static readonly EndpointInfo Monitor = new("monitor", "Monitor", false, null);

    [Fact]
    public void AddSamples_CopiesEachPacketToIndependentDeviceBuffers()
    {
        var players = new List<FakePlayer>();
        using var outputs = new OutputFanOut(Format, "capture", [Headphones, Monitor], _ =>
        {
            var player = new FakePlayer();
            players.Add(player);
            return player;
        });
        outputs.Start();

        byte[] packet = [1, 2, 3, 4, 5, 6, 7, 8];
        outputs.AddSamples(packet, packet.Length);
        packet.AsSpan().Fill(99);
        outputs.AddSamples([9, 10, 11, 12, 13, 14, 15, 16, 255], 8);

        foreach (var player in players)
        {
            var provider = Assert.IsType<MeteredBuffer>(player.Provider);
            Assert.Same(Format, provider.WaveFormat);
            Assert.Equal(PlaybackState.Playing, player.PlaybackState);
            var actual = new byte[16];
            Assert.Equal(actual.Length, provider.Read(actual, 0, actual.Length));
            Assert.Equal(Enumerable.Range(1, 16).Select(i => (byte)i), actual);
        }
    }

    [Fact]
    public void AddSamples_ResamplesForADeviceWhoseRateDiffers()
    {
        var device = new EndpointInfo("usb", "USB speakers", false, new MixFormatInfo(44100, 2, 32, "IeeeFloat"));
        var player = new FakePlayer();
        using var outputs = new OutputFanOut(Format, "capture", [device], _ => player);
        outputs.Start();
        var packet = new byte[Format.AverageBytesPerSecond / 10];
        for (var i = 0; i < packet.Length; i += 4)
        {
            BitConverter.TryWriteBytes(packet.AsSpan(i), 0.5f);
        }

        outputs.AddSamples(packet, packet.Length);

        Assert.Equal(44100, player.Provider!.WaveFormat.SampleRate);
        var actual = new byte[4410 * 8];
        Assert.Equal(actual.Length, player.Provider.Read(actual, 0, actual.Length));
        Assert.Contains(actual, value => value != 0);
    }

    [Fact]
    public void Conversion_IsAppliedOnlyToTheMismatchedDevice()
    {
        var matching = new EndpointInfo("usb", "USB speakers", false, new MixFormatInfo(48000, 2, 32, "IeeeFloat"));
        var mismatched = new EndpointInfo("tv", "TV", false, new MixFormatInfo(44100, 2, 32, "IeeeFloat"));
        var unknown = new EndpointInfo("old", "Unknown format", false, null);
        var players = new Dictionary<string, FakePlayer> { ["usb"] = new(), ["tv"] = new(), ["old"] = new() };

        using var outputs = new OutputFanOut(
            Format, "capture", [matching, mismatched, unknown], endpoint => players[endpoint.Id]);

        Assert.IsType<MeteredBuffer>(players["usb"].Provider);
        Assert.IsType<MeteredBuffer>(players["old"].Provider);
        Assert.IsNotType<MeteredBuffer>(players["tv"].Provider);
        Assert.Equal(44100, players["tv"].Provider!.WaveFormat.SampleRate);
        Assert.Equal(["TV: converting 48000 Hz 2 ch -> 44100 Hz 2 ch"], outputs.Conversions);
    }

    [Fact]
    public void Snapshot_ReportsEveryDeviceByNameInSelectionOrder()
    {
        using var outputs = new OutputFanOut(Format, "capture", [Headphones, Monitor], _ => new FakePlayer());
        outputs.AddSamples(new byte[3840], 3840);

        var snapshot = outputs.Snapshot();

        Assert.Equal(["Headphones", "Monitor"], snapshot.Select(device => device.Name));
        Assert.All(snapshot, device =>
        {
            Assert.Equal(3840, device.Stats.BufferedBytes);
            Assert.Equal(Format.AverageBytesPerSecond, device.Stats.AverageBytesPerSecond);
            Assert.Equal(10, device.Stats.FillMilliseconds);
        });
    }

    [Fact]
    public void Snapshot_FillRisesWhileOutputIsStalled_FallsAsItDrains_AndStopsAtFull()
    {
        var player = new FakePlayer();
        using var outputs = new OutputFanOut(Format, "capture", [Headphones], _ => player);

        BufferStats Current() => outputs.Snapshot().Single().Stats;
        outputs.AddSamples(new byte[3840], 3840);
        var first = Current();
        outputs.AddSamples(new byte[3840], 3840);
        var stalled = Current();
        player.Provider!.Read(new byte[5760], 0, 5760);
        var drained = Current();
        outputs.AddSamples(new byte[stalled.BufferLength], stalled.BufferLength);
        var full = Current();

        Assert.Equal(10, first.FillMilliseconds);
        Assert.Equal(20, stalled.FillMilliseconds);
        Assert.True(stalled.FillFraction > first.FillFraction);
        Assert.Equal(5, drained.FillMilliseconds);
        Assert.Equal(1.0, full.FillFraction);
    }

    [Fact]
    public void Read_ZeroFillsAnUnderrun_WithoutStoppingPlayback()
    {
        var player = new FakePlayer();
        using var outputs = new OutputFanOut(Format, "capture", [Headphones], _ => player);
        outputs.Start();
        outputs.AddSamples([1, 2, 3, 4, 5, 6, 7, 8], 8);

        var actual = Enumerable.Repeat((byte)255, 16).ToArray();
        Assert.Equal(actual.Length, player.Provider!.Read(actual, 0, actual.Length));

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 0, 0, 0, 0, 0, 0 }, actual);
        Assert.Equal(PlaybackState.Playing, player.PlaybackState);
    }

    [Fact]
    public void AddSamples_DropsOverflowWithoutBlockingOtherDevices()
    {
        var stalled = new FakePlayer();
        var active = new FakePlayer();
        using var outputs = new OutputFanOut(
            Format, "capture", [Headphones, Monitor], endpoint => endpoint == Headphones ? stalled : active);
        var stalledBuffer = Assert.IsType<MeteredBuffer>(stalled.Provider);
        var activeBuffer = Assert.IsType<MeteredBuffer>(active.Provider);
        var packet = new byte[stalledBuffer.Stats.BufferLength];
        packet.AsSpan().Fill(42);
        outputs.AddSamples(packet, packet.Length);
        activeBuffer.Read(new byte[packet.Length], 0, packet.Length);

        outputs.AddSamples([1, 2, 3, 4, 5, 6, 7, 8], 8);

        Assert.Equal(stalledBuffer.Stats.BufferLength, stalledBuffer.Stats.BufferedBytes);
        Assert.Equal([1L, 0L], outputs.Snapshot().Select(device => device.Stats.Overflows));
        var actual = new byte[8];
        activeBuffer.Read(actual, 0, actual.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, actual);
    }

    [Theory]
    [InlineData("capture", false)]
    [InlineData("CAPTURE", false)]
    [InlineData("new-default", true)]
    public void Constructor_RejectsDefaultAndActualCaptureEndpoints_BeforeOpeningAnyOutput(string id, bool isDefault)
    {
        var opened = new List<string>();
        var forbidden = new EndpointInfo(id, "Forbidden device", isDefault, null);

        var error = Assert.Throws<ArgumentException>(() =>
            new OutputFanOut(Format, "capture", [Headphones, forbidden], endpoint =>
            {
                opened.Add(endpoint.Id);
                return new FakePlayer();
            }));

        Assert.Empty(opened);
        Assert.Contains("Forbidden device", error.Message);
        Assert.Contains("double or feed back", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_ReleasesEarlierOutputs_WhenLaterOutputCannotBeOpenedOrInitialized(bool failInit)
    {
        var first = new FakePlayer();
        var second = new FakePlayer { FailInit = failInit };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new OutputFanOut(Format, "capture", [Headphones, Monitor], endpoint =>
                endpoint == Headphones ? first :
                failInit ? second : throw new InvalidOperationException("device unavailable")));

        Assert.Contains("Monitor", error.Message);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(failInit ? 1 : 0, second.DisposeCalls);
    }

    [Fact]
    public void Dispose_ReleasesEveryOutputOnce_AfterPartialPlaybackStartup()
    {
        var first = new FakePlayer();
        var second = new FakePlayer { FailPlay = true };
        var outputs = new OutputFanOut(
            Format, "capture", [Headphones, Monitor], endpoint => endpoint == Headphones ? first : second);

        Assert.Throws<InvalidOperationException>(outputs.Start);
        outputs.Dispose();
        outputs.Dispose();

        Assert.Equal(1, first.StopCalls);
        Assert.Equal(1, second.StopCalls);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
        Assert.Equal(PlaybackState.Stopped, first.PlaybackState);
    }

    [Fact]
    public void Dispose_StillReleasesEveryOutput_WhenStopAndDisposeThrow()
    {
        var first = new FakePlayer { FailStop = true, FailDispose = true };
        var second = new FakePlayer();
        var outputs = new OutputFanOut(
            Format, "capture", [Headphones, Monitor], endpoint => endpoint == Headphones ? first : second);
        outputs.Start();

        var error = Assert.Throws<AggregateException>(outputs.Dispose);

        Assert.Equal(2, error.Flatten().InnerExceptions.Count);
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.StopCalls);
        Assert.Equal(1, second.DisposeCalls);
    }

    [Fact]
    public void Constructor_PreservesInitializationError_WhenCleanupAlsoFails()
    {
        var first = new FakePlayer { FailDispose = true };
        var second = new FakePlayer { FailInit = true };

        var error = Assert.Throws<AggregateException>(() =>
            new OutputFanOut(
                Format, "capture", [Headphones, Monitor], endpoint => endpoint == Headphones ? first : second));

        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message.Contains("Monitor"));
        Assert.Contains(error.Flatten().InnerExceptions, e => e.Message.Contains("dispose failed"));
        Assert.Equal(1, first.DisposeCalls);
        Assert.Equal(1, second.DisposeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lost_ReportsUnexpectedOutputStop_WithoutDisposingFromThePlaybackCallback(bool withError)
    {
        var player = new FakePlayer();
        using var outputs = new OutputFanOut(Format, "capture", [Headphones], _ => player);
        var reasons = new List<string>();
        outputs.Lost += reasons.Add;
        outputs.Start();

        player.EndPlayback(withError ? new InvalidOperationException("device removed") : null);

        var reason = Assert.Single(reasons);
        Assert.Contains("Headphones", reason);
        Assert.Contains(withError ? "device removed" : "unexpectedly", reason);
        Assert.Equal(0, player.DisposeCalls);
    }

    [Fact]
    public void Dispose_DetachesStoppedHandlers_AndDoesNotReportNormalStopAsLoss()
    {
        var player = new FakePlayer();
        var outputs = new OutputFanOut(Format, "capture", [Headphones], _ => player);
        var reasons = new List<string>();
        outputs.Lost += reasons.Add;
        outputs.Start();

        outputs.Dispose();
        player.EndPlayback(new InvalidOperationException("late device notification"));

        Assert.Empty(reasons);
        Assert.Equal(1, player.StopCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    private sealed class FakePlayer : IWavePlayer
    {
        public bool FailInit { get; init; }

        public bool FailPlay { get; init; }

        public bool FailStop { get; init; }

        public bool FailDispose { get; init; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IWaveProvider? Provider { get; private set; }

        public PlaybackState PlaybackState { get; private set; }

        public WaveFormat OutputWaveFormat => Provider?.WaveFormat ?? Format;

        public float Volume { get; set; }

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider)
        {
            if (FailInit)
            {
                throw new InvalidOperationException("init failed");
            }

            Provider = waveProvider;
        }

        public void Play()
        {
            if (FailPlay)
            {
                throw new InvalidOperationException("play failed");
            }

            PlaybackState = PlaybackState.Playing;
        }

        public void Pause() => PlaybackState = PlaybackState.Paused;

        public void Stop()
        {
            StopCalls++;
            if (FailStop)
            {
                throw new InvalidOperationException("stop failed");
            }

            EndPlayback(null);
        }

        public void Dispose()
        {
            DisposeCalls++;
            if (FailDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }

        public void EndPlayback(Exception? error)
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
        }
    }
}
