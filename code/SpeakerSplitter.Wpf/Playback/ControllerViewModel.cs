using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio;
using SpeakerSplitter.Devices;
using SpeakerSplitter.Wpf.Devices;

namespace SpeakerSplitter.Wpf.Playback;

/// <summary>Device selection and session lifecycle on the UI thread; worker notifications are enqueued there.</summary>
public sealed class ControllerViewModel : INotifyPropertyChanged, IDisposable
{
    #region Fields

    /// <summary>Enumerates current active endpoints; tests can supply a fixed device list.</summary>
    private readonly Func<IReadOnlyList<EndpointInfo>> _listEndpoints;

    /// <summary>Creates a fresh, owned session for each Start.</summary>
    private readonly Func<IPlaybackSession> _createSession;

    /// <summary>Enqueues an action on the UI thread without blocking the calling worker.</summary>
    private readonly Action<Action> _postToUi;

    /// <summary>The Start/Stop command and its live availability.</summary>
    private readonly ControllerCommand _toggleCommand;

    /// <summary>The manual endpoint-refresh command.</summary>
    private readonly ControllerCommand _refreshCommand;

    /// <summary>The active session, or <see langword="null"/> when stopped.</summary>
    private IPlaybackSession? _session;

    /// <summary>The active session's subscribed loss handler, removed before release.</summary>
    private Action<string>? _lossHandler;

    /// <summary>Whether endpoint enumeration succeeded on the last refresh.</summary>
    private bool _devicesAvailable;

    /// <summary>Whether the last endpoint list contains a default output.</summary>
    private bool _hasDefault;

    /// <summary>Prevents reentrant lifecycle changes during startup or cleanup.</summary>
    private bool _busy;

    /// <summary>Prevents new sessions and selection changes after the window releases this controller.</summary>
    private bool _disposed;

    /// <summary>The latest device-list status or failure.</summary>
    private string _status = "";

    #endregion

    #region Properties

    /// <summary>The active endpoint choices, including the disabled default endpoint.</summary>
    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    /// <summary>Starts or stops the owned session, depending on the current state.</summary>
    public ICommand ToggleCommand => _toggleCommand;

    /// <summary>Refreshes endpoint snapshots manually while stopped.</summary>
    public ICommand RefreshCommand => _refreshCommand;

    /// <summary>Whether a session is currently owned by the controller.</summary>
    public bool IsRunning => _session is not null;

    /// <summary>The current label for the Start/Stop button.</summary>
    public string ActionText => IsRunning ? "Stop" : "Start";

    /// <summary>A readable status; listing failures are shown rather than silently hiding devices.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    #endregion

    #region Events

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Constructor(s)

    /// <summary>Lists endpoints without opening audio; sessions are created only when Start is requested.</summary>
    /// <param name="listEndpoints">An endpoint enumerator, or <see langword="null"/> for Windows endpoints.</param>
    /// <param name="createSession">A session factory, or <see langword="null"/> for the shared Core engine.</param>
    /// <param name="postToUi">Enqueues work on the UI thread; <see langword="null"/> uses the creating thread's dispatcher.</param>
    public ControllerViewModel(
        Func<IReadOnlyList<EndpointInfo>>? listEndpoints = null,
        Func<IPlaybackSession>? createSession = null,
        Action<Action>? postToUi = null)
    {
        _listEndpoints = listEndpoints ?? EndpointCatalog.ListActive;
        _createSession = createSession ?? (() => new PlaybackSession());
        var dispatcher = Dispatcher.CurrentDispatcher;
        _postToUi = postToUi ?? (action => dispatcher.BeginInvoke(action));
        _toggleCommand = new ControllerCommand(Toggle, CanToggle);
        _refreshCommand = new ControllerCommand(RefreshDevices, () => !_disposed && !_busy && !IsRunning);
        RefreshDevices();
    }

    #endregion

    #region Methods

