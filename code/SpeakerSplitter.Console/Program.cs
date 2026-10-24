using System.Reflection;

var version = Assembly.GetExecutingAssembly().GetName().Version;

Console.WriteLine($"SpeakerSplitter {version}");
Console.WriteLine("Mirrors Windows system audio to additional playback devices.");
Console.WriteLine();
Console.WriteLine("Usage: SpeakerSplitter");
Console.WriteLine("  (no options yet - endpoint listing arrives in milestone M1)");
