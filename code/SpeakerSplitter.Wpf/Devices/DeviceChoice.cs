using System.ComponentModel;
using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Wpf.Devices;

/// <summary>One endpoint's selectable UI state; all access belongs on the UI thread.</summary>
public sealed class DeviceChoice : INotifyPropertyChanged
{
    #region Fields

    /// <summary>Whether this non-default endpoint is selected for mirroring.</summary>
    private bool _isSelected;

    /// <summary>Whether session startup or playback prevents changing this selection.</summary>
    private bool _isLocked;

    /// <summary>The in-memory app-only gain percentage, initially 100; never persisted to disk.</summary>
    private double _appVolumePercent = 100;

    /// <summary>Whether startup, shutdown or unavailable endpoints prevent gain changes.</summary>
    private bool _isVolumeLocked;

    #endregion

    #region Properties

    /// <summary>The endpoint snapshot used to display and select this device.</summary>
    public EndpointInfo Endpoint { get; }

    /// <summary>The name, with the default endpoint explicitly identified.</summary>
    public string DisplayName => Endpoint.IsDefault
        ? $"{Endpoint.FriendlyName} (default; capture only)"
        : Endpoint.FriendlyName;

    /// <summary>False for the default endpoint or while selection is locked by the controller.</summary>
    public bool CanSelect => !Endpoint.IsDefault && !_isLocked;

    /// <summary>Allows gain adjustment for selected secondary streams, including while selection is locked.</summary>
    public bool CanAdjustVolume => IsSelected && !Endpoint.IsDefault && !_isVolumeLocked;

    /// <summary>The accessible label identifying this as stream gain, not Windows endpoint volume.</summary>
    public string VolumeLabel => $"App-only volume: {Endpoint.FriendlyName}";

    /// <summary>App-only gain in percent, retained while this window is open; changes never affect the primary.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The percentage is non-finite or outside 0–100.</exception>
    /// <exception cref="InvalidOperationException">The primary or a locked volume control is being changed.</exception>
    public double AppVolumePercent
    {
        get => _appVolumePercent;
        set
        {
            if (!double.IsFinite(value) || value < 0 || value > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "App-only volume must be between 0 and 100.");
            }

            if (_appVolumePercent == value)
            {
                return;
            }

            if (Endpoint.IsDefault || _isVolumeLocked)
            {
                throw new InvalidOperationException("App-only volume is disabled for this device.");
            }

            _appVolumePercent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppVolumePercent)));
        }
    }

    /// <summary>Locks gain during lifecycle transitions without locking live sliders during playback.</summary>
    internal bool IsVolumeLocked
    {
        get => _isVolumeLocked;
        set
        {
            if (_isVolumeLocked == value)
            {
                return;
            }

            _isVolumeLocked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdjustVolume)));
        }
    }

    /// <summary>Locks selection during startup/playback or when the endpoint list is unavailable.</summary>
    internal bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value)
            {
                return;
            }

            _isLocked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect)));
        }
    }

    /// <summary>Explains why the default device cannot be selected, including on a disabled checkbox.</summary>
    public string SelectionHint => Endpoint.IsDefault
        ? "The default output plays natively. Mirroring to the capture device would double or feed back the audio."
        : "Mirror system audio to this device.";

    /// <summary>Whether this endpoint is selected; changes to a disabled endpoint are rejected.</summary>
    /// <exception cref="InvalidOperationException">Selection is disabled for this endpoint.</exception>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            if (!CanSelect)
            {
                throw new InvalidOperationException("Selection is disabled for this output device.");
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanAdjustVolume)));
        }
    }

    #endregion

    #region Events

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    #region Constructor(s)

    /// <summary>Creates an initially unselected device row.</summary>
    /// <param name="endpoint">The active endpoint to show.</param>
    public DeviceChoice(EndpointInfo endpoint)
    {
        Endpoint = endpoint;
    }

    #endregion
}
