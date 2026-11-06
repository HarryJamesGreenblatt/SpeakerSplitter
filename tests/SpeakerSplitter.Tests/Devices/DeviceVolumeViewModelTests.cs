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
        Assert.Equal(0, control.VolumeWrites);
        Assert.Equal(0, control.MuteWrites);
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

    [Fact]
    public void VolumeAndMute_UseActualNativeReadbackAndLeaveOtherControlsUnchanged()
    {
        var control = new FakeEndpointVolumeControl { VolumeStep = 0.05f };
        var other = new FakeEndpointVolumeControl();
        using var model = new DeviceVolumeViewModel("secondary", "Secondary", _ => control);
        using var otherModel = new DeviceVolumeViewModel("other", "Other", _ => other);

        model.VolumePercent = 41;
        model.Muted = false;

        Assert.Equal(40, model.VolumePercent, 4);
        Assert.False(model.Muted);
        Assert.Equal(1, control.VolumeWrites);
        Assert.Equal(1, control.MuteWrites);
        Assert.Equal(0, other.VolumeWrites);
        Assert.Equal(0, other.MuteWrites);
        Assert.Equal(63, otherModel.VolumePercent, 4);
    }

    [Fact]
    public void Volume_WhenWriteFails_HidesUnavailableControlsAndDoesNotClaimRequestedReadback()
    {
        var control = new FakeEndpointVolumeControl { FailWrite = true };
        using var model = new DeviceVolumeViewModel("secondary", "Secondary", _ => control);

        model.VolumePercent = 10;

        Assert.False(model.HasControl);
        Assert.Contains("device write failed", model.Status);
        Assert.Equal(1, control.DisposeCalls);
        Assert.Equal(0.63f, control.State.Volume);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1d)]
    [InlineData(101d)]
    public void Volume_RejectsInvalidInputWithoutIssuingWrites(double value)
    {
        var control = new FakeEndpointVolumeControl();
        using var model = new DeviceVolumeViewModel("secondary", "Secondary", _ => control);

        Assert.Throws<ArgumentOutOfRangeException>(() => model.VolumePercent = value);

        Assert.Equal(0, control.VolumeWrites);
        Assert.True(model.HasControl);
    }
}
