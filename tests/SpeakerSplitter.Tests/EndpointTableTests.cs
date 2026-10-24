namespace SpeakerSplitter.Tests;

public class EndpointTableTests
{
    [Fact]
    public void Write_ShowsNotAvailableForDeviceWithoutFormat_AndStillListsOthers()
    {
        var endpoints = new[]
        {
            new EndpointInfo("a", "Broken Device", false, null),
            new EndpointInfo("b", "Speakers", true, new MixFormatInfo(48000, 2, 32, "IeeeFloat")),
        };
        var writer = new StringWriter();

        EndpointTable.Write(writer, endpoints);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(lines, line => line.StartsWith("Broken Device") && line.Contains("n/a"));
        Assert.Contains(lines, line => line.StartsWith("Speakers") && line.Contains("48000") && line.Contains("IeeeFloat"));
    }
}
