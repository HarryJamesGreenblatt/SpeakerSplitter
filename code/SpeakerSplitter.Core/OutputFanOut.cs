using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace SpeakerSplitter;

/// <summary>
/// Plays the captured audio on several non-default endpoints at once. Each device gets its own buffer and its
/// own shared-mode <c>WasapiOut</c>, converted to that device's format when it differs from the capture, so a
/// slow or failing device never holds up the others.
/// </summary>
public sealed class OutputFanOut : IDisposable
{
    #region Fields

    /// <summary>One output per chosen device, in the order the devices were given.</summary>
    private readonly List<DeviceOutput> _outputs = [];

    /// <summary>Non-zero once <see cref="Dispose"/> has begun; guards against disposing twice.</summary>
    private int _disposed;

    #endregion

    #region Properties

    /// <summary>One line per device that is being converted; devices that already match the capture are not listed.</summary>
    public IReadOnlyList<string> Conversions =>
        _outputs.Where(output => output.Conversion is not null).Select(output => $"{output.Name}: converting {output.Conversion}").ToList();

    #endregion

    #region Events

    /// <summary>
    /// Raised with a readable reason when an output stops without being asked to. Signalling only: the session
    /// stops capture and outputs on its main thread, not on the playback thread that raises this.
    /// </summary>
    public event Action<string>? Lost;

    #endregion

    #region Constructor(s)

    /// <summary>
    /// Opens one output per endpoint. Nothing is opened if any endpoint is the default or the capture endpoint,
    /// and everything already opened is released if a later output fails to initialize.
    /// </summary>
    /// <param name="format">The capture format, which is what each device's buffer holds.</param>
    /// <param name="captureEndpointId">The endpoint that is being captured; it must never also be an output.</param>
    /// <param name="endpoints">The endpoints to mirror to.</param>
    /// <param name="createOutput">Creates a player for an endpoint; defaults to shared-mode WASAPI, and lets tests substitute a fake.</param>
    /// <exception cref="ArgumentException">An endpoint is the default or capture endpoint.</exception>
    /// <exception cref="InvalidOperationException">An output could not be initialized.</exception>
    public OutputFanOut(
        WaveFormat format,
        string captureEndpointId,
        IReadOnlyList<EndpointInfo> endpoints,
        Func<EndpointInfo, IWavePlayer>? createOutput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(captureEndpointId);

        // Refuse the default and capture endpoints first, before any output is opened.
        foreach (var endpoint in endpoints)
        {
            if (endpoint.IsDefault || string.Equals(endpoint.Id, captureEndpointId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"{endpoint.FriendlyName} is the default or capture endpoint. Mirroring to it would double or feed back the audio.",
                    nameof(endpoints));
            }
        }

        // Open the outputs; if one fails, release the ones already open and report both failures if cleanup also fails.
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

    #endregion

    #region Methods

    /// <summary>Starts playback on every output. Each plays silence until audio arrives.</summary>
    /// <exception cref="ObjectDisposedException">The fan-out has been disposed.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        foreach (var output in _outputs)
        {
            output.Player.Play();
        }
    }

    /// <summary>
    /// Copies one captured buffer into every device's own buffer, so each device plays independently.
    /// Called on the capture thread.
    /// </summary>
    /// <param name="buffer">The captured audio.</param>
    /// <param name="count">The number of valid bytes in <paramref name="buffer"/>.</param>
    public void AddSamples(byte[] buffer, int count)
    {
        foreach (var output in _outputs)
        {
            output.Buffer.AddSamples(buffer, 0, count);
        }
    }

    /// <summary>Reports each device's buffer fill and counters. Safe to call from any thread, including after <see cref="Dispose"/>.</summary>
    public IReadOnlyList<DeviceBufferStats> Snapshot() =>
        _outputs.Select(output => new DeviceBufferStats(output.Name, output.Stats)).ToList();

