using System.ComponentModel;
using System.Runtime.InteropServices;
using SpeakerSplitter.Devices;

namespace SpeakerSplitter.App.Devices;

/// <summary>Device-wide Windows volume/mute UI state, owned by one selected device on the UI thread.</summary>
public sealed class DeviceVolumeViewModel : INotifyPropertyChanged, IDisposable
{
    #region Fields

    /// <summary>The owned endpoint control, or <see langword="null"/> when unavailable or released.</summary>
    private IEndpointVolumeControl? _control;

    /// <summary>The last successfully read Windows state, absent when the control is unavailable.</summary>
    private EndpointVolumeState? _state;

    /// <summary>The visible device-control error, empty after a successful read.</summary>
    private string _status = "";

    /// <summary>Prevents using the model after its device is deselected or its window closes.</summary>
    private bool _disposed;

    #endregion

    #region Properties

    /// <summary>The friendly device name used to label its controls.</summary>
    public string Name { get; }

    /// <summary>Whether settings were read successfully; unavailable values are hidden behind a visible error.</summary>
    public bool HasControl => !_disposed && _control is not null && _state is not null;

    /// <summary>The last read Windows volume percentage; displayed only when <see cref="HasControl"/> is true.</summary>
    public double VolumePercent => _state is { } state ? state.Volume * 100d : 0d;

    /// <summary>The last read device mute state; displayed only when <see cref="HasControl"/> is true.</summary>
    public bool Muted => _state?.Muted ?? false;

    /// <summary>A visible failure message instead of a success-shaped fallback for unavailable device controls.</summary>
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

    /// <summary>Opens and reads device settings without issuing volume/mute writes.</summary>
    /// <param name="endpointId">The selected secondary ID.</param>
    /// <param name="name">The friendly device name.</param>
    /// <param name="openControl">Opens an owned native control; tests may supply a fake.</param>
    public DeviceVolumeViewModel(string endpointId, string name, Func<string, IEndpointVolumeControl> openControl)
    {
        Name = name;
        try
        {
            _control = openControl(endpointId);
            ApplyState(_control.GetState());
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException
            or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            Fail(exception);
        }
    }

    #endregion

    #region Methods

    /// <summary>Releases device controls without changing the user's Windows volume/mute settings.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseControl();
    }

    /// <summary>Updates backing state directly, without using volume/mute setters or writing back to Windows.</summary>
    /// <param name="state">The successfully read endpoint settings.</param>
    private void ApplyState(EndpointVolumeState state)
    {
        if (!float.IsFinite(state.Volume) || state.Volume < 0 || state.Volume > 1)
        {
            throw new InvalidOperationException("The device returned an invalid Windows volume.");
        }

        _state = state;
        Status = "";
        NotifyState();
    }

    /// <summary>Hides unavailable controls, releases any opened handle and reports read/cleanup errors together.</summary>
    /// <param name="exception">The original device-control failure.</param>
    private void Fail(Exception exception)
    {
        var message = $"Device controls unavailable for {Name}: {exception.Message}";
        try
        {
            ReleaseControl();
        }
        catch (Exception cleanup)
        {
            message += $" Cleanup also failed: {cleanup.Message}";
        }

        _state = null;
        Status = message;
        NotifyState();
    }

    /// <summary>Releases the one owned control once, even if disposal reports a failure.</summary>
    private void ReleaseControl()
    {
        var control = _control;
        _control = null;
        try
        {
            control?.Dispose();
        }
        finally
        {
            NotifyState();
        }
    }

    /// <summary>Updates bound availability and the last known settings on the UI thread.</summary>
    private void NotifyState()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasControl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VolumePercent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Muted)));
    }

    #endregion
}
