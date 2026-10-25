using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace SpeakerSplitter;

// Reports when the default multimedia render endpoint changes. Loopback capture stays bound to the
// endpoint it was opened on, so without this a default-output switch would silently keep capturing
// the old device instead of failing.
public sealed class DefaultOutputWatcher : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _client;

    public DefaultOutputWatcher()
    {
        _client = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_client);
    }

    // Raised on a COM notification thread.
    public event Action? Changed;

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_client);
        _enumerator.Dispose();
    }

    private sealed class NotificationClient(DefaultOutputWatcher owner) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                owner.Changed?.Invoke();
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }
}
