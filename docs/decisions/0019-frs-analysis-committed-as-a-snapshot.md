# 0019 — The FRS v1.1 analysis is committed as a dated snapshot

**Date:** 2026-11-10 · **Status:** Withdrawn by [0023](0023-frs-v1-1-withdrawn.md) (2026-11-10)

## Context

On 2026-09-17 a conformance analysis of `FRS_Fiolyn_v1.1.docx` against the backend at `6c3c953`
was written to `FRS/`: one file per module, a cross-cutting file and an index. It was staged and
never committed. By 2026-11-09 the files were gone from the working tree and the index. They were
recovered from git's unreachable blobs, taking the last version of each file.

Uncommitted, the analysis can be lost again. Committed as if it were current, it would become
the kind of document `docs/` was cleaned of: a pseudo-spec that drifts from the code
(`.claude/CLAUDE.md`, "Implementing a spec").

## Decision

`FRS/` is committed as a **dated snapshot**: its index says it is true at `6c3c953` and is not
kept in step with the code. It stays outside `docs/` and outside the four layers, like `planning/`
([`docs/README.md`](../README.md#not-documentation)). Nothing in `docs/` treats it as a source of
truth.

Dropping it was rejected. It holds the only story-by-story reading of US-01 … US-07 against the
code, and the open questions that block Modules 3–7. Rewriting it later would cost more than
keeping it.

## Consequences

- A reader can be misled by a stale count in `FRS/`. The snapshot banner is the only protection.
- When a decision in `FRS/` is taken, it becomes an ADR. When it is built, it goes in
  `docs/reference/`. `FRS/` is not edited to match.
- The parts that will be kept current (story-to-code links, the ranking of open questions) are
  marked as such where they are added.
