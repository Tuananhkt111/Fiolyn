# 0029 — Telegram ingest and income/expense shares stay

**Date:** 2026-11-10 · **Status:** Accepted

## Context

Two live features appear nowhere in FRS v1.1:

- Telegram ingest: `stock` commands are recorded as transactions, and everything else is filed to
  Notion.
- Income and expense shares: paired lend and borrow child rows
  ([0007](0007-shares-as-paired-lend-borrow-rows.md)).

Under the new per-account roles ([0024](0024-roles-on-account-membership-no-workspace.md)), their
fate was undecided by omission.

## Decision

Both stay, and the FRS gains them:

- **Telegram** is a second entry point (§6, interfaces). A transaction it records takes the resolved
  sender as its creator ([0027](0027-transaction-creator-backfilled-from-payer-else-admin.md)), and
  goes through the same role checks as the web API. Telegram is not a privileged path, as today.
- **Shares** are part of Module 5. Share children follow the roles of their parent's account.

Rejected: **dropping shares** (existing share rows would need a decision of their own), and
**leaving the question open**.

## Consequences

- The FRS needs a §6 entry for Telegram and a share section in Module 5.
- Answers the Medium question N-4 in `FRS/README.md`.
