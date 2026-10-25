namespace SpeakerSplitter;

public enum StartFailure
{
    // The default output changed after the capture device was opened, so capture would be on the wrong one.
    DefaultOutputChanged,

    // At least one chosen output is no longer active, is now the default, or was chosen twice.
    TargetsChanged,
}

// Start refused before opening any output or starting capture, because what the caller chose is no longer valid.
public sealed class SplitterStartException(StartFailure reason, string message, IReadOnlyList<string> problems)
    : Exception(message)
{
    public StartFailure Reason { get; } = reason;

    // One line per chosen output that cannot be used; empty for DefaultOutputChanged.
    public IReadOnlyList<string> Problems { get; } = problems;
}
