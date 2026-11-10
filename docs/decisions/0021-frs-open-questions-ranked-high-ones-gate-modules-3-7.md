# 0021 — FRS open questions are ranked, and the high ones gate Modules 3–7

**Date:** 2026-11-10 · **Status:** Withdrawn by [0023](0023-frs-v1-1-withdrawn.md) (2026-11-10)

## Context

FRS v1.1 Appendix A.2 lists seven open questions as a flat, unranked log, each marked "Pending".
The conformance analysis in `FRS/` ([0019](0019-frs-analysis-committed-as-a-snapshot.md)) found four
more decisions that v1.1 never asks: whether the email whitelist survives, what becomes of the
system-wide admin, what the creator of an existing transaction is, and whether Telegram ingest and
shares survive. Modules 3–7 are still to be specified, and several of these questions decide
their data model. Specifying those modules first would mean writing on top of undecided ground.

## Decision

The open questions are kept, ranked, in `FRS/README.md` ("Open questions, ranked"). Each one gets:

- a **kind**: A information missing, B two readings, C the rule is unknown, D not understood. The
  scheme is the one the owner uses for open questions in another project;
- an **impact**: High (blocks a module or changes the data model), Medium (shapes the contract of
  several stories), Low (local, or already answered by the code).

**Every High question is answered by the product owner, and recorded as an ADR, before Modules
3–7 are specified.** Medium and Low questions can be answered as the work reaches them.

## Consequences

- At the time of writing, six questions are High: the credential model (A.2-5), the whitelist,
  the workspace migration (A.2-8), wallet versus broker account (A.2-10), the system admin, and the
  transaction creator. Module 3–7 specification waits on all six.
- The analysis proposes an answer where the code already settles one (A.2-4, display names), but
  each answer is still the owner's to confirm.
- The ranking is the one living part of `FRS/` besides the `Code:` lines. When a question is
  answered, its row says so and links the ADR; the row is not deleted.
