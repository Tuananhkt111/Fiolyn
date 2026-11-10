# 0023 — Google sign-in and the email whitelist stay

**Date:** 2026-11-10 · **Status:** Accepted

## Context

FRS v1.1 US-01 and US-02 specify email-and-password self-service registration, "Remember me", a
forgot-password flow by email, and an inactivity timeout. Appendix A.2-5 left open whether to add
passwords, replace Google sign-in, or keep it.

The code signs in with a Google ID token only. An account exists only for an address an admin has
put in `AllowedEmail`. Sessions are a 15-minute access token and a 30-day refresh token, rotated on
every use, with reuse detection. The app has no email capability, and v1.1 §2.4 itself says the
system sends no email. v1.1 never asks whether dropping the whitelist is intended.

## Decision

Google sign-in stays the only credential, and the `AllowedEmail` whitelist stays: registration is
closed. US-01 and US-02 are rewritten to match:

- "Register" is the first Google sign-in of a whitelisted address.
- "Remember me" is the client keeping the refresh token.
- There is no password and no forgot-password flow.
- The refresh-token rotation and reuse detection stay as they are.

The `PASSWORD_*` and `INVALID_CREDENTIALS` rows of v1.1 §8 go with the password model.

Rejected:

- **Passwords alongside Google:** two credential paths, an account-linking rule, and an email
  provider for resets.
- **Passwords instead of Google:** every existing user would need a password set out of band, and
  the `GoogleSub` identity would be retired.
- **Open registration:** anyone with a Google account could create one. That isn't wanted for a
  family ledger.

## Consequences

- No new infrastructure. The FRS needs a revision of US-01, US-02 and §8.
- A bad token still answers 401 and a non-whitelisted address 403. v1.1's "don't reveal which part
  was wrong" rule doesn't hold for that pair, and the rewritten US-02 has to say so.
- The whitelist stays the only way to get an account. It is the natural place for an invitation to
  someone who has no account yet (US-04).
- The session-timeout question (A.2-9) stays open.
- Answers the High questions A.2-5 and N-1 in `FRS/README.md`.
