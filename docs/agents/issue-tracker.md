# Issue tracker: GitHub

Issues and specs live in GitHub Issues for
AxelerateEngineering/mcp-servers-for-revit. Use the `gh` CLI
from this clone; use `--repo AxelerateEngineering/mcp-servers-for-revit`
when an explicit repository is needed.

## Conventions

- Create: `gh issue create --title "..." --body-file <file>`
- Read: `gh issue view <number> --comments`
- Read structured details:
  `gh issue view <number> --json number,title,body,labels,comments`
- List: `gh issue list --state open --json number,title,body,labels,comments`
  Add `--label` and `--state` filters as needed.
- Comment: `gh issue comment <number> --body-file <file>`
- Add or remove labels:
  `gh issue edit <number> --add-label "..."`
  `gh issue edit <number> --remove-label "..."`
- Close: `gh issue close <number> --comment "..."`

For multiline bodies, write the exact text to a temporary file
and pass `--body-file`.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill instructions

"Publish to the issue tracker" means create a GitHub issue.
"Fetch the relevant ticket" means read the issue with comments.

## Wayfinding operations

- Map: one issue labelled `wayfinder:map` containing Notes,
  Decisions-so-far, and Fog.
- Child tickets: link to the map using `--parent` when creating
  an issue. Use `wayfinder:research`, `wayfinder:prototype`,
  `wayfinder:grilling`, or `wayfinder:task`.
- If sub-issues are unavailable, maintain a task list in the
  map and add `Part of #<map>` to each child.
- Record blockers with `gh issue edit <child> --add-blocked-by <blocker>`.
  If dependencies are unavailable, use a `Blocked by: #<number>`
  line in the child body.
- Frontier: select the first open, unassigned child in map
  order whose blockers are all closed.
- Claim: `gh issue edit <number> --add-assignee "@me"`
- Resolve: comment with the outcome, close the child, and add
  a summary and link to the map's Decisions-so-far.
