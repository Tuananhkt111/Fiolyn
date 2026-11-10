# 0016 — Changed-line coverage is reported before a PR, not gated

**Date:** 2026-11-10 · **Status:** Accepted

## Context

`coverlet.collector` was referenced by the test project but never used. Whether a change came
with tests was judged by reading the diff, and nothing pointed at a changed branch that no test
reached. A whole-project coverage percentage wouldn't help: it is dominated by code nobody touched,
and moves too little per change to show a gap.

## Decision

`scripts/coverage-changed.py [<base>]` runs the suite with `--collect "XPlat Code Coverage"` and
lists, for each production `.cs` file changed since the merge-base with `<base>` (default
`master`), the changed code lines no test ran. Generated migrations are excluded. It is adapted from
a `coverage.py` from another of the owner's projects (its diff-to-line-numbers and Cobertura-hits
functions).

It reports and exits 0. It runs before a PR, as part of the implementation loop's verify phase:
each listed line gets a test, or the PR says why it has none.

A CI threshold that fails under N% of changed lines was rejected for now. Some changed lines can't
be reached by a unit test (`Program.cs`, DI registration, EF configuration), so a threshold would
need an exclusion list before it could be trusted. There is no history yet to pick N from.

## Consequences

- A gap is only seen if someone runs the script. CI doesn't run it.
- Line coverage only: a line run by a test whose assertions don't check its effect still counts as
  covered. The `test-reviewer` agent looks at that side.
- Files that change but contain no code lines (pure records, attribute-only edits) are listed as
  not measured rather than as covered.
- Revisit as a CI gate once a few PRs show what share of changed lines is reasonably testable.
