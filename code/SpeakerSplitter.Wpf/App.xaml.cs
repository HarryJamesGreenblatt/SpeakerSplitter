using System.Windows;

namespace SpeakerSplitter.Wpf;

/// <summary>The WPF host; audio processing remains in the shared Core engine.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        if (Environment.ExitCode != 0)
        {
            e.ApplicationExitCode = Environment.ExitCode;
        }

        base.OnExit(e);
    }
}
