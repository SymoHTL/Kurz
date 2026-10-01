---
name: diagram-change-walk
description: Living diagram - the walk one change takes from a branch to main, in the order the gates bite, with every place it bounces back; the procedure itself is the skill change-walk
metadata:
  type: reference
---

The order is the order in which a change meets its gates. The commands are in the skill
`.claude/skills/change-walk/SKILL.md`; this entry is the picture of it.

```mermaid
flowchart TD
    A["fetch origin; branch off the fresh origin/main.
    Gate: the ruleset refuses a push to main"] --> B
    subgraph B["Local proof"]
        B1["edit. Gate: the write-time hook denies a disallowed path"] --> B2
        B2["py -3 tools/gates.py: every gate green; pull-request gates say NOT RUN.
        A changed decision has a case and a recorded red proof. Gate: self-tests"]
    end
    B --> C["commit and push.
    Gate: the pre-push hook runs the tree gate over every commit, messages included.
    Future plans: no gate, HAZARD issue 2"]
    C --> D["open the pull request as a Draft: title without a skip literal,
    description with Blast radius when tools change. Gates: pr-title, pr-breadth"]
    D --> E["gates job on every push and on every edit of title or description.
    Red: read the table at the end of the log, fix, push once"]
    E --> F["mark Ready once, with the description final.
    The review runs: base branch's reviewer and rules, the diff as data"]
    F --> G{"review status on this head?"}
    G -- "pending: Draft, outside pull request or no run" --> F
    G -- "error: did not complete" --> H["read the run's last line:
    usage-limit: wait for the reset; credential: the owner; budget: re-run"]
    H --> F
    G -- "success" --> I["list every thread; fix each finding in its file;
    then resolve; then push once. Gate: pr-findings"]
    I --> J{"gates green and review green on the head,
    zero unresolved threads, branch up to date?"}
    J -- "no" --> E
    J -- "yes" --> K["py -3 tools/merge_pr.py PR SHA: merges exactly that head, squash.
    Gate: the ruleset"]
    J -- "red for a cause outside the change" --> N["the owner approves this pull request and head:
    merge_pr.py --over-red. Judgment step, HAZARD issue 3"]
    N --> L
    K --> L["delete the branch; the gates job runs on main"]
    X["a defect escapes anyway"] -.-> Y["rule with its gate in CLAUDE.md, knowledge entry,
    gate map updated: never a private note"]
```

## Update triggers

- `.claude/skills/change-walk/SKILL.md` changes: the whole walk.
- `.github/workflows/gates.yml` (its triggers) changes: nodes D and E.
- `.github/workflows/review.yml` (triggers, the job's `if`) changes: nodes F and G.
- `tools/merge_pr.py` changes what it refuses or waives: nodes J, K and N.
- `tools/pr_gates.py` changes a pull-request gate: nodes D and I.
