namespace SpeakerSplitter.Tests;

public class DeviceSelectionTests
{
    private static readonly IReadOnlyList<EndpointInfo> Endpoints =
    [
        new("{0.0.0.0}.{aaaa-1111}", "Speakers", true, null),
        new("{0.0.0.0}.{bbbb-2222}", "Headphones", false, null),
        new("{0.0.0.0}.{cccc-3333}", "Monitor", false, null),
    ];

    [Theory]
    [InlineData("1")]
    [InlineData("aaaa")]
    [InlineData("AAAA-1111")]
    public void Select_RefusesTheDefaultEndpoint_ByNumberOrId(string request)
    {
        var result = DeviceSelection.Select(Endpoints, [request]);

        Assert.False(result.IsValid);
        Assert.Empty(result.Accepted);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Speakers", error);
        Assert.Contains("default output device", error);
    }

    [Fact]
    public void Select_AcceptsSeveralNonDefaultDevices_InRequestedOrder()
    {
        var result = DeviceSelection.Select(Endpoints, ["3", "bbbb"]);

        Assert.True(result.IsValid);
        Assert.Equal(["Monitor", "Headphones"], result.Accepted.Select(e => e.FriendlyName));
    }

    [Fact]
    public void Select_ReportsEveryProblem_AndStillAcceptsTheValidDevices()
    {
        var result = DeviceSelection.Select(Endpoints, ["1", "2", "3"]);

        Assert.False(result.IsValid);
        Assert.Equal(["Headphones", "Monitor"], result.Accepted.Select(e => e.FriendlyName));
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData("2", "2")]
    [InlineData("2", "bbbb")]
    public void Select_RejectsTheSameDeviceTwice(string first, string second)
    {
        var result = DeviceSelection.Select(Endpoints, [first, second]);

        Assert.Single(result.Accepted);
        var error = Assert.Single(result.Errors);
        Assert.Contains("Headphones", error);
        Assert.Contains("more than once", error);
    }

    [Theory]
    [InlineData("0", "no device number 0")]
    [InlineData("4", "no device number 4")]
    [InlineData("zzzz", "No active device matches")]
    [InlineData("0.0.0.0", "matches 3 devices")]
    [InlineData("  ", "empty value")]
    public void Select_RefusesUnknownOutOfRangeAndAmbiguousRequests(string request, string expected)
    {
        var result = DeviceSelection.Select(Endpoints, [request]);

        Assert.Empty(result.Accepted);
        Assert.Contains(expected, Assert.Single(result.Errors));
    }

    [Fact]
    public void Select_WithNoRequests_IsValidAndEmpty()
    {
        var result = DeviceSelection.Select(Endpoints, []);

        Assert.True(result.IsValid);
        Assert.Empty(result.Accepted);
    }

    [Theory]
    [InlineData("2,3", new[] { "2", "3" })]
    [InlineData("2 3", new[] { "2", "3" })]
    [InlineData(" 2, 3 ,", new[] { "2", "3" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void SplitList_SeparatesOnCommasAndSpaces(string? line, string[] expected)
    {
        Assert.Equal(expected, DeviceSelection.SplitList(line));
    }
}
