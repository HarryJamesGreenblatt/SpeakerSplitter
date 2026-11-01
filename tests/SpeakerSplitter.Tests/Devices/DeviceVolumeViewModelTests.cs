using SpeakerSplitter.App.Devices;

namespace SpeakerSplitter.Tests.Devices;

public class DeviceVolumeViewModelTests
{
    [Fact]
    public void Constructor_ReadsCurrentDeviceSettingsAndReleasesTheHandleOnce()
    {
        var control = new FakeEndpointVolumeControl();
        var model = new DeviceVolumeViewModel("secondary", "Secondary", _ => control);

        Assert.True(model.HasControl);
        Assert.Equal(63, model.VolumePercent, 4);
        Assert.True(model.Muted);
        Assert.Equal(1, control.ReadCalls);
        model.Dispose();
        model.Dispose();
        Assert.False(model.HasControl);
        Assert.Equal(1, control.DisposeCalls);
    }

    [Fact]
    public void Constructor_WhenReadFails_HidesSettingsAndReportsReadAndCleanupFailures()
    {
        var control = new FakeEndpointVolumeControl { FailRead = true, FailDispose = true };
        using var model = new DeviceVolumeViewModel("secondary", "Secondary", _ => control);

        Assert.False(model.HasControl);
        Assert.Contains("device read failed", model.Status);
        Assert.Contains("device dispose failed", model.Status);
        Assert.Equal(1, control.DisposeCalls);
    }
}
