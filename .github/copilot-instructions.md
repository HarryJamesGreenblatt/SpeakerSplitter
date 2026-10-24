This project uses Azure DevOps (org: sequitur-scrum, project: SpeakerSplitter) as its system of record for work tracking. Always check whether the Azure DevOps MCP server has a tool relevant to the user's request.

Before starting a milestone, look up its work items (and wiki pages) in ADO for current scope and status. After finishing work, update the relevant work item. Milestone definitions in `context/Overview.md` are the seed; ADO is the source of truth once items exist.

Work item naming convention: Epic, Feature, PBI and Bug titles are `<Project>: <Title Case Title>`, e.g. `SpeakerSplitter: Enumerate Render Endpoints`. Milestone Features keep their milestone label after the prefix, e.g. `SpeakerSplitter: M1 — Enumerate Render Endpoints`. Task titles are Title Case with no project prefix (their parent PBI already carries it). Each Task models one acceptance criterion of its parent PBI, and all items are assigned to HJG@sequitur.solutions.

Git conventions:
- Hosting: GitHub (`origin`, HarryJamesGreenblatt/SpeakerSplitter, public) is primary, mirrored to the ADO Git repo (`ado`). `origin` has both push URLs, so `git push` updates both. After a PR merges on GitHub, run `git switch main && git pull origin main && git push` so ADO catches up.
- Workflow: one branch and one PR per PBI, e.g. `m1/enumerate-render-endpoints`. Reference the PBI as `AB#<id>` in the PR title or description so Azure Boards links it.
- Commit message format: subject is `type: Title Case Summary` (types: feat, fix, docs, chore, test, refactor), then a blank line and a short `- ` bullet list of what changed. Do not add Co-authored-by trailers, session footers or other signatures.
