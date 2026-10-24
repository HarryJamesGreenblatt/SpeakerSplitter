This project uses Azure DevOps (org: sequitur-scrum, project: SpeakerSplitter) as its system of record for work tracking. Always check whether the Azure DevOps MCP server has a tool relevant to the user's request.

Before starting a milestone, look up its work items (and wiki pages) in ADO for current scope and status. Milestone definitions in `context/Overview.md` are the seed; ADO is the source of truth once items exist.

Keep ADO current as work progresses, not just at the end, so intermediate progress shows on the board (Scrum template states):
- Starting a PBI: set the PBI to `Committed` and its parent Feature to `In Progress` (if still `New`); the Epic to `In Progress` when its first Feature starts.
- Starting a Task: set it to `In Progress`. Finishing it: set it to `Done` as soon as its acceptance criterion is met (update Tasks one by one, not in a batch at the end).
- Finishing a PBI: set it to `Done` once all its Tasks are `Done` and its acceptance criteria are met. Finishing a Feature or Epic: set `Done` once all children are `Done`.
- Add a short comment on the PBI or Feature when something notable happens (evidence, results, blockers, anomalies). File a Bug for defects found.
- Task states: `To Do`, `In Progress`, `Done`. PBI states: `New`, `Approved`, `Committed`, `Done`. Feature and Epic states: `New`, `In Progress`, `Done`.

Work item naming convention: Epic, Feature, PBI and Bug titles are `<Project>: <Title Case Title>`, e.g. `SpeakerSplitter: Enumerate Render Endpoints`. Milestone Features keep their milestone label after the prefix, e.g. `SpeakerSplitter: M1 — Enumerate Render Endpoints`. Task titles are Title Case with no project prefix (their parent PBI already carries it). Each Task models one acceptance criterion of its parent PBI, and all items are assigned to HJG@sequitur.solutions.

Git conventions:
- Hosting: the ADO Git repo (`ado`) is the PR home and primary; the public GitHub repo (`origin`, HarryJamesGreenblatt/SpeakerSplitter) is a mirror. `origin` has both push URLs, so `git push origin <branch>` updates both. Do not open PRs on GitHub. After a PR is completed in ADO, run `git switch main && git pull ado main && git push origin main` so GitHub catches up.
- Workflow: one branch and one PR per PBI, e.g. `m1/enumerate-render-endpoints`, with one commit per Task. Create the PR in ADO (`az repos pr create ... --work-items <pbi> <tasks>`), and reference the PBI and its Tasks as `#<id>` in the PR description. Include a `git diff --stat main...<branch>` block (in a code fence) at the end of the PR description. The Azure Boards GitHub app is connected, but `AB#<id>` is only needed for GitHub-hosted commits.
- Commit message format: subject is `type: Title Case Summary` (types: feat, fix, docs, chore, test, refactor), then a blank line and a short `- ` bullet list of what changed, then `#<taskid>` on the line directly under the last bullet (no blank line) so the pushed commit auto-links to that Task in ADO. If a commit references several work items, put each `#<id>` on its own line. Commits not tied to a Task (e.g. docs) omit the line. Do not add Co-authored-by trailers, session footers or other signatures.
