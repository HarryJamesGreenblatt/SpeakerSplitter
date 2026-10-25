using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeakerSplitter;

// The audio engine behind every front end. It captures the default render endpoint with loopback and
// mirrors it to the chosen endpoints. It never touches the console or any UI: a host drives it with
// Start and Stop and listens to its events. Events are raised on worker threads (timer, capture and COM
// notification threads), so a UI host must marshal them, and a handler must not block on the thread
// that calls Stop. Start and Stop are meant to be called from one thread.
public sealed class SplitterEngine : IDisposable
{
    private static readonly TimeSpan DefaultPeakInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan DefaultStatsInterval = TimeSpan.FromSeconds(1);

    private readonly LoopbackSource _source;
    private readonly Func<IReadOnlyList<EndpointInfo>> _listEndpoints;
    private readonly Func<EndpointInfo, IWavePlayer>? _createOutput;
    private readonly Func<IDefaultOutputWatcher> _createWatcher;
    private readonly IDisposable[] _owned;
    private readonly LevelMeter _meter;
    private readonly TimeSpan _peakInterval;
    private readonly TimeSpan _statsInterval;

    private IDefaultOutputWatcher? _watcher;
    private OutputFanOut? _outputs;
    private Timer? _peakTimer;
    private Timer? _statsTimer;
    private bool _started;
    private int _released;
    private int _disposed;
    private string? _lossReason;
    private long _buffers;
    private long _bytes;

    internal SplitterEngine(
        LoopbackSource source,
        string captureEndpointId,
        Func<IReadOnlyList<EndpointInfo>> listEndpoints,
        Func<EndpointInfo, IWavePlayer>? createOutput = null,
        Func<IDefaultOutputWatcher>? createWatcher = null,
        IDisposable[]? owned = null,
        TimeProvider? time = null,
        TimeSpan? peakInterval = null,
        TimeSpan? statsInterval = null)
    {
        _source = source;
        CaptureEndpointId = captureEndpointId;
        CaptureFormat = MixFormatInfo.From(source.WaveFormat);
        _listEndpoints = listEndpoints;
        _createOutput = createOutput;
        _createWatcher = createWatcher ?? (() => new DefaultOutputWatcher());
        _owned = owned ?? [];
        _meter = new LevelMeter(time);
        _peakInterval = peakInterval ?? DefaultPeakInterval;
        _statsInterval = statsInterval ?? DefaultStatsInterval;
    }

    // Raised once, with the first reason, when capture or an output is lost or the default output changes.
    // The engine does not recover: the host should stop it.
    public event Action<string>? Lost;

    // The decayed capture peak on a 0..1 scale (1.0 is digital full scale), about every 50 ms once started.
    public event Action<float>? PeakUpdated;

    // Per-device buffer statistics, about once a second once started.
    public event Action<IReadOnlyList<DeviceBufferStats>>? StatsUpdated;

    public MixFormatInfo CaptureFormat { get; }

    public string CaptureEndpointId { get; }

    public long BuffersReceived => Interlocked.Read(ref _buffers);

    public long BytesReceived => Interlocked.Read(ref _bytes);

    // One line per output that is being converted; outputs that already match the capture are not listed.
    public IReadOnlyList<string> Conversions => _outputs?.Conversions ?? [];

