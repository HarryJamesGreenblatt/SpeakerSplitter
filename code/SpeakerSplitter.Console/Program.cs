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
