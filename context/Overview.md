Project: SpeakerSplitter — mirror Windows system audio to additional playback devices.
Stack: C#, .NET (current LTS or newer), NAudio (WASAPI only). Console app first; no GUI until Milestone 4.

Core design rule: Capture via WasapiLoopbackCapture on the DEFAULT render endpoint.
The default endpoint keeps playing natively. Only play captured audio to OTHER endpoints.
Never allow the capture endpoint to also be an output (causes doubling/feedback).

Work in milestones. Stop after each one, summarize what you built, and wait for me.

M1 — Enumerate: List all active render endpoints (ID, friendly name, default flag,
     mix format: sample rate, channels, bit depth/encoding). Done when output matches
     Windows Sound settings.

M2 — Capture: Loopback-capture the default endpoint; print the capture WaveFormat and a
     live peak meter to the console. Done when meter moves with music and idles at silence.

M3 — Fan-out: For each selected non-default device, create its own BufferedWaveProvider
     + WasapiOut (shared mode). DataAvailable writes into every buffer. Underruns play
     silence. If a device's format differs from the capture format, resample/convert
     for that device only. Log buffer fill level per device once per second.
     Done when audio plays on default + 1 extra device for 10 minutes without glitches.

M4 — Drift + UX: If a buffer's fill drifts outside a target band, drop/pad samples to
     correct. Then add a minimal WPF window: device checkboxes (capture device disabled),
     per-device volume, start/stop, buffer meters.

Out of scope for now: ASIO, kernel streaming, WinUI, process-specific capture,
hot-plug handling (just log and stop cleanly on device removal).

Keep code small and readable. Explain any non-obvious WASAPI/NAudio behavior in comments.