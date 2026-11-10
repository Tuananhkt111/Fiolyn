# 0023 — Sessions end when the whitelist says so; logout is the caller's own

**Date:** 2026-11-10 · **Status:** Accepted

## Context

The `AllowedEmail` whitelist decides who may sign in, but it was checked only at Google login. A
refresh token lives 30 days and each refresh issued a new one, so a person removed from the
whitelist kept working until they stopped using the app. Separately, `POST /api/auth/logout`
revoked any refresh token it was given, without checking that the token belonged to the caller.
Both came out of the FRS v1.0 conformance check (`FRS/`).

## Decision

- **A refresh re-checks the whitelist.** If the user's email is no longer in `AllowedEmail`, the
  refresh is refused with 401 and nothing is issued. The client then falls back to Google login,
  which answers 403 for a non-whitelisted address.
- **A logout only revokes the caller's own token.** A token belonging to someone else gets the same
  401 as an unknown token, so the endpoint reveals nothing about tokens it doesn't own.

Rejected: revoking every token of a removed user on the spot. It needs a write path from the
whitelist service into refresh tokens. The refresh check gives the same result within one
access-token lifetime (15 minutes), with no extra moving part.

## Consequences

- Removing an address ends its sessions within 15 minutes, not 30 days. Existing access tokens stay
  valid until they expire, because they are self-contained JWTs.
- Every refresh costs one more indexed lookup on `AllowedEmail`.
- Re-adding an address restores access at the next Google login.
