using NAudio.Wave;

namespace SpeakerSplitter;

/// <summary>
/// Captures what the default render endpoint is playing. The capture format is that endpoint's shared-mode
/// mix format and is never changed; the engine fans the audio out to other devices from this one place.
/// </summary>
public sealed class LoopbackSource : IDisposable
{
    #region Constants

    /// <summary>AUDCLNT_E_DEVICE_INVALIDATED: the endpoint was unplugged, disabled or reconfigured while in use.</summary>
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    #endregion

    #region Fields

    /// <summary>The underlying capture, usually a WASAPI loopback capture on the default render endpoint.</summary>
    private readonly IWaveIn _capture;

    /// <summary>Set once the capture has fully stopped, so <see cref="Stop"/> can wait for its last callback.</summary>
    private readonly ManualResetEventSlim _stopped = new();

    /// <summary>Whether <see cref="Start"/> has run and <see cref="Stop"/> has not.</summary>
    private bool _started;

    #endregion

    #region Properties

    /// <summary>The capture format, which is the endpoint's shared-mode mix format.</summary>
    public WaveFormat WaveFormat => _capture.WaveFormat;

    #endregion

    #region Events

    /// <summary>
    /// Raised on the capture thread with each buffer of captured audio and the number of valid bytes in it.
    /// NAudio reuses the buffer between events, so copy it if it must outlive the callback.
    /// </summary>
    public event Action<byte[], int>? Data;

    /// <summary>
    /// Raised once, on the capture thread, with a readable reason when capture ends on its own (device removed,
    /// disabled or reconfigured). It is not raised for a normal <see cref="Stop"/>.
    /// </summary>
    public event Action<string>? Lost;

    #endregion

    #region Constructor(s)

    /// <summary>Opens loopback capture on the default render endpoint.</summary>
    public LoopbackSource() : this(new WasapiLoopbackCapture())
    {
    }

    /// <summary>Wraps an existing capture, which lets tests substitute a fake.</summary>
    /// <param name="capture">The capture to read from; this source takes ownership of it.</param>
    public LoopbackSource(IWaveIn capture)
    {
        _capture = capture;
        _capture.DataAvailable += (_, e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
        _capture.RecordingStopped += OnRecordingStopped;
    }

    #endregion

    #region Methods

    /// <summary>Starts capturing; <see cref="Data"/> is raised from then on.</summary>
    public void Start()
    {
        _capture.StartRecording();
        _started = true;
    }

    /// <summary>
    /// Stops capturing and waits up to two seconds for the capture thread to finish. Does nothing if capture
    /// was never started or has already been stopped.
    /// </summary>
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

    /// <summary>Stops capture if it is running and releases the underlying capture.</summary>
    public void Dispose()
    {
        Stop();
        _capture.Dispose();
        _stopped.Dispose();
    }

    /// <summary>
    /// Handles the capture ending. An exception means capture was lost, so <see cref="Lost"/> is raised with the
    /// reason; either way <see cref="_stopped"/> is set so a waiting <see cref="Stop"/> can return.
    /// </summary>
    /// <param name="sender">The capture that stopped.</param>
    /// <param name="e">Carries the exception when capture ended on its own.</param>
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

    /// <summary>Turns a capture failure into a readable reason, recognising the device-removed case.</summary>
    /// <param name="exception">The failure that ended capture.</param>
    private static string Describe(Exception exception) =>
        exception.HResult == DeviceInvalidated
            ? "The capture device was unplugged, disabled or reconfigured (for example, its sample rate changed)."
            : $"Capture stopped unexpectedly ({exception.GetType().Name}: {exception.Message}).";

    #endregion
}
