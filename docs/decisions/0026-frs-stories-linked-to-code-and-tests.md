# 0026 — FRS v1.0 stories are linked to code and tests, and docs-lint keeps the links true

**Date:** 2026-11-10 · **Status:** Accepted

## Context

FRS v1.0 (`FRS_Fiolyn_v1.0.docx`, kept outside the repo) is the spec of the system as built. A
draft v1.1 was withdrawn, so v1.0 is the only FRS the code answers to. The conformance check in
`FRS/` compared all 26 backend stories with the code and found two kinds of drift. The spec
described some behaviour wrongly (list A). The code had defects the spec didn't notice (B1–B8, all
fixed in #20–#23). Neither showed up in review, because nothing tied a story to the code and tests
that carry it.

`docs/reference/*.md` already describes what the code does, at a technical level. The FRS
describes the same behaviour as stories and acceptance criteria. The two had no link, so one could
change without the other.

## Decision

- **Two sources, linked, each with its own job.** FRS v1.0 owns the stories and acceptance
  criteria. `docs/reference/` owns the technical detail. A change that alters a story's behaviour
  updates both in the same PR: the reference doc, and the story's row in `FRS/module-*.md`.
  Corrections to the spec text itself go into the next revision of the `.docx` (list A in
  `FRS/README.md`).
- **One file per backend module** in `FRS/`, with `FRS/README.md` as the index. Each story has a
  `**Code:**` line, naming the classes that implement it today, and a `**Tests:**` line, naming the
  tests that cover it. These two lines are kept current. The findings around them are dated: the
  check, plus updates when a PR fixes one.
- **Tests carry the story.** Every test on a `**Tests:**` line has
  `[Trait( "Story", "US-nn" )]`, so one story's tests run with `--filter "Story=US-nn"`.
- **docs-lint enforces the links.** It fails when a class on a `**Code:**` line is no longer
  declared under `Fiolyn/`. It also fails when a `**Tests:**` line and the tagged tests disagree, in
  either direction: a listed test that is missing or untagged, or a tagged test that isn't listed.
  A story with a `**Code:**` line must have at least one tagged test.

Rejected:
- Listing the tests only through the trait. One place is less to keep in step, but a reader of the
  FRS would see no tests without running a filter, and the check catches the duplication drifting.
- Moving the stories into `docs/reference/`. The FRS is written for people who read stories, not
  code, and its revision history belongs to the `.docx`.

## Consequences

- A test for a story's behaviour must be listed on that story's `**Tests:**` line, or docs-lint
  fails. A test that covers no story carries no trait and needs no listing.
- Renaming or removing a listed class or test fails docs-lint until the FRS line follows.
- The check is structural only. It can't tell whether a listed test really covers its story; the
  `test-reviewer` agent and the conformance rows do that.
- The FE stories (FE-US-nn) belong to the frontend repository and are out of scope here.
