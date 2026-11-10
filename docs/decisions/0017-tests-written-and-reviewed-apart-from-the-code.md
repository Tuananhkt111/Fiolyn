# 0017 — Tests for a logic change are written and reviewed apart from the code

**Date:** 2026-11-10 · **Status:** Accepted

## Context

Claude wrote the code and its tests in the same conversation. A test written with the
implementation in view tends to check what the code does rather than what it should do: an
expected value recomputed with the production formula, or a stub that returns the value the test
then asserts. Both stay green when the behaviour breaks. Changed-line coverage
([0016](0016-changed-line-coverage-reported-not-gated.md)) can't see this: a line run by such a
test counts as covered.

In another of the owner's projects, a reviewer agent limited to a few checks found 5 of 6 past test fixes in a
blind trial, at about 110k tokens a run.

## Decision

- **Writing:** for a change made by Claude, the tests come from a fresh agent that follows
  `.claude/skills/test-gen/skill.md` and gets only the requirement and the production file paths,
  not the conversation. Expected values come from the requirement.
- **Review:** the `test-reviewer` agent (`.claude/agents/test-reviewer.md`) checks the new tests on
  three points only: tautological asserts, mocks that hand back the expected value, and acceptance
  criteria with no test. It is adapted from a reviewer agent in another project, which also
  checked traceability and test level. Those two were dropped: Fiolyn has no scenario files to
  trace to.
- **When:** the review runs for logic changes only (a branch, calculation, validation, state change
  or error mapping), not for a rename, a DTO-only change or DI wiring. Running it after every
  `test-gen` was rejected on cost.
- **Acting on it:** every finding that is high, a tautology or a mock is fixed before the PR. The
  rest are listed in the PR.

## Consequences

- Each logic change costs one more agent run for the writing and one for the review.
- "Logic change" is a judgement call, so a change can skip review by being misjudged as wiring.
- The review is only as good as the requirement it gets. Without acceptance criteria, its third
  check returns "unverifiable".
- The fresh agent can still read the implementation from the file. The separation removes the
  conversation's reasoning, not the code.
