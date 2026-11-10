# 0020 — Story-to-code links are written down and checked by docs-lint

**Date:** 2026-11-10 · **Status:** Withdrawn by [0023](0023-frs-v1-1-withdrawn.md) (2026-11-10)

## Context

FRS v1.1 specifies US-01 … US-07 (Modules 1 and 2). The analysis in `FRS/` names the classes that
implement each story today, but only in prose. No test and no doc carried a story ID, so nothing
showed which tests cover which story, and a rename could silently orphan a story's description of
the code.

## Decision

- Each story section in `FRS/module-*.md` opens with a `**Code:**` line listing the classes that
  implement it today.
- Tests that exercise a story's current behaviour carry `[Trait( "Story", "US-nn" )]`, so
  `dotnet test --filter "Story=US-01"` runs one story's tests.
- `scripts/docs-lint.sh` fails when a class on a `Code:` line is no longer declared under
  `Fiolyn/`, or when a story with a `Code:` line has no tagged test. The check copies an idea from
  another project: compare names in the spec to declarations in the code.

The `Code:` lines are the one part of the `FRS/` snapshot
([0019](0019-frs-analysis-committed-as-a-snapshot.md)) that is kept current.

## Consequences

- A rename or deletion of a listed class fails docs-lint, locally and in CI, until the `Code:` line
  is updated. That is the point, and also a small cost on every refactor of those classes.
- The check is by declaration name, not by namespace or file. Two classes with the same name would
  both satisfy it.
- Only stories with a `Code:` line are checked. Modules 3–7 have no stories yet, so they have none.
- Which tests carry a tag is a judgement made when tagging. The lint checks that a tag exists, not
  that the tagged tests cover the story's acceptance criteria. That is the `test-reviewer`'s job
  ([0017](0017-tests-written-and-reviewed-apart-from-the-code.md)).
