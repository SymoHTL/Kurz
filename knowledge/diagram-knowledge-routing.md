---
name: diagram-knowledge-routing
description: Living diagram - where a fact goes in this repository (rule, knowledge entry, design record, issue, session notes, private memory), with the promotion flow, the postmortem flow and the public-repository filter
metadata:
  type: reference
---

A fact is routed by its kind, never by convenience. When two stores fit, the more shared one
wins, with one exception that is specific to this repository: it is public, so a fact that is
bound to a machine or a person, and every future plan, stays out however useful it would be.
"Review rule" in a node means a section of `.review/review-rules.yaml`: a model reading one batch
of a diff (node R5 of [[diagram-gate-map]]).

```mermaid
flowchart TD
    F["a fact worth keeping"] --> Q0{"future plan, or bound to a machine,
    a person or another workspace?"}
    Q0 -- "yes" --> PM["private agent memory, with the reason it is local.
    Never this repository. Gates: tree gate and pre-push hook for the shapes;
    future plans: HAZARD issue 2"]
    Q0 -- "no" --> Q1{"what kind?"}
    Q1 -- "a statement about the design" --> DR["kurz-design.md.
    Chosen by the owner: decided. Proposed and not objected to: marked assumed. Unanswered: under Open.
    Which of the three it is: judgment step. The shape of the record: review rule design record"]
    Q1 -- "a rule someone would otherwise break" --> CL["CLAUDE.md, ending in its gate,
    a HAZARD with its issue, or judgment step.
    Gate: review rule rules for sessions"]
    Q1 -- "a lesson, trap, recipe or postmortem" --> KN["one file in knowledge/ plus its INDEX.md line,
    same pull request. Gate: knowledge"]
    Q1 -- "open work, status, a question for later that is not a design question (a design question goes under Open in the record, DR,
    and a design round asks it; an issue only points there)" --> IS["an issue. Never repository markdown.
    Gate: review rule rules for sessions"]
    Q1 -- "this session's task, decisions, next step" --> NO["the session notes file, outside the repository.
    Judgment step"]
    Q1 -- "a long procedure" --> SK["a skill under .claude/skills/. Judgment step"]
    PMx["a lesson found only in private memory at wrap-up"] --> Q2{"does it hold a reason to stay local:
    a plan, a machine, a person, another workspace?"}
    Q2 -- "yes: it stays, or only the part without that reason is promoted" --> PM
    Q2 -- "no" --> PR["promote: write the knowledge entry,
    delete the private copy. HAZARD issue 6"]
    PR --> KN
    ES["a defect escaped"] --> PO["postmortem: rule with gate, knowledge entry,
    gate map update, in one pull request.
    Gate: review rule every pull request, for a lesson the description names and no file records"]
    PO --> CL
    PO --> KN
    KN --> ST{"entry names a file, tool or flag?"}
    ST -- "yes" --> VF["verify it still exists before acting on it:
    entries are point-in-time. Judgment step"]
```

## Update triggers

- The "Knowledge" rules or the "A public repository" rules in `CLAUDE.md` change: the whole tree.
- `tools/lint_knowledge.py` changes what it checks: the node KN.
- `tools/tree_gate.py`, `tools/kit.py` (its patterns) or `.githooks/pre-push` changes what it
  refuses: the node PM.
- `.review/review-rules.yaml` changes the sections "design record", "rules for sessions" or
  "every pull request": the nodes DR, CL, IS and PO.
- A HAZARD issue that a node names (2, 6) closes or opens: that node.
- A new store appears (a directory, a tracker label used as a store): a new branch under Q1.
