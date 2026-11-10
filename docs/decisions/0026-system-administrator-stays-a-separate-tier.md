# 0026 — The system administrator stays a separate tier

**Date:** 2026-11-10 · **Status:** Accepted

## Context

`AppUser.IsAdmin` is a system-wide flag, set by hand in SQL and carried in the access token as the
`isAdmin` claim. It gates every `/api/admin/*` endpoint: the email whitelist, transaction categories,
other users' admin flags, and read and delete on every user's accounts and transactions. FRS v1.1
§2.2 lists three user classes (Admin, Member, visitor). Its Admin is a per-workspace role, and the
system administrator isn't mentioned at all.

## Decision

The system administrator stays: `IsAdmin`, the `isAdmin` claim and `/api/admin/*` are kept. The
FRS gains a fourth user class in §2.2, "system administrator", separate from an account's Admin
([0024](0024-roles-on-account-membership-no-workspace.md)).

- The system administrator manages the whitelist
  ([0023](0023-google-sign-in-and-whitelist-stay.md)), categories and users.
- An account's Admin role is resolved per request from the membership row. It never goes in the
  token: a token is issued at sign-in, before any account is in play.
- The `isAdmin` claim keeps meaning the system tier only.

Rejected: **removing the system tier.** The whitelist and the categories would need new owners, and
categories would have to become per user or per account.

## Consequences

- Two different things are called "admin". The code must keep them apart by name, for example
  `IsAdmin` for the system tier and an account-role enum value for the per-account Admin.
- Becoming a system administrator still takes a manual SQL update.
- Answers the High question N-2 in `FRS/README.md`.
