using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Tests.Devices;

internal sealed class FakeEndpointVolumeControl : IEndpointVolumeControl
{
    public EndpointVolumeState State { get; set; } = new(0.63f, true, false, true);
    public bool FailRead { get; init; }
    public bool FailDispose { get; init; }
    public bool FailWrite { get; init; }
    public float VolumeStep { get; init; }
    public int ReadCalls { get; private set; }
    public int VolumeWrites { get; private set; }
    public int MuteWrites { get; private set; }
    public int DisposeCalls { get; private set; }

    public EndpointVolumeState GetState()
    {
        ReadCalls++;
        if (FailRead)
        {
            throw new InvalidOperationException("device read failed");
        }

        return State;
    }

    public EndpointVolumeState SetVolume(float volume)
    {
        VolumeWrites++;
        if (FailWrite)
        {
            throw new InvalidOperationException("device write failed");
        }

        var actual = VolumeStep > 0 ? MathF.Round(volume / VolumeStep) * VolumeStep : volume;
        State = State with { Volume = actual };
        return State;
    }

    public EndpointVolumeState SetMute(bool muted)
    {
        MuteWrites++;
        if (FailWrite)
        {
            throw new InvalidOperationException("device write failed");
        }

        State = State with { Muted = muted };
        return State;
    }

    public void Dispose()
    {
        DisposeCalls++;
        if (FailDispose)
        {
            throw new InvalidOperationException("device dispose failed");
        }
    }
}
