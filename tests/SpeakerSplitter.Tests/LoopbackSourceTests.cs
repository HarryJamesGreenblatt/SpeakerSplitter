using System.Runtime.InteropServices;
using NAudio.Wave;

namespace SpeakerSplitter.Tests;

public class LoopbackSourceTests
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);

    [Fact]
    public void Lost_ReportsDeviceRemoval_WhenCaptureStopsWithDeviceInvalidated()
    {
        var capture = new FakeCapture();
        var source = new LoopbackSource(capture);
        var reasons = new List<string>();
        source.Lost += reasons.Add;

        source.Start();
        capture.Fail(new COMException("invalidated", DeviceInvalidated));

        var reason = Assert.Single(reasons);
        Assert.Contains("unplugged", reason);
    }

    [Fact]
    public void Lost_IncludesExceptionTypeAndMessage_ForOtherFailures()
    {
        var capture = new FakeCapture();
        var source = new LoopbackSource(capture);
        var reasons = new List<string>();
        source.Lost += reasons.Add;

        source.Start();
        capture.Fail(new InvalidOperationException("boom"));

        var reason = Assert.Single(reasons);
        Assert.Contains("InvalidOperationException", reason);
        Assert.Contains("boom", reason);
    }

    [Fact]
    public void Lost_IsNotRaised_OnNormalStop()
    {
        var capture = new FakeCapture();
        var source = new LoopbackSource(capture);
        var reasons = new List<string>();
        source.Lost += reasons.Add;

        source.Start();
        source.Stop();

        Assert.Empty(reasons);
    }

    [Fact]
    public void Dispose_StopsAndDisposesCapture_EvenAfterLoss()
    {
        var capture = new FakeCapture();
        var source = new LoopbackSource(capture);
        source.Start();
        capture.Fail(new InvalidOperationException("device gone"));

        source.Dispose();

        Assert.Equal(1, capture.StopCalls);
        Assert.True(capture.Disposed);
    }

    private sealed class FakeCapture : IWaveIn
    {
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int StopCalls { get; private set; }

        public bool Disposed { get; private set; }

        // Never raised: these tests only exercise how capture ends.
        public event EventHandler<WaveInEventArgs>? DataAvailable
        {
            add { }
            remove { }
        }

        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void StartRecording()
        {
        }

        public void StopRecording()
        {
            StopCalls++;
            RecordingStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Fail(Exception exception) => RecordingStopped?.Invoke(this, new StoppedEventArgs(exception));

        public void Dispose() => Disposed = true;
    }
}
