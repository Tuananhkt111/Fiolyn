# Architecture decisions

Why Fiolyn is built the way it is. Each record states the context, the decision, and what it costs.

**These records are historical.** An ADR describes what was decided as of its date. It is never
authoritative on current behaviour — for that, see [`../reference/`](../reference/) and
[`rules/`](../rules/). If an ADR disagrees with those, the ADR is the stale one.

**They are also immutable.** Don't edit a merged ADR to reflect new thinking. Write a new one and
mark the old one superseded. The pre-commit hook enforces this: in a merged ADR, only the
`**Status:**` line may change ([0014](0014-versioned-git-hooks.md)).

| # | Decision | Date |
|---|---|---|
| [0001](0001-money-as-long-not-decimal.md) | Money and quantities are `long`, not `decimal` | 2026-04-23 |
| [0002](0002-neon-and-northflank-hosting.md) | Neon over Supabase, Northflank over Render | 2026-04-01 |
| [0003](0003-no-https-redirection.md) | No `UseHttpsRedirection` behind the Northflank proxy | 2026-04-01 |
| [0004](0004-migrations-never-run-on-startup.md) | Migrations never run on application startup | 2026-05-03 |
| [0005](0005-squashed-migration-history.md) | Migration history squashed to a single `InitialCreate` | 2026-05-10 |
| [0006](0006-tpt-transaction-hierarchy.md) | One ledger: TPT hierarchy with seven leaves | 2026-05-03 |
| [0007](0007-shares-as-paired-lend-borrow-rows.md) | Income/expense shares are auto-paired Lend/Borrow rows | 2026-05-07 |
| [0008](0008-counterparty-entity-over-free-text.md) | `Counterparty` entity replaces the free-text name | 2026-05-07 |
| [0009](0009-offset-pagination.md) | Offset pagination, uniform across every list endpoint | 2026-05-10 |
| [0010](0010-broker-spend-account-fk-restrict.md) | `BrokerAccount → SpendAccount` FK is `Restrict` | 2026-04-26 |
| [0011](0011-membership-is-access.md) | Membership is access; no owner/editor/viewer roles | 2026-04-23 |
| [0012](0012-rest-conventions-and-non-goals.md) | RFC 7807 errors — and the REST features deliberately skipped | 2026-04-16 |
| [0013](0013-deploy-gated-on-tests-and-docs-lint.md) | Deploy only after tests and docs-lint pass | 2026-11-09 |
| [0014](0014-versioned-git-hooks.md) | Git hooks live in the repo, under `scripts/hooks/` | 2026-11-09 |
| [0015](0015-claude-code-guards.md) | Claude Code guards in one Python dispatcher, failing open | 2026-11-09 |
| [0016](0016-changed-line-coverage-reported-not-gated.md) | Changed-line coverage is reported before a PR, not gated | 2026-11-10 |
| [0017](0017-tests-written-and-reviewed-apart-from-the-code.md) | Tests for a logic change are written and reviewed apart from the code | 2026-11-10 |
| [0018](0018-pinned-skills-are-owner-typed.md) | Skills whose model pin matters are typed by the owner only | 2026-11-10 |
| [0022](0022-one-deploy-at-a-time-newest-push-wins.md) | One deploy at a time: the newest push to `master` wins | 2026-11-10 |
| [0023](0023-sessions-follow-the-whitelist.md) | Sessions end when the whitelist says so; logout is the caller's own | 2026-11-10 |
| [0024](0024-recreating-a-deleted-row-restores-it.md) | Re-creating a soft-deleted row restores it; a deleted user stays out | 2026-11-10 |

## Writing a new one

Copy the shape of any existing record:

```markdown
# NNNN — Short imperative title

**Date:** YYYY-MM-DD · **Status:** Accepted

## Context
What was true that forced a choice.

## Decision
What was chosen, stated plainly.

## Consequences
What this costs, and what it rules out.
```

Dates come from the commit that made the decision, not the day the record was written.
