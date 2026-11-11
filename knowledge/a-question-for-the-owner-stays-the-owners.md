---
name: a-question-for-the-owner-stays-the-owners
description: On 2026-11-11 a session took two picks that were the owner's. It took a default that the owner's answer P1, which section 12 cites, contradicts (a chain of Arg calls copying its list). It answered two questions the questionnaire had given to the owner as "conventions" (the text of thirteen names that are no value). Before a default is taken, every owner comment the record's clause cites is read; a question given to the owner stays the owner's until the owner answers it
metadata:
  type: feedback
---

## What happened

Before a change that a person sees, a session answers a fixed questionnaire and posts it on the
issue as the comment "Product pass", as the instructions sessions follow here ask (they are kept
outside this repository; issue 174, comment 6784467506, is one such pass). Each open question in it
is tagged with whose it is: one the session may take at its default, or one only the owner answers.
On 2026-11-11 two picks of the owner's were taken by the session:

- **A default that a cited answer contradicts.** In the change of `Command` (issue 174), the
  questionnaire gave its question 2 to the session at a default: a chain of `Arg` calls copies its
  list. The session took the copy. Section 12 cites the owner's answer P1 (issue 174, comment
  6560478370), which says "The build is held to that, with no copy in a chain, and a test shows it".
  The questionnaire had read the record's text and not the comment the text cites. The build,
  which had reached no pull request, was turned around: the members append through a `mut` list,
  and a chain takes over its temporary (issue 174, comment 6785704771, which corrected the pass's
  question 2 and names the build's test: a chain and `cmd = cmd.Arg(x)` run under a runtime whose
  every copy of a list fails).
- **A question for the owner answered by the session.** In the follow-up of pull request 451, the
  questionnaire (issue 174, comment 6784467506) gave two questions to the owner: the text of
  thirteen names that are no value (its question 1), and that text's scope (question 3). The
  session answered both itself, as "conventions of the compiler's texts, not picks of the language"
  (comment 6784467838), and never put them to the owner. The acceptance judge of pull request 461,
  in its row "A decided statement cites the owner", asked whether that text stood, since it rested
  on the session's comments and not on the owner's choice (comment 6792226339). The owner then
  answered at once, "As built, 13 names" (comment
  6792215957).

## How to apply

- Before a default is taken, open and read every owner comment that the record's clause on the
  topic cites. A default that one of those comments contradicts is no default: build what the
  answer says, and name the questionnaire's miss on the issue.
- A question the questionnaire gave to the owner stays the owner's, whatever the session thinks of
  it, and only the owner's word takes it back. Where an earlier answer of the owner's already
  settles it, the comment on the issue cites that answer by its id. While the owner is away, the
  step may go on with the questionnaire's default, marked *(assumed)* where it reaches the record and
  never recorded as decided, and the question itself still goes to the owner with the next
  questions.

Gate: `judgment step`. The questionnaire reads the record's text and not the comments the text
cites. The acceptance judge's row "A decided statement cites the owner", one of the questions the
judge answers on every change (as on pull request 461, comment 6792226339), catches the second
shape, but only after the build.
