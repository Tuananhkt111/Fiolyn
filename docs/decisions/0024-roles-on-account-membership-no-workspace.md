# 0024 — Admin and Member roles on each account's membership, no workspace

**Date:** 2026-11-10 · **Status:** Accepted (not yet built)

## Context

FRS v1.1 makes a `Workspace` the sharing boundary. Every user gets one, members join by invitation,
and `WorkspaceMember` carries an Admin or Member role. Appendix A.2-8 left open how existing data
moves over.

The code has no container. Each spend account and broker account has its own membership table
(`SpendAccountMember`, `BrokerAccountMember`), and every member can do everything:
[0011](0011-membership-is-access.md) rejected roles. That per-account model is finer than a
workspace. v1.1 would need a per-wallet "shared-with" setting (`[TBD]` in its §4.1) to keep private
wallets private, and that setting would look much like the membership tables that already exist.

## Decision

There is no `Workspace` entity. Each membership row gets a role, **Admin** or **Member**:

- The user who creates an account is its Admin.
- v1.1's permission matrix (§2.2) applies per account. The Admin manages the account, its members
  and every transaction in it. A Member uses the account and edits or deletes only the transactions
  they created ([0027](0027-transaction-creator-backfilled-from-payer-else-admin.md)).
- Migration: in every existing account, the earliest member by `JoinedAt` becomes Admin and the
  others become Members.
- The role is resolved per request from the membership row, never carried in the access token
  ([0026](0026-system-administrator-stays-a-separate-tier.md)).

This supersedes [0011](0011-membership-is-access.md).

Rejected:

- **Workspaces, each account moved to its earliest member's workspace:** adds a container above
  every access check, and still needs the per-wallet "shared-with" setting.
- **One household workspace for all existing data:** the simplest migration, but the coarsest
  sharing.
- **No roles at all (0011 stands):** drops v1.1's Admin/Member matrix and A.1-3.

## Consequences

- Reads keep today's membership predicate. Writes gain a role check.
- The FRS needs a revision wherever it says "workspace": §1.3, §2.1, §4.1, US-03 (no personal
  workspace on registration), US-04 (an invitation is to an account) and US-05.
- Not decided yet: whether an Admin can hand the role over, and what happens when the last Admin
  leaves. Today the last member of an account can't be removed.
- Answers the High question A.2-8 in `FRS/README.md`.
