---
name: design-round
description: >-
  Run a round of Kurz design questions with the owner and record the answers: numbered questions
  with options, costs and a lean; what counts as decided, assumed or open; how the record reaches
  main. Use whenever a design choice comes up, when the owner answers a round, or when the
  reference uncovers a blank.
---

# A design round

Where a step names the review rule "design record", that is a section of
`.review/review-rules.yaml`: the reviewer reads the diff and the description, not the
conversation. What was asked and answered in chat can only be a judgment step.

## 1. Ask

1. Read the sections of `kurz-design.md` that the topic touches, and section "Open".
   `judgment step`
2. Write numbered questions. Each one has: the question in one line; two to four options; what
   each option costs (at run time, in the compiler, for the developer); a stated lean with its
   reason. The owner answers by number. `judgment step`
3. Kurz is designed in the spirit of C#: explain a new concept through its C# equivalent.
   `judgment step`
4. A wish that cannot hold - a cost assumed away, a guarantee no system can give - is contradicted
   in the reply. It is not recorded as decided, however clearly it was asked for.
   Gate: review rule "design record" for a wish that reached the record; the reply itself is a
   `judgment step`.
5. Small defaults that follow from "in the spirit of C#" are not questions. State them as
   assumptions in one line and move on; a real fork is a question. `judgment step`

## 2. Record

1. What the owner chose goes into `kurz-design.md` as decided, in the section it belongs to, with
   the date when the choice replaces an earlier one. That the owner chose it: `judgment step`.
2. What was proposed and not objected to is marked *(assumed)*, with a few words on why it is
   only assumed. Gate: review rule "design record".
3. What nobody answered goes under "Open" in the same change, not only into chat. An answered
   question leaves "Open" in the same change. Gate: review rule "design record", for what the
   diff shows; reading the untouched part of "Open" is a `judgment step`.
4. An ambiguous answer is recorded as assumed, with the reading that was taken, and the reading
   is said back to the owner. `judgment step`
5. Before writing, walk `knowledge/diagram-design-change-playbook.md`: status of every statement,
   guarantees with mechanism and cost, samples read against the rules beside them, numbers from
   their source. Each of its nodes names its own gate.
6. Future plans are not design. What gets built when stays out of the record, the pull request,
   the branch name and the commit message. HAZARD (#2).

## 3. Land

1. The record takes the walk in the skill `change-walk`: branch, gates, Draft, review, merge with
   the tool. One round is one pull request. Gate: the ruleset.
2. The description traces every item recorded as decided to the owner's choice: the question's
   number or name, and the answer that was given. An item without that trace is a finding.
   Gate: review rule "design record".
3. The next round's questions can go to the owner while that pull request is in review.
   `judgment step`