    /// <summary>
    /// Stops and releases every output. A failing driver does not stop the rest from being released; the
    /// failures are reported together. Calling it again does nothing.
    /// </summary>
    /// <exception cref="AggregateException">Some output resources could not be released.</exception>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseAll(_outputs.Select(output => (Action)output.Dispose));
    }

    /// <summary>Forwards an output's loss to <see cref="Lost"/>, unless the fan-out is already being disposed.</summary>
    /// <param name="reason">A readable reason.</param>
    private void OnLost(string reason)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            Lost?.Invoke(reason);
        }
    }

    /// <summary>Runs every release even if some throw, then reports the failures together.</summary>
    /// <param name="releases">The release steps to run.</param>
    /// <exception cref="AggregateException">At least one release threw.</exception>
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

    #endregion

    #region Nested Types

    /// <summary>One device's buffer, optional converter and shared-mode player, plus what is needed to release them.</summary>
    private sealed class DeviceOutput : IDisposable
    {
        #region Fields

        /// <summary>The endpoint this output plays on.</summary>
        private readonly EndpointInfo _endpoint;

        /// <summary>Reports an unexpected stop to the owning fan-out.</summary>
        private readonly Action<string> _lost;

        /// <summary>The opened device, or <see langword="null"/> when a test supplied the player.</summary>
        private readonly MMDevice? _device;

        #endregion

        #region Properties

        /// <summary>The buffer the capture thread writes into and the player reads from.</summary>
        public MeteredBuffer Buffer { get; }

        /// <summary>The player that renders this device's audio.</summary>
        public IWavePlayer Player { get; }

        /// <summary>The device's friendly name.</summary>
        public string Name => _endpoint.FriendlyName;

        /// <summary>A short description of the conversion, or <see langword="null"/> when the device already matches the capture and receives the audio untouched.</summary>
        public string? Conversion { get; }

        /// <summary>The buffer's current fill and counters.</summary>
        public BufferStats Stats => Buffer.Stats;

        #endregion

        #region Constructor(s)

        /// <summary>Opens the device, builds its buffer and optional converter, and initializes the player on them.</summary>
        /// <param name="endpoint">The endpoint to play on.</param>
        /// <param name="format">The capture format, which the buffer holds.</param>
        /// <param name="createOutput">Creates the player; <see langword="null"/> opens a shared-mode <c>WasapiOut</c>.</param>
        /// <param name="lost">Called when the player stops unexpectedly.</param>
        /// <exception cref="InvalidOperationException">The output could not be opened or initialized.</exception>
        public DeviceOutput(
            EndpointInfo endpoint,
            WaveFormat format,
            Func<EndpointInfo, IWavePlayer>? createOutput,
            Action<string> lost)
        {
            _endpoint = endpoint;
            _lost = lost;
            Buffer = new MeteredBuffer(format, TimeSpan.FromMilliseconds(300));

            try
            {
                // Open the player: a real shared-mode WASAPI output, or whatever the caller supplied.
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

                // Convert for this device only when its mix format differs from the capture.
                IWaveProvider source = Buffer;
                if (endpoint.Format is { } device && FormatConversion.NeedsConversion(format, device))
                {
                    source = FormatConversion.Create(Buffer, device);
                    Conversion = FormatConversion.Describe(format, device);
                }

                Player.Init(source);
                Player.PlaybackStopped += OnPlaybackStopped;
            }
            catch (Exception initializationError)
            {
                // Release whatever was opened; keep both failures if the release also fails.
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

        #endregion

        #region Methods

        /// <summary>Stops listening for playback ending, then stops and releases the player and the device.</summary>
        public void Dispose()
        {
            Player.PlaybackStopped -= OnPlaybackStopped;
            ReleaseAll([Player.Stop, Player.Dispose, () => _device?.Dispose()]);
        }

        /// <summary>Reports that playback ended without being asked to, with the exception when there was one.</summary>
        /// <param name="sender">The player that stopped.</param>
        /// <param name="args">Carries the exception when playback failed.</param>
        private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
        {
            _lost(args.Exception is null
                ? $"Output '{_endpoint.FriendlyName}' stopped unexpectedly."
                : $"Output '{_endpoint.FriendlyName}' stopped ({args.Exception.GetType().Name}: {args.Exception.Message}).");
        }

        #endregion
    }

    #endregion
}
