# 0023 — FRS v1.1 is withdrawn, with everything built on it

**Date:** 2026-11-10 · **Status:** Accepted

## Context

`FRS_Fiolyn_v1.1.docx` was the target-stage spec. Three records built on it:

- [0019](0019-frs-analysis-committed-as-a-snapshot.md): its conformance analysis, committed under
  `FRS/`.
- [0020](0020-story-to-code-links-checked-by-docs-lint.md): `**Code:**` lines for its stories
  US-01 … US-07, `[Trait( "Story", "US-nn" )]` on tests, and a docs-lint section checking both.
- [0021](0021-frs-open-questions-ranked-high-ones-gate-modules-3-7.md): its open questions,
  ranked, with a gate on Modules 3–7.

The product owner found v1.1 incorrect and is rewriting the target spec from scratch. The answers
recorded against its open questions were not merged, and a v1.2 draft built on it was discarded.

## Decision

FRS v1.1 is treated as if it never existed. 0019, 0020 and 0021 are withdrawn, and what they
introduced is removed:

- the `FRS/` folder;
- the story traits on tests;
- the story-code-links section of `scripts/docs-lint.sh`;
- the related lines in `docs/README.md`, `docs/rules/testing.md` and `.claude/CLAUDE.md`.

The three records stay in the log with their Status set to withdrawn, because a merged ADR is never
deleted.

## Consequences

- There is no target-stage spec in or alongside the repo until the owner provides the rewrite.
  The as-built behaviour is still described by `docs/reference/`.
- Linking stories to code and tests, or ranking open questions, can return with the new spec. If
  it does, it gets a new ADR rather than reviving these.
- ADRs 0001–0018 and 0022 are unaffected: none of them depends on v1.1.
