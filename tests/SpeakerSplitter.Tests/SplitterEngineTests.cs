using System.Runtime.InteropServices;
using NAudio.Wave;
using SpeakerSplitter.Capture;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Output;

namespace SpeakerSplitter.Tests;

public class SplitterEngineTests
{
    private static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private static readonly EndpointInfo Speakers = new("capture", "Speakers", true, null);
    private static readonly EndpointInfo Headphones = new("headphones", "Headphones", false, null);
    private static readonly EndpointInfo Monitor = new("monitor", "Monitor", false, null);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void Start_MirrorsCapturedAudioToEveryTarget_AndRaisesPeakAndStatsEvents()
    {
        using var rig = new Rig();
        using var sawPeak = new ManualResetEventSlim();
        using var sawStats = new ManualResetEventSlim();
        IReadOnlyList<DeviceBufferStats>? stats = null;
        rig.Engine.PeakUpdated += level =>
        {
            if (level > 0.1f)
            {
                sawPeak.Set();
            }
        };
        rig.Engine.StatsUpdated += snapshot =>
        {
            if (snapshot.All(device => device.Stats.BufferedBytes > 0))
            {
                stats = snapshot;
                sawStats.Set();
            }
        };
        rig.Engine.Start([Headphones, Monitor]);
        var packet = FloatBytes(0.1f, -0.5f, 0.2f, 0.3f);

        rig.Capture.Raise(packet);

        Assert.True(sawPeak.Wait(Patience));
        Assert.True(sawStats.Wait(Patience));
        Assert.Equal(1, rig.Engine.BuffersReceived);
        Assert.Equal(packet.Length, rig.Engine.BytesReceived);
        Assert.Equal(["Headphones", "Monitor"], stats!.Select(device => device.Name));
        Assert.All(stats!, device => Assert.Equal(packet.Length, device.Stats.BufferedBytes));
    }

    [Fact]
    public void Start_RejectsAChangedDefaultOutput_BeforeOpeningAnyOutput()
    {
        using var rig = new Rig();
        rig.Current = [Speakers with { IsDefault = false }, new EndpointInfo("other", "Other", true, null), Headphones];

        var error = Assert.Throws<SplitterStartException>(() => rig.Engine.Start([Headphones]));

        Assert.Equal(StartFailure.DefaultOutputChanged, error.Reason);
        Assert.Empty(rig.Outputs);
        Assert.False(rig.Capture.Started);
        Assert.True(rig.Watcher.Disposed);
    }

    [Fact]
    public void Start_ReportsEveryTargetThatChangedSinceItWasChosen()
    {
        using var rig = new Rig();
        rig.Current = [Speakers, Headphones];

        var error = Assert.Throws<SplitterStartException>(() => rig.Engine.Start([Monitor, Headphones, Headphones, Speakers]));

        Assert.Equal(StartFailure.TargetsChanged, error.Reason);
        Assert.Equal(3, error.Problems.Count);
        Assert.Contains("Monitor is no longer", error.Problems[0]);
        Assert.Contains("Headphones was selected more than once", error.Problems[1]);
        Assert.Contains("Speakers is now the default", error.Problems[2]);
        Assert.Empty(rig.Outputs);
        Assert.False(rig.Capture.Started);
    }

    [Fact]
    public void Start_UsesTheCurrentFormatOfEachTarget()
    {
        using var rig = new Rig();
        rig.Current = [Speakers, Headphones with { Format = new MixFormatInfo(44100, 2, 32, "IeeeFloat") }];

        rig.Engine.Start([Headphones]);

        Assert.Equal(["Headphones: converting 48000 Hz 2 ch -> 44100 Hz 2 ch"], rig.Engine.Conversions);
    }

    [Fact]
    public void Start_WhenAnOutputCannotBeOpened_ReleasesWhatItOpened()
    {
        using var rig = new Rig();
        rig.FailToOpen = Monitor;

        Assert.Throws<InvalidOperationException>(() => rig.Engine.Start([Headphones, Monitor]));

        Assert.Equal(1, Assert.Single(rig.Outputs).DisposeCalls);
        Assert.True(rig.Watcher.Disposed);
        Assert.False(rig.Capture.Started);
        rig.Engine.Stop();
    }

    [Fact]
    public void Start_CannotBeCalledTwice()
    {
        using var rig = new Rig();
        rig.Engine.Start([Headphones]);

        Assert.Throws<InvalidOperationException>(() => rig.Engine.Start([Headphones]));
    }

