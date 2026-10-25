using NAudio.Wave;

namespace SpeakerSplitter;

// Captures what the default render endpoint is playing. The capture format is that endpoint's
// shared-mode mix format and is never changed; M3 fans audio out to other devices from this one place.
public sealed class LoopbackSource : IDisposable
{
    // AUDCLNT_E_DEVICE_INVALIDATED: the endpoint was unplugged, disabled or reconfigured while in use.
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    private readonly IWaveIn _capture;
    private readonly ManualResetEventSlim _stopped = new();
    private bool _started;

    public LoopbackSource() : this(new WasapiLoopbackCapture())
    {
    }

    public LoopbackSource(IWaveIn capture)
    {
        _capture = capture;
        _capture.DataAvailable += (_, e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    // NAudio reuses the buffer between events; copy it if it must outlive the callback.
    public event Action<byte[], int>? Data;

    // Raised once with a readable reason when capture ends on its own (device removed, disabled or
    // reconfigured). It is not raised for a normal Stop(). Runs on the capture thread.
    public event Action<string>? Lost;

    public void Start()
    {
        _capture.StartRecording();
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _capture.StopRecording();
        _stopped.Wait(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _capture.Dispose();
        _stopped.Dispose();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            if (e.Exception is not null)
            {
                Lost?.Invoke(Describe(e.Exception));
            }
        }
        finally
        {
            _stopped.Set();
        }
    }

    private static string Describe(Exception exception) =>
        exception.HResult == DeviceInvalidated
            ? "The capture device was unplugged, disabled or reconfigured (for example, its sample rate changed)."
            : $"Capture stopped unexpectedly ({exception.GetType().Name}: {exception.Message}).";
}