    // Opens loopback capture on the current default render endpoint.
    public static SplitterEngine ForDefaultOutput()
    {
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var source = new LoopbackSource(new WasapiLoopbackCapture(device));
            return new SplitterEngine(source, device.ID, EndpointCatalog.ListActive, owned: [device, enumerator]);
        }
        catch
        {
            device?.Dispose();
            enumerator?.Dispose();
            throw;
        }
    }

    // Starts capture and mirrors it to the targets (none is allowed: capture and peak only). The targets are
    // re-checked against the current endpoints first, because the default output or a device may have changed
    // since the host listed them. Throws SplitterStartException if they no longer hold; nothing is left open
    // when Start throws.
    public void Start(IEnumerable<EndpointInfo> targets)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_started)
        {
            throw new InvalidOperationException("The engine has already been started.");
        }

        if (!PeakMeter.IsSupported(CaptureFormat))
        {
            throw new NotSupportedException(
                $"The peak meter supports 32-bit float and 16-bit PCM, not {CaptureFormat.Describe()}.");
        }

        var requested = targets.ToList();
        _started = true;
        try
        {
            _source.Lost += Lose;
            _watcher = _createWatcher();
            _watcher.Changed += () => Lose("The default output device changed.");

            // The watcher above covers every later change, so checking now closes the gap before capture starts.
            var chosen = Resolve(_listEndpoints(), requested);

            var outputs = new OutputFanOut(_source.WaveFormat, CaptureEndpointId, chosen, _createOutput);
            _outputs = outputs;
            outputs.Lost += Lose;
            outputs.Start();
            _source.Data += OnData;
            _source.Start();

            // Timers, not the capture callback, so the peak can decay while no data arrives.
            _peakTimer = new Timer(_ => PeakUpdated?.Invoke(_meter.Level), null, TimeSpan.Zero, _peakInterval);
            _statsTimer = new Timer(_ => StatsUpdated?.Invoke(outputs.Snapshot()), null, _statsInterval, _statsInterval);
        }
        catch (Exception failure)
        {
            try
            {
                Release();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(
                    "The engine failed to start and could not release everything.", failure, cleanup);
            }

            throw;
        }
    }

    // Stops capture and releases every output. Safe to call more than once. Throws AggregateException if
    // some output resource could not be released; everything else is still released.
    public void Stop()
    {
        if (_started)
        {
            Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Stop();
        }
        finally
        {
            _source.Dispose();
            foreach (var resource in _owned)
            {
                resource.Dispose();
            }
        }
    }

    private IReadOnlyList<EndpointInfo> Resolve(IReadOnlyList<EndpointInfo> current, List<EndpointInfo> requested)
    {
        var currentDefault = current.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
        if (!string.Equals(currentDefault, CaptureEndpointId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SplitterStartException(
                StartFailure.DefaultOutputChanged, "The default output device changed before capture started.", []);
        }

        var chosen = new List<EndpointInfo>();
        var problems = new List<string>();
        foreach (var wanted in requested)
        {
            var match = current.FirstOrDefault(
                endpoint => string.Equals(endpoint.Id, wanted.Id, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                problems.Add($"{wanted.FriendlyName} is no longer an active output device.");
            }
            else if (match.IsDefault)
            {
                problems.Add(
                    $"{match.FriendlyName} is now the default output device, which SpeakerSplitter captures. " +
                    "Mirroring to it would double or feed back the audio.");
            }
            else if (chosen.Contains(match))
            {
                problems.Add($"{match.FriendlyName} was selected more than once.");
            }
            else
            {
                // The current entry, not the caller's: its mix format reflects the device as it is now.
                chosen.Add(match);
            }
        }

        if (problems.Count > 0)
        {
            throw new SplitterStartException(
                StartFailure.TargetsChanged, "The output devices changed before capture started.", problems);
        }

        return chosen;
    }

    private void OnData(byte[] buffer, int count)
    {
        Interlocked.Increment(ref _buffers);
        Interlocked.Add(ref _bytes, count);

        _meter.Push(PeakMeter.Compute(buffer.AsSpan(0, count), CaptureFormat));
        _outputs?.AddSamples(buffer, count);
    }

    private void Lose(string reason)
    {
        // The first reason wins: unplugging the default device raises both a capture error and a default-device change.
        if (Interlocked.CompareExchange(ref _lossReason, reason, null) is null)
        {
            Lost?.Invoke(reason);
        }
    }

    // Anything that calls back is silenced before the objects it touches go away, and the timers are
    // awaited so nothing is raised after Stop returns. Each step runs even if an earlier one throws.
    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            _source.Lost -= Lose;
            _watcher?.Dispose();
            StopTimer(_statsTimer);
            StopTimer(_peakTimer);
        }
        finally
        {
            try
            {
                _source.Stop();
            }
            finally
            {
                if (_outputs is not null)
                {
                    _outputs.Lost -= Lose;
                    _outputs.Dispose();
                }
            }
        }
    }

    private static void StopTimer(Timer? timer)
    {
        if (timer is null)
        {
            return;
        }

        using var finished = new ManualResetEvent(false);
        if (timer.Dispose(finished))
        {
            finished.WaitOne(TimeSpan.FromSeconds(2));
        }
    }
}
