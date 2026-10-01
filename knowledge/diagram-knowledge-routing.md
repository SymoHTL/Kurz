---
name: diagram-knowledge-routing
description: Living diagram - where a fact goes in this repository (rule, knowledge entry, design record, issue, session notes, private memory), with the promotion flow, the postmortem flow and the public-repository filter
metadata:
  type: reference
---

A fact is routed by its kind, never by convenience. When two stores fit, the more shared one
wins, with one exception that is specific to this repository: it is public, so a fact that is
bound to a machine or a person, and every future plan, stays out however useful it would be.

```mermaid
flowchart TD
    F["a fact worth keeping"] --> Q0{"future plan, or bound to a machine,
    a person or another workspace?"}
    Q0 -- "yes" --> PM["private agent memory, with the reason it is local.
    Never this repository. Gates: tree gate and pre-push hook for the shapes;
    future plans: HAZARD issue 2"]
    Q0 -- "no" --> Q1{"what kind?"}
    Q1 -- "a design decision the owner made" --> DR["kurz-design.md.
    Proposed and not objected to: marked assumed. Unanswered: under Open.
    Gate: review rule design record; judgment step"]
    Q1 -- "a rule someone would otherwise break" --> CL["CLAUDE.md, ending in its gate,
    a HAZARD with its issue, or judgment step.
    Gate: review rule rules for sessions"]
    Q1 -- "a lesson, trap, recipe or postmortem" --> KN["one file in knowledge/ plus its INDEX.md line,
    same pull request. Gate: knowledge lint"]
    Q1 -- "open work, status, a question for later" --> IS["an issue. Never repository markdown.
    Gate: review rule rules for sessions"]
    Q1 -- "this session's task, decisions, next step" --> NO["the session notes file, outside the repository.
    No gate: machine-level instruction"]
    Q1 -- "a long procedure" --> SK["a skill under .claude/skills/. Judgment step"]
    PMx["a lesson found only in private memory at wrap-up"] --> PR["promote: write the knowledge entry,
    delete the private copy. HAZARD issue 6"]
    PR --> KN
    ES["a defect escaped"] --> PO["postmortem: rule with gate, knowledge entry,
    gate map update, in one pull request"]
    PO --> CL
    PO --> KN
    KN --> ST{"entry names a file, tool or flag?"}
    ST -- "yes" --> VF["verify it still exists before acting on it:
    entries are point-in-time. Judgment step"]
```

## Update triggers

- The "Knowledge" rules or the "A public repository" rules in `CLAUDE.md` change: the whole tree.
- `tools/lint_knowledge.py` changes what it checks: the node KN.
- `tools/tree_gate.py` changes its patterns: the node PM.
- A new store appears (a directory, a tracker label used as a store): a new branch under Q1.
