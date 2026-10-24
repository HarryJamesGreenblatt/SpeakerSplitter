# SpeakerSplitter Instructions

Project: SpeakerSplitter, a Windows console and WPF app (C#, .NET, NAudio/WASAPI) that mirrors system audio to additional playback devices.

Shared ADO, work item, git, commit and PR conventions live in the workspace-level `.github/copilot-instructions.md` (the `sequitur-scrum` workspace folder, one level above this repo). They apply here; this file only adds SpeakerSplitter specifics.

## Tracking

- ADO: org `sequitur-scrum`, project `SpeakerSplitter`. Epic #1 holds Features M1-M4 (milestones) and Feature #73 (Project Setup and Tooling).
- Before starting a milestone, look up its Features, PBIs and wiki pages in ADO. `context/Overview.md` is the original seed; ADO is the source of truth now.
- Milestone Features keep their label after the prefix, e.g. `SpeakerSplitter: M1 — Enumerate Render Endpoints`.
- Tags: `speakersplitter` plus `m1`-`m4`, `setup`, `testing` or `ui` as appropriate.

## Repositories

- ADO Git `SpeakerSplitter` (remote `ado`) is primary and the PR home.
- GitHub `HarryJamesGreenblatt/SpeakerSplitter` (public) is the mirror; `origin` pushes to both.

## Code Layout

- Source in `code/`, tests in `tests/`, solution `SpeakerSplitter.sln`.
- Shared build settings in `Directory.Build.props` (net10.0-windows, nullable, warnings as errors).
- Design rule: capture the DEFAULT render endpoint (WASAPI loopback) and only play to OTHER endpoints; never use the capture endpoint as an output.
