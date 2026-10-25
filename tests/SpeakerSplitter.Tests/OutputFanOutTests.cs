using NAudio.Wave;

namespace SpeakerSplitter.Tests;

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
            var provider = Assert.IsType<BufferedWaveProvider>(player.Provider);
            Assert.Same(Format, provider.WaveFormat);
            Assert.Equal(PlaybackState.Playing, player.PlaybackState);
            var actual = new byte[16];
            Assert.Equal(actual.Length, provider.Read(actual, 0, actual.Length));
            Assert.Equal(Enumerable.Range(1, 16).Select(i => (byte)i), actual);
        }
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
        var stalledBuffer = Assert.IsType<BufferedWaveProvider>(stalled.Provider);
        var activeBuffer = Assert.IsType<BufferedWaveProvider>(active.Provider);
        var packet = new byte[stalledBuffer.BufferLength];
        packet.AsSpan().Fill(42);
        outputs.AddSamples(packet, packet.Length);
        activeBuffer.Read(new byte[packet.Length], 0, packet.Length);

        outputs.AddSamples([1, 2, 3, 4, 5, 6, 7, 8], 8);

        Assert.Equal(stalledBuffer.BufferLength, stalledBuffer.BufferedBytes);
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
