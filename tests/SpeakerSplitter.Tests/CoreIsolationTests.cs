using System.Reflection;

namespace SpeakerSplitter.Tests;

public class CoreIsolationTests
{
    private static readonly string[] HostOnly =
    [
        "System.Console",
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
        "System.Windows.Forms",
        "NAudio.WinForms",
    ];

    // The engine is shared by every front end, so it must not depend on one. The referenced-assembly list is
    // what the compiled code actually uses, so this fails as soon as Core touches the console or a UI framework.
    [Fact]
    public void Core_ReferencesNeitherTheConsoleNorAWindowingFramework()
    {
        var core = ReferencedAssemblies(typeof(SplitterEngine));
        var console = ReferencedAssemblies(typeof(CommandLine));

        Assert.Empty(core.Intersect(HostOnly));

        // The same check must see System.Console where it is genuinely used, or the assertion above proves nothing.
        Assert.Contains("System.Console", console);
    }

    private static string[] ReferencedAssemblies(Type type) =>
        type.Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();
}
