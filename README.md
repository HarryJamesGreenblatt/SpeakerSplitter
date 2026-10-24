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
| M4 | Drift correction + minimal WPF UX |

Scope, acceptance criteria and status are tracked in Azure DevOps (`sequitur-scrum` / `SpeakerSplitter`). The original seed is in [`context/Overview.md`](context/Overview.md).

## Status

Planning complete; implementation starts with M1.
