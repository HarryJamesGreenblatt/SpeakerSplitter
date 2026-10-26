using NAudio.CoreAudioApi;
using NAudio.Wave;
using SpeakerSplitter.Capture;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Output;

namespace SpeakerSplitter;

/// <summary>
/// The audio engine behind every front end. It captures the default render endpoint with loopback and
/// mirrors it to the chosen endpoints. It never touches the console or any UI: a host drives it with
/// <see cref="Start"/> and <see cref="Stop"/> and listens to its events.
/// <para>
/// Events are raised on worker threads (timer, capture and COM notification threads), so a UI host must
/// marshal them, and a handler must not block on the thread that calls <see cref="Stop"/>. <see cref="Start"/>
/// and <see cref="Stop"/> are meant to be called from one thread.
/// </para>
/// </summary>
public sealed class SplitterEngine : IDisposable
{
    #region Fields

    /// <summary>How often <see cref="PeakUpdated"/> is raised unless a test says otherwise.</summary>
    private static readonly TimeSpan DefaultPeakInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>How often <see cref="StatsUpdated"/> is raised unless a test says otherwise.</summary>
    private static readonly TimeSpan DefaultStatsInterval = TimeSpan.FromSeconds(1);

    /// <summary>The loopback capture of the default endpoint, which every output is fed from.</summary>
    private readonly LoopbackSource _source;

    /// <summary>Lists the endpoints that are active now; used to re-check the targets at start.</summary>
    private readonly Func<IReadOnlyList<EndpointInfo>> _listEndpoints;

    /// <summary>Creates a player for an endpoint, or <see langword="null"/> for real shared-mode WASAPI; tests substitute fakes.</summary>
    private readonly Func<EndpointInfo, IWavePlayer>? _createOutput;

    /// <summary>Creates the watcher that reports a change of the default output.</summary>
    private readonly Func<IDefaultOutputWatcher> _createWatcher;

    /// <summary>Resources the engine owns beyond the capture, such as the device and enumerator it was opened on.</summary>
    private readonly IDisposable[] _owned;

    /// <summary>Holds and decays the capture peak that <see cref="PeakUpdated"/> reports.</summary>
    private readonly LevelMeter _meter;

    /// <summary>How often <see cref="PeakUpdated"/> is raised.</summary>
    private readonly TimeSpan _peakInterval;

    /// <summary>How often <see cref="StatsUpdated"/> is raised.</summary>
    private readonly TimeSpan _statsInterval;

    /// <summary>Reports a change of the default output while running, or <see langword="null"/> before <see cref="Start"/>.</summary>
    private IDefaultOutputWatcher? _watcher;

    /// <summary>The per-device outputs, or <see langword="null"/> before <see cref="Start"/>.</summary>
    private OutputFanOut? _outputs;

    /// <summary>Raises <see cref="PeakUpdated"/>, or <see langword="null"/> when not running.</summary>
    private Timer? _peakTimer;

    /// <summary>Raises <see cref="StatsUpdated"/>, or <see langword="null"/> when not running.</summary>
    private Timer? _statsTimer;

    /// <summary>Whether <see cref="Start"/> has been called; an engine starts at most once.</summary>
    private bool _started;

    /// <summary>Non-zero once everything has been released, so a second release is a no-op.</summary>
    private int _released;

    /// <summary>Non-zero once <see cref="Dispose"/> has begun.</summary>
    private int _disposed;

    /// <summary>The first reason capture or an output was lost, or <see langword="null"/> while nothing has been.</summary>
    private string? _lossReason;

    /// <summary>Buffers received from capture so far; updated with <see cref="Interlocked"/>.</summary>
    private long _buffers;

    /// <summary>Bytes received from capture so far; updated with <see cref="Interlocked"/>.</summary>
    private long _bytes;

    #endregion

    #region Properties

    /// <summary>The capture format, which is the default endpoint's shared-mode mix format.</summary>
    public MixFormatInfo CaptureFormat { get; }

    /// <summary>The ID of the endpoint being captured, which is never used as an output.</summary>
    public string CaptureEndpointId { get; }

    /// <summary>How many buffers capture has delivered so far.</summary>
    public long BuffersReceived => Interlocked.Read(ref _buffers);

    /// <summary>How many bytes capture has delivered so far.</summary>
    public long BytesReceived => Interlocked.Read(ref _bytes);

    /// <summary>One line per output that is being converted; outputs that already match the capture are not listed.</summary>
    public IReadOnlyList<string> Conversions => _outputs?.Conversions ?? [];

    #endregion

    #region Events

    /// <summary>
    /// Raised once, with the first reason, when capture or an output is lost or the default output changes.
    /// The engine does not recover: the host should stop it.
    /// </summary>
    public event Action<string>? Lost;

    /// <summary>The decayed capture peak on a 0..1 scale (1.0 is digital full scale), about every 50 ms once started.</summary>
    public event Action<float>? PeakUpdated;

