# SpeakerSplitter

Mirror Windows system audio to additional playback devices.

Captures the **default** render endpoint with WASAPI loopback (NAudio) and plays the audio on other selected endpoints. The default endpoint keeps playing natively and is never used as an output, so there is no doubling or feedback.

**Stack:** C#, .NET (current LTS or newer), NAudio (WASAPI only). Console app first; WPF UI in milestone 4.

## Milestones

| | Milestone |
|---|---|
| M1 | Enumerate render endpoints |
| M2 | Capture default endpoint (loopback + peak meter) |
| M3 | Fan-out to non-default devices |
| M4 | Multi-device mixing controller (WPF) |

Scope, acceptance criteria and status are tracked in Azure DevOps (`sequitur-scrum` / `SpeakerSplitter`). The original seed is in [`context/Overview.md`](context/Overview.md).

## Status

The shared Core engine and CLI mirror the default endpoint to selected additional outputs. M4 adds a WPF controller, with app-only gain, device endpoint volume/mute, opt-in primary linking and diagnostics tracked as separate PBIs. Audio drift correction remains deferred.

## WPF Controller

Run from the repository root:

```powershell
dotnet run --project .\code\SpeakerSplitter.Wpf
```

The default device is visible but cannot be selected as a mirror target. Volume, mute and diagnostics controls are separate follow-on work; the initial shell does not change Windows volume settings.

Select one or more additional outputs and press **Start**. Device selection and refresh are locked while mirroring; press **Stop** to release the session. If an output is lost or the default changes, the shell stops and reports the reason. Refresh the device list before restarting.
