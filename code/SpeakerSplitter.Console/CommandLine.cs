namespace SpeakerSplitter;

public sealed record CommandLineOptions(IReadOnlyList<string> Devices, string? Error);

public static class CommandLine
{
    public const string Usage = "Usage: SpeakerSplitter.Console [--device <number|id-part>]...";

    private const string DeviceOption = "--device";

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        var devices = new List<string>();

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
}