    /// <summary>Refreshes device choices and explicitly reports a missing default endpoint or enumeration failure.</summary>
    public void RefreshDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy || IsRunning)
        {
            throw new InvalidOperationException("Stop mirroring before refreshing output devices.");
        }

        try
        {
            var endpoints = _listEndpoints();
            var selected = Devices.Where(device => device.IsSelected)
                .Select(device => device.Endpoint.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var device in Devices)
            {
                device.PropertyChanged -= OnSelectionChanged;
            }

            Devices.Clear();
            foreach (var endpoint in endpoints)
            {
                var choice = new DeviceChoice(endpoint);
                if (!endpoint.IsDefault && selected.Contains(endpoint.Id))
                {
                    choice.IsSelected = true;
                }

                choice.PropertyChanged += OnSelectionChanged;
                Devices.Add(choice);
            }

            _devicesAvailable = true;
            _hasDefault = endpoints.Any(endpoint => endpoint.IsDefault);
            Status = _hasDefault
                ? "Select at least one additional output device."
                : "No default output is available. Choose one in Windows Sound settings, then refresh.";
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            _devicesAvailable = false;
            Status = $"Could not list output devices ({exception.GetType().Name}: {exception.Message}).";
        }
        finally
        {
            NotifyState();
        }
    }

    /// <summary>Stops the active session and reports cleanup errors visibly; safe when already stopped.</summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopSession("Stopped. Refresh devices if your outputs have changed.");
    }

    /// <summary>Stops and disposes the session on window close; repeated disposal is a no-op.</summary>
    /// <exception cref="AggregateException">Audio cleanup failed; the caller must surface the failure.</exception>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _busy = true;
        try
        {
            ReleaseSession();
            Status = "Stopped.";
        }
        catch (AggregateException exception)
        {
            Status = $"Audio cleanup failed: {exception.Message}";
            throw;
        }
        finally
        {
            foreach (var device in Devices)
            {
                device.PropertyChanged -= OnSelectionChanged;
            }

            _busy = false;
            NotifyState();
        }
    }

    /// <summary>Checks selection, endpoint availability and reentrancy before allowing Start/Stop.</summary>
    private bool CanToggle() => !_disposed && !_busy &&
        (IsRunning || (_devicesAvailable && _hasDefault && Devices.Any(device => device.IsSelected)));

    /// <summary>Starts a fresh session or stops the existing one on the UI thread.</summary>
    private void Toggle()
    {
        if (IsRunning)
        {
            Stop();
            return;
        }

        _busy = true;
        NotifyState();
        try
        {
            var targets = Devices.Where(device => device.IsSelected).Select(device => device.Endpoint).ToList();
            var session = _createSession();
            _session = session;
            _lossHandler = reason => _postToUi(() => OnLost(session, reason));
            session.Lost += _lossHandler;
            session.Start(targets);
            Status = $"Mirroring to {targets.Count} additional device(s).";
        }
        catch (Exception exception) when (exception is SplitterStartException or COMException or MmException or AggregateException
            or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            var detail = exception is SplitterStartException changed && changed.Problems.Count > 0
                ? $"{exception.Message} {string.Join(" ", changed.Problems)}"
                : exception.Message;
            Status = $"Could not start audio ({exception.GetType().Name}: {detail}). Refresh devices before restarting.";
            try
            {
                ReleaseSession();
            }
            catch (AggregateException cleanup)
            {
                Status += $" Cleanup also failed: {cleanup.Message}";
            }
        }
        finally
        {
            _busy = false;
            NotifyState();
        }
    }

    /// <summary>Ignores stale queued notifications and stops the matching lost session without recovery.</summary>
    /// <param name="session">The session that raised the worker notification.</param>
    /// <param name="reason">The engine's readable loss reason.</param>
    private void OnLost(IPlaybackSession session, string reason)
    {
        if (ReferenceEquals(_session, session))
        {
            StopSession($"Audio session lost: {reason} Refresh devices before restarting.");
        }
    }

    /// <summary>Releases the session, restores selection availability and preserves any cleanup failure.</summary>
    /// <param name="status">The message shown only if release succeeds.</param>
    private void StopSession(string status)
    {
        _busy = true;
        NotifyState();
        try
        {
            ReleaseSession();
            Status = status;
        }
        catch (AggregateException exception)
        {
            Status = $"Audio cleanup failed: {exception.Message}";
        }
        finally
        {
            _busy = false;
            NotifyState();
        }
    }

    /// <summary>Unsubscribes and attempts both Stop and Dispose, then reports all release failures together.</summary>
    /// <exception cref="AggregateException">At least one session-release operation failed.</exception>
    private void ReleaseSession()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        session.Lost -= _lossHandler;
        _lossHandler = null;
        List<Exception> errors = [];
        try
        {
            session.Stop();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        try
        {
            session.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Could not release the audio session.", errors);
        }
    }

    /// <summary>Updates command availability and locks checkboxes while the session owns the endpoints.</summary>
    private void NotifyState()
    {
        foreach (var device in Devices)
        {
            device.IsLocked = _disposed || _busy || IsRunning || !_devicesAvailable;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActionText)));
        _toggleCommand.NotifyAvailability();
        _refreshCommand.NotifyAvailability();
    }

    /// <summary>Updates Start availability after a device checkbox changes.</summary>
    /// <param name="sender">The device row that changed.</param>
    /// <param name="args">The property that changed.</param>
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DeviceChoice.IsSelected))
        {
            NotifyState();
        }
    }

    #endregion
}
