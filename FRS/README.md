# FRS v1.0 — conformance with the code

How far the backend still matches `FRS_Fiolyn_v1.0.docx`, the spec of the system as built.

| | |
|---|---|
| **Spec** | `FRS_Fiolyn_v1.0.docx`, Rev 2, 2026-09-16 — "Baseline — documents the system as built" |
| **Code** | `master` at `4aa72f7` (2026-11-10) |
| **Scope** | Backend stories US-01 … US-26 (Part I, Modules 1–9). The frontend stories FE-US-01 … FE-US-25 belong to the FiolynWeb repository and are out of scope here. |
| **Method** | Each acceptance criterion and business rule was checked against the C# code and tests, with `path:line` evidence. Authority: code and `docs/reference/openapi.json`, then `docs/rules/`, then `docs/reference/`. Claims marked *inferred* were read from code but not run. |

## The headline

**No behaviour has drifted since v1.0 was written.** Since 2026-09-16 the production code has changed
only by a DTO move and a Swashbuckle version bump. Every difference below already existed on that
date, so in each case it is **v1.0 that describes the system wrongly**, or the code that has a
defect v1.0 didn't notice.

## Verdicts

| Module | Stories | Verdict |
|---|---|---|
| [1 · Authentication & Access](module-1-authentication.md) | US-01 – US-04 | All match |
| [2 · Users & Profile](module-2-users-profile.md) | US-05 – US-08 | US-06 partly (email isn't editable); US-07 differs (unlink returns 200, not 204) |
| [3 · Spend Accounts](module-3-spend-accounts.md) | US-09 – US-11 | US-10 differs (members are added by email; the last member can't be removed) |
| [4 · Broker Accounts](module-4-broker-accounts.md) | US-12 – US-14 | All match (US-13 adds by email too) |
| [5 · Transaction Categories](module-5-categories.md) | US-15 – US-16 | All match |
| [6 · Transactions](module-6-transactions.md) | US-17 – US-21 | US-19 partly (transfer edit and delete need only one side's membership) |
| [7 · Counterparties & Obligations](module-7-counterparties-obligations.md) | US-22 – US-23 | All match |
| [8 · Income/Expense Shares](module-8-shares.md) | US-24 | Matches, with small undocumented edges |
| [9 · Telegram Ingest & Notion](module-9-telegram-ingest.md) | US-25 – US-26 | US-26 differs (per-share dividends; `broker:` is required) |

## A · Where v1.0 is wrong (candidates for v1.0 Rev 3)

The code is right and the spec should change.

- **US-06:** `PATCH /api/users/me` edits the display name only. Email is the Google identity and can't be edited. The Profile Fields table and the §8 "email already used" case don't apply.
- **US-07:** unlinking a Telegram ID returns 200 with the profile, not 204.
- **US-10, US-13:** members are added by email, not user id. Adding an existing member is a no-op. The last member of an account can't be removed (400).
- **US-02:** a replayed refresh token revokes all of the user's active refresh tokens, not only one chain.
- **US-21:** the admin endpoints return the same responses as the user endpoints. There is no extra owner or member detail.
- **US-26:** `stock div {symbol} {qty} {price}` records a per-share dividend (total = qty × price). `broker:` is required, not optional.
- **US-24:**
  - at most 20 shares per request;
  - without a parent description, a child's description is `Shared with {name}`, with no colon;
  - the percentage is a `double`, not a 2-place decimal.
- **Appendix A.1 preamble:** it points to FRS v1.1 for target designs. v1.1 was a draft that has been withdrawn, so this sentence should go.

## B · Possible code defects (your call: fix the code, or accept and document)

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | A member of only the destination account can edit or delete a transfer. Deleting it reverses the balance of a source account they aren't a member of. This contradicts v1.0 and `docs/reference/transactions.md` ("membership of **both**"). | High | Verified in code (`TransferTransactionService.cs:122-127`) |
| 2 | `StockTransactionRequest` has no `[Range]` on `Quantity`, `Price` or `TotalAmount`, so a Buy with a negative total credits the account. This breaks the rule in `docs/rules/transactions.md` that request DTOs carry a positive magnitude. | High | Verified in code |
| 3 | The unique indexes on whitelist emails, user email and Google id, counterparty names and category names also cover soft-deleted rows, while the services' pre-checks skip them. Re-adding a removed email, contact or category gives 409. Lend, borrow or shares naming a deleted contact, or a deleted user signing in again, probably gives 500. | Medium | Counterparty index verified; the effects are *inferred* |
| 4 | Refreshing a token doesn't re-check the whitelist, so a user removed from it keeps access for up to 30 days. | Medium | *Inferred* |
| 5 | Logout doesn't check that the refresh token belongs to the caller. | Low | *Inferred* |
| 6 | `SpendAccountService.GetMembersAsync` checks membership in a separate query, against `docs/rules/access-control.md`. | Low | Reported |
| 7 | A share percentage is range-checked by the DTO before the service rounds it, so 0.005 or 100.004 is rejected instead of rounded. | Low | Reported |

The 500-instead-of-409 on a concurrent duplicate contact (found by the CounterpartyResolver tests) is the same family as #3.

## C · Untested criteria

- No test covers a 401 or 403 path: the admin policies and `[Authorize]`.
- The Expense sign, delete-reversal outside shares, a stock symbol's case through the API, the stock category check, a transfer with `Amount < 1`, and obligations for a counterparty with both a lend and a borrow (US-23 AC2) have no tests.
- Nothing races two requests against a real database; the concurrency tests simulate the exception.

## v1.0 Appendix A — open questions today

| # | Question | Today |
|---|---|---|
| 1 | Does a display name edited via `PATCH /me` survive the next Google login? | **The code answers it: no.** Google overwrites it on every login (`AuthService.cs:56`). |
| 2 | Can a member leave a shared account? | Resolved in v1.0, and the code agrees: yes, unless they are the last member. |
| 3 | Per-user custom categories? | Open. Categories are global. |
| 4 | Admin view of obligations? | Open. There is no admin endpoint. |
| 5 | Verify §4's data types against the code | Partly answered: every id and foreign key is `long`; the nullability is listed in the module files. |
| 6 | Document the remaining endpoints to field level? | Open. |
| 7 | A stable error-code catalogue? | Open. |
| 8 | Real priorities and preconditions per story? | Open. The code can't answer it. |
| 9 | Non-functional targets? | Open. |

## Files

One file per backend module, linked above. Each story lists the classes that implement it today
(`**Code:**`) and the tests that cover it (`**Tests:**`).
