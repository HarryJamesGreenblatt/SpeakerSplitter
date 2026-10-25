namespace SpeakerSplitter.Tests;

public class CommandLineTests
{
    [Fact]
    public void Parse_CollectsEveryDeviceOption_InBothForms()
    {
        var options = CommandLine.Parse(["--device", "2", "--device=3", "--device", "bbbb"]);

        Assert.Null(options.Error);
        Assert.Equal(["2", "3", "bbbb"], options.Devices);
    }

    [Fact]
    public void Parse_WithNoArguments_SelectsNothing()
    {
        var options = CommandLine.Parse([]);

        Assert.Null(options.Error);
        Assert.Empty(options.Devices);
    }

    [Theory]
    [InlineData(new[] { "--device" }, "needs a device")]
    [InlineData(new[] { "--bogus" }, "Unknown argument '--bogus'")]
    [InlineData(new[] { "2" }, "Unknown argument '2'")]
    public void Parse_ReportsBadArguments(string[] args, string expected)
    {
        var options = CommandLine.Parse(args);

        Assert.Contains(expected, options.Error);
    }
}
