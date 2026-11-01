using SpeakerSplitter.Devices;

namespace SpeakerSplitter.Tests.Devices;

internal sealed class FakeEndpointVolumeControl : IEndpointVolumeControl
{
    public EndpointVolumeState State { get; set; } = new(0.63f, true, false, true);
    public bool FailRead { get; init; }
    public bool FailDispose { get; init; }
    public int ReadCalls { get; private set; }
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

    public void Dispose()
    {
        DisposeCalls++;
        if (FailDispose)
        {
            throw new InvalidOperationException("device dispose failed");
        }
    }
}
