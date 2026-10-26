namespace SpeakerSplitter;

/// <summary>What the command line asked for, or why it could not be understood.</summary>
/// <param name="Devices">The device requests given with <c>--device</c>, in order: a number or part of a device ID each.</param>
/// <param name="Error">A readable message when an argument was wrong, or <see langword="null"/> when the line parsed.</param>
public sealed record CommandLineOptions(IReadOnlyList<string> Devices, string? Error);

/// <summary>Reads the command-line app's arguments.</summary>
public static class CommandLine
{
    #region Constants

    /// <summary>The usage line printed at startup.</summary>
    public const string Usage = "Usage: SpeakerSplitter.CLI [--device <number|id-part>]...";

    /// <summary>The option that selects an output device; it may be repeated.</summary>
    private const string DeviceOption = "--device";

    #endregion

    #region Methods

    /// <summary>
    /// Parses the arguments, accepting <c>--device value</c> and <c>--device=value</c> any number of times.
    /// Parsing stops at the first argument it does not recognise.
    /// </summary>
    /// <param name="args">The arguments the program was started with.</param>
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var devices = new List<string>();

        // Walk the arguments; a --device given without a value, or any unknown argument, ends parsing with an error.
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == DeviceOption)
            {
                if (i + 1 >= args.Count)
                {
                    return new CommandLineOptions(devices, $"{DeviceOption} needs a device number or part of its ID.");
                }

                devices.Add(args[++i]);
            }
            else if (arg.StartsWith(DeviceOption + "=", StringComparison.Ordinal))
            {
                devices.Add(arg[(DeviceOption.Length + 1)..]);
            }
            else
            {
                return new CommandLineOptions(devices, $"Unknown argument '{arg}'.");
            }
        }

        return new CommandLineOptions(devices, null);
    }

    #endregion
}
