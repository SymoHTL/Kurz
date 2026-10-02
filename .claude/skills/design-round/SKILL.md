---
name: design-round
description: >-
  Run a round of Kurz design questions with Simon and record the answers: numbered questions with
  options, costs and a lean; what counts as decided, assumed or open; how the record reaches main.
  Use whenever a design choice comes up, when Simon answers a round, or when the reference
  uncovers a blank.
---

# A design round

## 1. Ask

1. Read the sections of `kurz-design.md` that the topic touches, section "Open", and the rules of
   `reference/` on the topic that are marked `open` or `proposed`. A question about one of them
   names its id, so that Simon can answer "C8 c".
2. Write numbered questions. Each one has: the question in one line; two to four options; what
   each option costs (at run time, in the compiler, for the developer); a stated lean with its
   reason. Simon answers by number.
3. Explain a new concept through its C# equivalent.
4. A wish that cannot hold - a cost assumed away, a guarantee no system can give - is contradicted
   in the reply. It is not recorded as decided, however clearly it was asked for.
5. Small defaults that follow from "in the spirit of C#" are not questions. State them as
   assumptions in one line and move on; a real fork is a question.

## 2. Record

1. What Simon chose goes into `kurz-design.md` as decided, in the section it belongs to, with the
   date when the choice replaces an earlier one.
2. What was proposed and not objected to is marked *(assumed)*, with a few words on why it is
   only assumed.
3. What nobody answered goes under "Open" in the same change, not only into chat. An answered
   question leaves "Open" in the same change.
4. An ambiguous answer is recorded as assumed, with the reading that was taken, and the reading
   is said back to Simon.
5. Before writing, walk `knowledge/diagram-design-change-playbook.md`: status of every statement,
   guarantees with mechanism and cost, samples read against the rules beside them, numbers from
   their source.
6. Future plans are not design. What gets built when stays out of the record, the pull request
   and the commit message.
7. Then the reference, in the same pull request: each rule the round answered gets its new status
   and the section it now cites; a rule that became `decided` gets its cases (a `.kz` file under
   `corpus/`, a `Case:` line under the rule, then `py -3 tools/lint_reference.py --sync`), and a
   new compile error gets its row in `reference/12-errors.md`. A `proposed` rule Simon did not
   object to stays `proposed` until the record says it, as *(assumed)* or decided.

## 3. Land

The record takes the walk in the skill `change-walk`: branch, gates, Draft, review, merge with the
tool. One round is one pull request. The next round's questions can go to Simon while that pull
request is in review.