    [Fact]
    public void Lost_IsRaisedOnceWithTheFirstReason()
    {
        using var rig = new Rig();
        var reasons = new List<string>();
        rig.Engine.Lost += reasons.Add;
        rig.Engine.Start([Headphones]);

        rig.Watcher.Raise();
        rig.Capture.Fail(new InvalidOperationException("device gone"));
        rig.Outputs[0].EndPlayback();

        Assert.Equal(["The default output device changed."], reasons);
    }

    [Fact]
    public void Stop_ReleasesEverythingOnce_SurfacesCleanupFailure_AndSilencesEvents()
    {
        using var rig = new Rig();
        rig.FailDisposeOf = Headphones;
        var peaks = 0;
        rig.Engine.PeakUpdated += _ => Interlocked.Increment(ref peaks);
        rig.Engine.Start([Headphones, Monitor]);
        SpinWait.SpinUntil(() => Volatile.Read(ref peaks) > 0, Patience);

        Assert.Throws<AggregateException>(rig.Engine.Stop);
        rig.Engine.Stop();

        Assert.Equal(1, rig.Capture.StopCalls);
        Assert.True(rig.Watcher.Disposed);
        Assert.All(rig.Outputs, output => Assert.Equal(1, output.DisposeCalls));
        var after = Volatile.Read(ref peaks);
        Thread.Sleep(100);
        Assert.Equal(after, Volatile.Read(ref peaks));
    }

    [Fact]
    public void Dispose_StopsARunningEngine_AndReleasesCapture()
    {
        var rig = new Rig();
        rig.Engine.Start([Headphones]);

        rig.Engine.Dispose();

        Assert.Equal(1, rig.Capture.StopCalls);
        Assert.True(rig.Capture.Disposed);
        Assert.Equal(1, Assert.Single(rig.Outputs).DisposeCalls);
    }

    private static byte[] FloatBytes(params float[] samples) => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Engine = new SplitterEngine(
                new LoopbackSource(Capture),
                Speakers.Id,
                () => Current,
                CreateOutput,
                () => Watcher,
                peakInterval: TimeSpan.FromMilliseconds(10),
                statsInterval: TimeSpan.FromMilliseconds(10));
        }

        public FakeCapture Capture { get; } = new();

        public FakeWatcher Watcher { get; } = new();

        public List<FakeOutput> Outputs { get; } = [];

        public IReadOnlyList<EndpointInfo> Current { get; set; } = [Speakers, Headphones, Monitor];

        public EndpointInfo? FailToOpen { get; set; }

        public EndpointInfo? FailDisposeOf { get; set; }

        public SplitterEngine Engine { get; }

        public void Dispose() => Engine.Dispose();

        private IWavePlayer CreateOutput(EndpointInfo endpoint)
        {
            if (endpoint.Id == FailToOpen?.Id)
            {
                throw new InvalidOperationException("device unavailable");
            }

            var output = new FakeOutput { FailDispose = endpoint.Id == FailDisposeOf?.Id };
            Outputs.Add(output);
            return output;
        }
    }

    private sealed class FakeCapture : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = Format;

        public bool Started { get; private set; }

        public int StopCalls { get; private set; }

        public bool Disposed { get; private set; }

        public event EventHandler<WaveInEventArgs>? DataAvailable;

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording() => Started = true;

        public void StopRecording()
        {
            StopCalls++;
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Raise(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));

        public void Fail(Exception exception) => RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeOutput : IWavePlayer
    {
        public bool FailDispose { get; init; }

        public int DisposeCalls { get; private set; }

        public IWaveProvider? Provider { get; private set; }

        public PlaybackState PlaybackState { get; private set; }

        public WaveFormat OutputWaveFormat => Provider?.WaveFormat ?? Format;

        public float Volume { get; set; }

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => Provider = waveProvider;

        public void Play() => PlaybackState = PlaybackState.Playing;

        public void Pause() => PlaybackState = PlaybackState.Paused;

        public void Stop() => EndPlayback();

        public void EndPlayback()
        {
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose()
        {
            DisposeCalls++;
            if (FailDispose)
            {
                throw new InvalidOperationException("dispose failed");
            }
        }
    }

    private sealed class FakeWatcher : IDefaultOutputWatcher
    {
        public bool Disposed { get; private set; }

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();

        public void Dispose() => Disposed = true;
    }
}
