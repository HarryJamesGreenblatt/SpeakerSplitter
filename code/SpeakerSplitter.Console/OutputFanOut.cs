using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeakerSplitter;

public sealed class OutputFanOut : IDisposable
{
    private readonly List<DeviceOutput> _outputs = [];
    private int _disposed;

    public OutputFanOut(
        WaveFormat format,
        string captureEndpointId,
        IReadOnlyList<EndpointInfo> endpoints,
        Func<EndpointInfo, IWavePlayer>? createOutput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureEndpointId);
        foreach (var endpoint in endpoints)
        {
            if (endpoint.IsDefault || string.Equals(endpoint.Id, captureEndpointId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{endpoint.FriendlyName} is the default or capture endpoint. Mirroring to it would double or feed back the audio.",
                    nameof(endpoints));
            }
        }

        try
        {
            foreach (var endpoint in endpoints)
            {
                _outputs.Add(new DeviceOutput(endpoint, format, createOutput, OnLost));
            }
        }
        catch (Exception initializationError)
        {
            try
            {
                Dispose();
            }
            catch (AggregateException cleanupError)
            {
                throw new AggregateException("Output initialization and cleanup failed.", initializationError, cleanupError);
            }

            throw;
        }
    }

    // Signalling only: the session stops capture and outputs on its main thread, not a playback thread.
    public event Action<string>? Lost;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        foreach (var output in _outputs)
        {
            output.Player.Play();
        }
    }

    public void AddSamples(byte[] buffer, int count)
    {
        foreach (var output in _outputs)
        {
            output.Buffer.AddSamples(buffer, 0, count);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseAll(_outputs.Select(output => (Action)output.Dispose));
    }

    private void OnLost(string reason)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Lost?.Invoke(reason);
        }
    }

    private static void ReleaseAll(IEnumerable<Action> releases)
    {
        List<Exception> errors = [];
        foreach (var release in releases)
        {
            try
            {
                release();
            }
            catch (Exception error)
            {
                // One failing driver must not prevent releasing the remaining devices.
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Could not release all output resources.", errors);
        }
    }

    private sealed class DeviceOutput : IDisposable
    {
        private readonly EndpointInfo _endpoint;
        private readonly Action<string> _lost;
        private readonly MMDevice? _device;

        public DeviceOutput(
            EndpointInfo endpoint,
            WaveFormat format,
            Func<EndpointInfo, IWavePlayer>? createOutput,
            Action<string> lost)
        {
            _endpoint = endpoint;
            _lost = lost;
            Buffer = new BufferedWaveProvider(format)
            {
                BufferDuration = TimeSpan.FromMilliseconds(300),
                ReadFully = true,
                DiscardOnBufferOverflow = true,
            };

            try
            {
                if (createOutput is null)
                {
                    using var enumerator = new MMDeviceEnumerator();
                    _device = enumerator.GetDevice(endpoint.Id);
                    // Shared mode coexists with other apps and never takes exclusive control.
                    Player = new WasapiOut(_device, AudioClientShareMode.Shared, true, 100);
                }
                else
                {
                    Player = createOutput(endpoint);
                }

                Player.Init(Buffer);
                Player.PlaybackStopped += OnPlaybackStopped;
            }
            catch (Exception initializationError)
            {
                try
                {
                    ReleaseAll([() => Player?.Dispose(), () => _device?.Dispose()]);
                }
                catch (AggregateException cleanupError)
                {
                    throw new AggregateException(
                        $"Could not initialize or release output '{endpoint.FriendlyName}'.", initializationError, cleanupError);
                }

                throw new InvalidOperationException(
                    $"Could not initialize output '{endpoint.FriendlyName}': {initializationError.Message}", initializationError);
            }
        }

        public BufferedWaveProvider Buffer { get; }

        public IWavePlayer Player { get; }

        public void Dispose()
        {
            Player.PlaybackStopped -= OnPlaybackStopped;
            ReleaseAll([Player.Stop, Player.Dispose, () => _device?.Dispose()]);
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
        {
            _lost(args.Exception is null
                ? $"Output '{_endpoint.FriendlyName}' stopped unexpectedly."
                : $"Output '{_endpoint.FriendlyName}' stopped ({args.Exception.GetType().Name}: {args.Exception.Message}).");
        }
    }
}
