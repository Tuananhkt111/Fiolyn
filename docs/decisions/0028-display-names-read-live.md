# 0028 — Display names are read live, never copied into rows

**Date:** 2026-11-10 · **Status:** Accepted

## Context

FRS v1.1 Appendix A.2-4 asks whether a display-name change applies to past transactions. The code
already answers it: a display name is always read through the `AppUser` navigation and is never
copied into a transaction or a membership row.

## Decision

Confirmed as the intended behaviour. A display-name change shows everywhere at once, past
transactions included. Nothing stores a snapshot of a name.

Rejected: **keeping the name used at the time**, which would need a name snapshot column on every
transaction.

## Consequences

- Nothing to build. US-07 AC5 already holds.
- Don't add a denormalised name column "for history" without superseding this record.
- Answers the Low question A.2-4 in `FRS/README.md`.
