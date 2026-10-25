using System.Reflection;
using SpeakerSplitter;

var version = Assembly.GetExecutingAssembly().GetName().Version;

Console.WriteLine($"SpeakerSplitter {version}");
Console.WriteLine("Mirrors Windows system audio to additional playback devices.");
Console.WriteLine();

var endpoints = EndpointCatalog.ListActive();
Console.WriteLine($"Active render endpoints ({endpoints.Count}):");
Console.WriteLine();
EndpointTable.Write(Console.Out, endpoints);
Console.WriteLine();

using var source = new LoopbackSource();
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
source.Data += (buffer, count) =>
{
    Interlocked.Increment(ref buffers);
    Interlocked.Add(ref bytes, count);

    meter.Push(PeakMeter.Compute(buffer.AsSpan(0, count), captureFormat));
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
}) { IsBackground = true }.Start();

source.Start();
Console.WriteLine();
Console.WriteLine("Capturing the default endpoint. Press Enter or Ctrl+C to stop.");

// Rendering from a timer, not the capture callback, lets the bar decay while no data arrives.
var renderLock = new object();
var lastLine = "";
var timer = new Timer(_ =>
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

await timer.DisposeAsync();
source.Stop();
Console.WriteLine();
Console.WriteLine($"Stopped. Received {buffers} buffers ({bytes} bytes).");
return 0;
