namespace SpeakerSplitter;

/// <summary>Why <see cref="SplitterEngine.Start"/> refused to start.</summary>
public enum StartFailure
{
    /// <summary>The default output changed after the capture device was opened, so capture would be on the wrong one.</summary>
    DefaultOutputChanged,

    /// <summary>At least one chosen output is no longer active, is now the default, or was chosen twice.</summary>
    TargetsChanged,
}

/// <summary>
/// Raised by <see cref="SplitterEngine.Start"/> before any output is opened or capture starts, because what the
/// caller chose is no longer valid.
/// </summary>
/// <param name="reason">Which check failed.</param>
/// <param name="message">A readable summary of the failure.</param>
/// <param name="problems">One line per chosen output that cannot be used; empty for <see cref="StartFailure.DefaultOutputChanged"/>.</param>
public sealed class SplitterStartException(StartFailure reason, string message, IReadOnlyList<string> problems)
    : Exception(message)
{
    /// <summary>Which check failed.</summary>
    public StartFailure Reason { get; } = reason;

    /// <summary>One line per chosen output that cannot be used; empty when the default output changed.</summary>
    public IReadOnlyList<string> Problems { get; } = problems;
}
