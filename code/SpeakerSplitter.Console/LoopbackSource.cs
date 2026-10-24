using NAudio.Wave;

namespace SpeakerSplitter;

// Captures what the default render endpoint is playing. The capture format is that endpoint's
// shared-mode mix format and is never changed; M3 fans audio out to other devices from this one place.
public sealed class LoopbackSource : IDisposable
{
    private readonly WasapiLoopbackCapture _capture = new();
    private readonly ManualResetEventSlim _stopped = new();
    private bool _started;

    public LoopbackSource()
    {
        _capture.DataAvailable += (_, e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
        _capture.RecordingStopped += (_, _) => _stopped.Set();
    }

    public WaveFormat WaveFormat => _capture.WaveFormat;

    // NAudio reuses the buffer between events; copy it if it must outlive the callback.
    public event Action<byte[], int>? Data;

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
}
