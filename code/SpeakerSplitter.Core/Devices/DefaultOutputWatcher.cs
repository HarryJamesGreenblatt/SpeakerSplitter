using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SpeakerSplitter.Devices;

/// <summary>Reports when the default render endpoint changes; abstracted so tests can fake it.</summary>
public interface IDefaultOutputWatcher : IDisposable
{
    /// <summary>Raised on a COM notification thread when the default multimedia render endpoint changes.</summary>
    event Action? Changed;
}

/// <summary>
/// Reports when the default multimedia render endpoint changes. Loopback capture stays bound to the endpoint it
/// was opened on, so without this a default-output switch would silently keep capturing the old device instead
/// of failing.
/// </summary>
public sealed class DefaultOutputWatcher : IDefaultOutputWatcher
{
    #region Fields

    /// <summary>The enumerator the notification callback is registered on.</summary>
    private readonly MMDeviceEnumerator _enumerator = new();

    /// <summary>The callback that receives Windows' device notifications and forwards the relevant one.</summary>
    private readonly NotificationClient _client;

    #endregion

    #region Events

    /// <inheritdoc />
    public event Action? Changed;

    #endregion

    #region Constructor(s)

    /// <summary>Starts listening for default-output changes.</summary>
    public DefaultOutputWatcher()
    {
        _client = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    #endregion

    #region Methods

    /// <summary>Stops listening and releases the enumerator.</summary>
    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_client);
        _enumerator.Dispose();
    }

    #endregion

    #region Nested Types

    /// <summary>Receives Windows' device notifications and raises <see cref="Changed"/> for the one that matters.</summary>
    /// <param name="owner">The watcher to raise <see cref="Changed"/> on.</param>
    private sealed class NotificationClient(DefaultOutputWatcher owner) : IMMNotificationClient
    {
        /// <summary>Raises <see cref="Changed"/> when the default multimedia render endpoint changes; other roles and flows are ignored.</summary>
        /// <param name="flow">Whether the change is for a render or a capture endpoint.</param>
        /// <param name="role">Which role's default changed.</param>
        /// <param name="defaultDeviceId">The new default device's ID.</param>
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                owner.Changed?.Invoke();
            }
        }

        /// <summary>Not used: only default-device changes matter here.</summary>
        /// <param name="deviceId">The device whose state changed.</param>
        /// <param name="newState">Its new state.</param>
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        /// <summary>Not used: only default-device changes matter here.</summary>
        /// <param name="pwstrDeviceId">The device that was added.</param>
        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        /// <summary>Not used: only default-device changes matter here.</summary>
        /// <param name="deviceId">The device that was removed.</param>
        public void OnDeviceRemoved(string deviceId)
        {
        }

        /// <summary>Not used: only default-device changes matter here.</summary>
        /// <param name="pwstrDeviceId">The device whose property changed.</param>
        /// <param name="key">The property that changed.</param>
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }

    #endregion
}
