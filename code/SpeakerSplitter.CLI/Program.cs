using System.Reflection;
using System.Runtime.InteropServices;
using NAudio;
using SpeakerSplitter;

// Print the banner and usage.
var version = Assembly.GetExecutingAssembly().GetName().Version;

Console.WriteLine($"SpeakerSplitter {version}");
Console.WriteLine("Mirrors Windows system audio to additional playback devices.");
Console.WriteLine(CommandLine.Usage);
Console.WriteLine();

// Read the command line; a bad argument ends the run with exit code 2.
var options = CommandLine.Parse(args);
if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    return 2;
}

// List the active render endpoints, so the user can see what to mirror to.
var endpoints = EndpointCatalog.ListActive();
Console.WriteLine($"Active render endpoints ({endpoints.Count}):");
Console.WriteLine();
EndpointTable.Write(Console.Out, endpoints);
Console.WriteLine();

// Resolve any --device requests into endpoints; the default endpoint, which is the one captured, is refused.
var selection = DeviceSelection.Select(endpoints, options.Devices);

// Without --device on a real console, ask. Redirected input (scripts, tests) never prompts.
var interactive = options.Devices.Count == 0 && !Console.IsInputRedirected;
while (interactive)
{
    Console.Write("Device numbers to mirror to (for example 2,3), or Enter for none: ");
    selection = DeviceSelection.Select(endpoints, DeviceSelection.SplitList(Console.ReadLine()));
    if (selection.IsValid)
    {
        Console.WriteLine();
        break;
    }

    foreach (var error in selection.Errors)
    {
        Console.Error.WriteLine(error);
    }
}

// Anything that could not be honoured ends the run with exit code 2.
if (!selection.IsValid)
{
    foreach (var error in selection.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 2;
}

// Echo the chosen devices.
if (selection.Accepted.Count > 0)
{
    Console.WriteLine("Selected output devices:");
    foreach (var device in selection.Accepted)
    {
        Console.WriteLine($"  {device.FriendlyName}");
    }

    Console.WriteLine();
}

// Open loopback capture on the default endpoint and compare its format with what the table showed.
using var engine = SplitterEngine.ForDefaultOutput();
var captureFormat = engine.CaptureFormat;
var defaultFormat = endpoints.FirstOrDefault(e => e.IsDefault)?.Format;

Console.WriteLine($"Loopback capture format: {captureFormat.Describe()}");
Console.WriteLine(captureFormat == defaultFormat
    ? "Matches the default endpoint's mix format."
    : "Does NOT match the default endpoint's mix format.");

// The peak meter reads only 32-bit float and 16-bit PCM, so any other capture format ends the run.
if (!PeakMeter.IsSupported(captureFormat))
{
    Console.Error.WriteLine($"The peak meter supports 32-bit float and 16-bit PCM, not {captureFormat.Describe()}.");
    return 1;
}

// Stop on Enter or Ctrl+C: either one releases the main thread, which then shuts the engine down.
using var quit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) =>
{
    // Cancelling the default termination lets the normal exit path stop and dispose the capture.
    e.Cancel = true;
    quit.Set();
};
new Thread(() =>
{
    Console.ReadLine();
    quit.Set();
})
{ IsBackground = true }.Start();

// A session can also end on its own (a device is lost or the default output changes); keep the reason.
string? lossReason = null;
void Lose(string reason)
{
    // The first reason wins: unplugging the default device raises both a capture error and a default-device change.
    if (Interlocked.CompareExchange(ref lossReason, reason, null) is null)
    {
        quit.Set();
    }
}

// Run the session: start the engine, draw the meter and fill log, and wait for a stop request or a loss.
engine.Lost += Lose;
try
{
    // The engine re-checks the devices before opening anything, then mirrors capture to them.
    engine.Start(selection.Accepted);
    Console.WriteLine();
    Console.WriteLine($"Capturing the default endpoint; mirroring to {selection.Accepted.Count} other device(s).");
    foreach (var conversion in engine.Conversions)
    {
        Console.WriteLine($"  {conversion}");
    }

    Console.WriteLine("Press Enter or Ctrl+C to stop.");

    // The engine raises both events from timers, not the capture callback, so the bar can decay while no
    // data arrives. The meter and the fill log share the console line: a log line overwrites the meter row
    // (padded to its width) and the meter redraws on its next update.
    var renderLock = new object();
    var lastLine = "";
    engine.PeakUpdated += level =>
    {
        lock (renderLock)
        {
            var line = MeterBar.Render(level);
            if (line != lastLine)
            {
                Console.Write('\r' + line);
                lastLine = line;
            }
        }
    };
    engine.StatsUpdated += stats =>
    {
        var now = DateTime.Now;
        lock (renderLock)
        {
            foreach (var device in stats)
            {
                Console.Write('\r' + FillLog.Format(now, device.Name, device.Stats).PadRight(lastLine.Length) + '\n');
                lastLine = "";
            }
        }
    };
    quit.Wait();
}
catch (SplitterStartException exception)
{
    // A device or the default output changed after it was listed, so nothing was started: exit code 2.
    if (exception.Reason == StartFailure.DefaultOutputChanged)
    {
        Console.Error.WriteLine($"{exception.Message} Restart SpeakerSplitter.");
    }
    else
    {
        Console.Error.WriteLine("The output devices changed before capture started:");
        foreach (var problem in exception.Problems)
        {
            Console.Error.WriteLine(problem);
        }
    }

    return 2;
}
catch (Exception exception) when (exception is COMException or MmException or AggregateException
    or InvalidOperationException or NotSupportedException or ArgumentException)
{
    // Any other audio failure ends the run with exit code 1.
    Console.Error.WriteLine($"Audio session failed ({exception.GetType().Name}: {exception.Message}).");
    return 1;
}
finally
{
    // Runs on every exit path, including exceptions. Stop silences everything that calls back before the
    // objects it touches go away, and waits for the engine's timers so nothing draws after the final line.
    engine.Lost -= Lose;
    try
    {
        engine.Stop();
    }
    catch (AggregateException exception)
    {
        Console.Error.WriteLine($"Output cleanup failed: {exception}");
        Lose("One or more output resources could not be released.");
    }
}

// Report how the session ended: a requested stop exits 0, a loss exits 1.
Console.WriteLine();
if (lossReason is not null)
{
    Console.Error.WriteLine($"Audio session lost: {lossReason}");
    Console.Error.WriteLine("Exiting without recovery. Restart SpeakerSplitter to use the current devices.");
}

Console.WriteLine($"Stopped. Received {engine.BuffersReceived} buffers ({engine.BytesReceived} bytes).");
return lossReason is null ? 0 : 1;
