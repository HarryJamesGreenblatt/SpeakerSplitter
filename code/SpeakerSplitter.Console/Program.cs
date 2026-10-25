using System.Reflection;
using System.Runtime.InteropServices;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SpeakerSplitter;

var version = Assembly.GetExecutingAssembly().GetName().Version;

Console.WriteLine($"SpeakerSplitter {version}");
Console.WriteLine("Mirrors Windows system audio to additional playback devices.");
Console.WriteLine(CommandLine.Usage);
Console.WriteLine();

var options = CommandLine.Parse(args);
if (options.Error is not null)
{
    Console.Error.WriteLine(options.Error);
    return 2;
}

var endpoints = EndpointCatalog.ListActive();
Console.WriteLine($"Active render endpoints ({endpoints.Count}):");
Console.WriteLine();
EndpointTable.Write(Console.Out, endpoints);
Console.WriteLine();

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

if (!selection.IsValid)
{
    foreach (var error in selection.Errors)
    {
        Console.Error.WriteLine(error);
    }

    return 2;
}

if (selection.Accepted.Count > 0)
{
    Console.WriteLine("Selected output devices:");
    foreach (var device in selection.Accepted)
    {
        Console.WriteLine($"  {device.FriendlyName}");
    }

    Console.WriteLine();
}

using var enumerator = new MMDeviceEnumerator();
using var captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
using var source = new LoopbackSource(new WasapiLoopbackCapture(captureDevice));
var captureFormat = MixFormatInfo.From(source.WaveFormat);
var defaultFormat = endpoints.FirstOrDefault(e => e.IsDefault)?.Format;

Console.WriteLine($"Loopback capture format: {captureFormat.Describe()}");
Console.WriteLine(captureFormat == defaultFormat
    ? "Matches the default endpoint's mix format."
    : "Does NOT match the default endpoint's mix format.");

if (!PeakMeter.IsSupported(captureFormat))
{
    Console.Error.WriteLine($"The peak meter supports 32-bit float and 16-bit PCM, not {captureFormat.Describe()}.");
    return 1;
}

var meter = new LevelMeter();
long buffers = 0;
long bytes = 0;
OutputFanOut? outputs = null;
source.Data += (buffer, count) =>
{
    Interlocked.Increment(ref buffers);
    Interlocked.Add(ref bytes, count);

    meter.Push(PeakMeter.Compute(buffer.AsSpan(0, count), captureFormat));
    outputs?.AddSamples(buffer, count);
};

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

string? lossReason = null;
void Lose(string reason)
{
    // The first reason wins: unplugging the default device raises both a capture error and a default-device change.
    if (Interlocked.CompareExchange(ref lossReason, reason, null) is null)
    {
        quit.Set();
    }
}

source.Lost += Lose;
var watcher = new DefaultOutputWatcher();
Timer? timer = null;
try
{
    watcher.Changed += () => Lose("The default output device changed.");

    // The default may have changed since the selection was made. The watcher above now covers every
    // later change, so checking the selection against the current list closes the gap before Start.
    var currentEndpoints = EndpointCatalog.ListActive();
    if (!string.Equals(
        currentEndpoints.FirstOrDefault(endpoint => endpoint.IsDefault)?.Id,
        captureDevice.ID,
        StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("The default output device changed before capture started. Restart SpeakerSplitter.");
        return 2;
    }

    var current = DeviceSelection.Select(currentEndpoints, selection.Accepted.Select(d => d.Id));
    if (!current.IsValid)
    {
        Console.Error.WriteLine("The output devices changed before capture started:");
        foreach (var error in current.Errors)
        {
            Console.Error.WriteLine(error);
        }

        return 2;
    }

    outputs = new OutputFanOut(source.WaveFormat, captureDevice.ID, current.Accepted);
    outputs.Lost += Lose;
    outputs.Start();
    source.Start();
    Console.WriteLine();
    Console.WriteLine($"Capturing the default endpoint; mirroring to {current.Accepted.Count} other device(s).");
    Console.WriteLine("Press Enter or Ctrl+C to stop.");

    // Rendering from a timer, not the capture callback, lets the bar decay while no data arrives.
    var renderLock = new object();
    var lastLine = "";
    timer = new Timer(_ =>
    {
        lock (renderLock)
        {
            var line = PeakMeter.Render(meter.Level);
            if (line != lastLine)
            {
                Console.Write('\r' + line);
                lastLine = line;
            }
        }
    }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
    quit.Wait();
}
catch (Exception exception) when (exception is COMException or MmException or AggregateException
    or InvalidOperationException or NotSupportedException or ArgumentException)
{
    Console.Error.WriteLine($"Audio session failed ({exception.GetType().Name}: {exception.Message}).");
    return 1;
}
finally
{
    // Runs on every exit path, including exceptions. Anything that calls back is silenced before the
    // objects it touches go away, and the timer is awaited so nothing draws after the final line.
    try
    {
        source.Lost -= Lose;
        watcher.Dispose();
        if (timer is not null)
        {
            await timer.DisposeAsync();
        }
    }
    finally
    {
        try
        {
            source.Stop();
        }
        finally
        {
            if (outputs is not null)
            {
                outputs.Lost -= Lose;
                try
                {
                    outputs.Dispose();
                }
                catch (AggregateException exception)
                {
                    Console.Error.WriteLine($"Output cleanup failed: {exception}");
                    Lose("One or more output resources could not be released.");
                }
            }
        }
    }
}

Console.WriteLine();
if (lossReason is not null)
{
    Console.Error.WriteLine($"Audio session lost: {lossReason}");
    Console.Error.WriteLine("Exiting without recovery. Restart SpeakerSplitter to use the current devices.");
}

Console.WriteLine($"Stopped. Received {buffers} buffers ({bytes} bytes).");
return lossReason is null ? 0 : 1;
