using NAudio.CoreAudioApi;

namespace SpeakerSplitter.Devices;

/// <summary>Owns Windows endpoint controls for one active secondary render device; never opens a playback stream.</summary>
public sealed class EndpointVolumeControl : IEndpointVolumeControl
{
    #region Fields

    /// <summary>The stable endpoint ID, used to protect the current default output.</summary>
    private readonly string _endpointId;

    /// <summary>Resolves current endpoint identity and the multimedia default.</summary>
    private readonly MMDeviceEnumerator _enumerator;

    /// <summary>The opened endpoint, or <see langword="null"/> if construction failed before it opened.</summary>
    private readonly MMDevice? _device;

    /// <summary>Identifies writes from this control instance in Windows notifications.</summary>
    private readonly Guid _notificationGuid = Guid.NewGuid();

    /// <summary>Non-zero once native control release has begun.</summary>
    private int _disposed;

    #endregion

    #region Constructor(s)

    /// <summary>Opens and reads an active secondary's volume interface without changing its settings.</summary>
    /// <param name="endpointId">The secondary render endpoint's stable ID.</param>
    /// <exception cref="InvalidOperationException">The endpoint is unavailable, is the default, or cannot be read.</exception>
    public EndpointVolumeControl(string endpointId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointId);
        _endpointId = endpointId;
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _device = _enumerator.GetDevice(endpointId);
            EnsureSecondary();
            GetState();
        }
        catch (Exception initialization)
        {
            try
            {
                Dispose();
            }
            catch (AggregateException cleanup)
            {
                throw new AggregateException("Endpoint control initialization and release failed.", initialization, cleanup);
            }

            throw new InvalidOperationException($"Could not open endpoint controls for '{endpointId}': {initialization.Message}", initialization);
        }
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public EndpointVolumeState GetState()
    {
        var control = GetControl();
        var volume = control.MasterVolumeLevelScalar;
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new InvalidOperationException("Windows returned an invalid endpoint volume.");
        }

        var support = control.HardwareSupport;
        return new EndpointVolumeState(volume, control.Mute,
            (support & EEndpointHardwareSupport.Volume) != 0,
            (support & EEndpointHardwareSupport.Mute) != 0);
    }

    /// <inheritdoc />
    public EndpointVolumeState SetVolume(float volume)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!float.IsFinite(volume) || volume < 0 || volume > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), volume, "Windows volume must be between 0 and 1.");
        }

        EnsureSecondary();
        var control = GetControl();
        control.NotificationGuid = _notificationGuid;
        control.MasterVolumeLevelScalar = volume;
        return GetState();
    }

    /// <inheritdoc />
    public EndpointVolumeState SetMute(bool muted)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        EnsureSecondary();
        var control = GetControl();
        control.NotificationGuid = _notificationGuid;
        control.Mute = muted;
        return GetState();
    }

    /// <summary>Releases the NAudio-owned volume callback and enumerator without restoring endpoint settings.</summary>
    /// <exception cref="AggregateException">At least one native resource could not be released.</exception>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        List<Exception> errors = [];
        try
        {
            // MMDevice owns AudioEndpointVolume; disposing both would release its COM interface twice.
            _device?.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        try
        {
            _enumerator.Dispose();
        }
        catch (Exception exception)
        {
            errors.Add(exception);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Could not release endpoint controls.", errors);
        }
    }

    /// <summary>Rejects the current default output, even when the UI's endpoint snapshot is stale.</summary>
    /// <exception cref="InvalidOperationException">This endpoint is now the default output.</exception>
    private void EnsureSecondary()
    {
        using var primary = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        if (string.Equals(primary.ID, _endpointId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The current default output cannot be controlled as a secondary device.");
        }
    }

    /// <summary>Resolves the existing volume interface and rejects disposed or inactive endpoints.</summary>
    private AudioEndpointVolume GetControl()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var device = _device ?? throw new InvalidOperationException("The endpoint is not open.");
        if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
        {
            throw new InvalidOperationException("The selected render endpoint is no longer active.");
        }

        return device.AudioEndpointVolume;
    }

    #endregion
}