    /// <summary>Per-device buffer statistics, about once a second once started.</summary>
    public event Action<IReadOnlyList<DeviceBufferStats>>? StatsUpdated;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Builds an engine around an existing capture. Internal so tests can substitute fakes; hosts use
    /// <see cref="ForDefaultOutput"/>.
    /// </summary>
    /// <param name="source">The loopback capture of the default endpoint.</param>
    /// <param name="captureEndpointId">The ID of the endpoint being captured.</param>
    /// <param name="listEndpoints">Lists the endpoints that are active now.</param>
    /// <param name="createOutput">Creates a player for an endpoint; <see langword="null"/> means real shared-mode WASAPI.</param>
    /// <param name="createWatcher">Creates the default-output watcher; <see langword="null"/> means the real one.</param>
    /// <param name="owned">Extra resources to dispose with the engine.</param>
    /// <param name="time">The clock the peak decays against; <see langword="null"/> means the system clock.</param>
    /// <param name="peakInterval">How often to raise <see cref="PeakUpdated"/>.</param>
    /// <param name="statsInterval">How often to raise <see cref="StatsUpdated"/>.</param>
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

    #endregion

    #region Methods

    /// <summary>Opens loopback capture on the current default render endpoint.</summary>
    public static SplitterEngine ForDefaultOutput()
    {
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        try
        {
            // Resolve the default endpoint explicitly, then hand the device and enumerator to the engine to own.
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

    /// <summary>
    /// Starts capture and mirrors it to the targets (none is allowed: capture and peak only). The targets are
    /// re-checked against the current endpoints first, because the default output or a device may have changed
    /// since the host listed them. Nothing is left open when this throws.
    /// </summary>
    /// <param name="targets">The endpoints to mirror to.</param>
    /// <exception cref="SplitterStartException">The default output or the targets no longer hold.</exception>
    /// <exception cref="NotSupportedException">The capture format cannot be metered.</exception>
    /// <exception cref="InvalidOperationException">The engine has already been started.</exception>
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
            // Listen for losses and for a change of the default output before anything is opened.
            _source.Lost += Lose;
            _watcher = _createWatcher();
            _watcher.Changed += () => Lose("The default output device changed.");

            // The watcher above covers every later change, so checking now closes the gap before capture starts.
            var chosen = Resolve(_listEndpoints(), requested);

            // Open and start the outputs first, so they are ready to play the moment capture delivers audio.
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
            // Release whatever was opened; wrap both failures only if the release also fails.
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

    /// <summary>
    /// Stops capture and releases every output. Safe to call more than once and before <see cref="Start"/>.
    /// Everything is released even when something throws.
    /// </summary>
    /// <exception cref="AggregateException">Some output resource could not be released.</exception>
    public void Stop()
    {
        if (_started)
        {
            Release();
        }
    }

    /// <summary>Stops a running engine, then releases the capture and any resources the engine owns.</summary>
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

    /// <summary>
    /// Matches the requested targets against the endpoints that are active now. The default output must still
    /// be the one being captured, and each target must still be active, not the default, and chosen once.
    /// </summary>
    /// <param name="current">The endpoints that are active now.</param>
    /// <param name="requested">The targets the host chose earlier.</param>
    /// <returns>The current entry for each target, in the order requested.</returns>
    /// <exception cref="SplitterStartException">The default output changed or a target is no longer valid.</exception>
    private IReadOnlyList<EndpointInfo> Resolve(IReadOnlyList<EndpointInfo> current, List<EndpointInfo> requested)
    {
        // Capture is bound to the endpoint it was opened on, so a different default means it is now the wrong one.
        var currentDefault = current.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id;
        if (!string.Equals(currentDefault, CaptureEndpointId, StringComparison.OrdinalIgnoreCase))
        {
            throw new SplitterStartException(
                StartFailure.DefaultOutputChanged, "The default output device changed before capture started.", []);
        }

        // Check every target and collect all the problems, so the host can report them together.
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

    /// <summary>Handles one captured buffer on the capture thread: counts it, feeds the peak meter and copies it to every output.</summary>
    /// <param name="buffer">The captured audio; reused by NAudio, so it is consumed synchronously.</param>
    /// <param name="count">The number of valid bytes in <paramref name="buffer"/>.</param>
    private void OnData(byte[] buffer, int count)
    {
        Interlocked.Increment(ref _buffers);
        Interlocked.Add(ref _bytes, count);

        _meter.Push(PeakMeter.Compute(buffer.AsSpan(0, count), CaptureFormat));
        _outputs?.AddSamples(buffer, count);
    }

    /// <summary>Records a loss and raises <see cref="Lost"/>, but only for the first one.</summary>
    /// <param name="reason">A readable reason.</param>
    private void Lose(string reason)
    {
        // The first reason wins: unplugging the default device raises both a capture error and a default-device change.
        if (Interlocked.CompareExchange(ref _lossReason, reason, null) is null)
        {
            Lost?.Invoke(reason);
        }
    }

    /// <summary>
    /// Stops everything the engine started. Anything that calls back is silenced before the objects it touches
    /// go away, and the timers are awaited so nothing is raised after <see cref="Stop"/> returns. Each step runs
    /// even if an earlier one throws.
    /// </summary>
    /// <exception cref="AggregateException">Some output resource could not be released.</exception>
    private void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        try
        {
            // Silence the callbacks first, so nothing fires while the rest is torn down.
            _source.Lost -= Lose;
            _watcher?.Dispose();
            StopTimer(_statsTimer);
            StopTimer(_peakTimer);
        }
        finally
        {
            try
            {
                // Stop capture before the outputs go, so no audio is written to a released buffer.
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

    /// <summary>Disposes a timer and waits up to two seconds for any callback in flight to finish.</summary>
    /// <param name="timer">The timer to stop, or <see langword="null"/> if it was never created.</param>
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

    #endregion
}
