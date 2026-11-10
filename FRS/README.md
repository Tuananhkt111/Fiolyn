# FRS v1.1 — conformance analysis

What `FRS_Fiolyn_v1.1.docx` asks for, what the system does today, and what the difference costs.

| | |
|---|---|
| **Spec analysed** | `FRS_Fiolyn_v1.1.docx` — "Family Shared Workspace Edition — target stage", Rev 2, dated 2026-09-16 |
| **Code analysed** | `Fiolyn/` backend at commit `6c3c953` |
| **Docs analysed** | `docs/` — rules, reference, decisions, guides |
| **Analysis date** | 2026-09-17 |
| **Not analysed** | The FiolynWeb SPA. It is a separate repository; every screen-level and redirect-level acceptance criterion in v1.1 is unverifiable from here and is marked *frontend*. |

This is a dated snapshot, true at `6c3c953` (2026-09-17) and committed as one
([ADR 0019](../docs/decisions/0019-frs-analysis-committed-as-a-snapshot.md)). It is not updated as the
code moves on: counts and endpoint inventories below are true at that commit and nowhere else.

**Two exceptions are kept current.** The `**Code:**` line under each story names the classes that
implement it today; tests for it carry `[Trait( "Story", "US-nn" )]`, and docs-lint fails when a
listed class is gone or a story has no tagged test
([ADR 0020](../docs/decisions/0020-story-to-code-links-checked-by-docs-lint.md)). And the
[open questions, ranked](#open-questions-ranked), are updated as each one is answered.

---

## Read this first — what v1.1 actually contains

v1.1 §3.1 marks Modules 3–7 as `⏳ Pending`, and §1.2 states they "will be specified in later
revisions". The document body carries **US-01 … US-07 only** — Modules 1 and 2. There is no third
story-bearing section; the raw document XML contains no other `US-` identifier.

So the comparison runs at two different resolutions:

- **Modules 1–2** — story by story, acceptance criterion by acceptance criterion.
- **Modules 3–7** — capability level, against everything v1.1 *does* constrain about them: the
  module table (§3.1), the constraints and assumptions (§2.4, §2.5), the data model (§4.1) and the
  open questions (Appendix A). Those sections say real things about Modules 3–7 — enough to find
  four capability gaps and one naming collision — but not enough to test an acceptance criterion
  against.

If a story-level revision of Modules 3–7 exists elsewhere, it was not in the file supplied and this
analysis does not cover it.

---

## Verdicts

| Verdict | Meaning |
|---|---|
| **Met** | The behaviour exists and matches what v1.1 asks. |
| **Partial** | Some criteria are met; the shape differs. |
| **Absent** | Nothing in the codebase addresses it. Greenfield. |
| **Conflict** | The codebase deliberately does something else, recorded in an ADR or a rule. Adopting v1.1 means *superseding a decision*, not filling a gap. |

`Conflict` is the load-bearing distinction. An `Absent` item is work. A `Conflict` item is a decision
that has to be re-taken by a person before the work can be scoped.

---

## Summary

### Module 1 — Authentication & Onboarding → [`module-1-auth-onboarding.md`](module-1-auth-onboarding.md)

| Story | Verdict | One line |
|---|---|---|
| US-01 Register a new account | **Conflict** | No registration exists. An `AppUser` is created implicitly on first Google login, and only if an admin has already whitelisted the email. |
| US-02 Log in | **Conflict** | Google ID token in, Fiolyn JWT out. No password, no "Remember me", no forgot-password, no inactivity timeout. |
| US-03 Mandatory onboarding | **Absent** | No onboarding gate. A user with no accounts sees empty lists. The wallet-create endpoint itself already accepts name, type and an initial balance. |
| US-04 Invite a member | **Absent** | No invite, no accept/decline, no expiry, no notification. Members are added unilaterally and immediately, by email, by any existing member. |
| US-05 Admin / Member permissions | **Conflict** | Membership *is* access — no roles, every member equal ([ADR 0011](../docs/decisions/0011-membership-is-access.md)). Transactions carry no creator column, so creator-only editing has nothing to check. |

### Module 2 — Profile → [`module-2-profile.md`](module-2-profile.md)

| Story | Verdict | One line |
|---|---|---|
| US-06 View profile | **Partial** | `GET /api/users/me` exists. Returns 8 fields where v1.1 specifies 5; has no `Name`; `Role` is a system-wide boolean, not a workspace role. |
| US-07 Edit profile | **Partial** | `PATCH /api/users/me` edits **display name only**. Tele ID lives on two separate routes. Email is not editable at all — it is the SSO identity. |

### Modules 3–7 — one file each

v1.1 specifies no stories for these, so each file compares at capability level. See
[the scope note](#read-this-first--what-v11-actually-contains).

| Module | Verdict | One line |
|---|---|---|
| [3 · Spend Account](module-3-spend-account.md) | **Partial** | Substantially built — two entities (`SpendAccount`, `BrokerAccount`), sharing, adjustment. Blocked on the wallet-type question (Appendix A.2-10). |
| [4 · Category](module-4-category.md) | **Partial** | Built as a global admin-managed lookup bound to one kind. "Custom" categories — per user or per workspace — do not exist. |
| [5 · Transaction](module-5-transaction.md) | **Partial** | Seven kinds including transfer, with atomic balance writes. **Recurring transactions do not exist** and there is no scheduler of any kind. |
| [6 · Obligation](module-6-obligation.md) | **Met** *(capability)* | Counterparty contact list plus a receivable/payable/net rollup at `GET /api/obligations`. |
| [7 · Report / Dashboard](module-7-report-dashboard.md) | **Absent** | No aggregate or reporting endpoint. `GET /api/transactions` is the raw material. |

### Cross-cutting → [`cross-cutting.md`](cross-cutting.md)

Nine structural differences that are not owned by any single module, including the two that gate
everything else.

---

## The five findings that decide the plan

**1 · The authentication question blocks Module 1 entirely, and v1.1 already knows it.**
Appendix A.2-5 is marked "⏳ Pending — blocks Module 1". It is correct. US-01 and US-02 describe an
email/password system; the code has no password column, no password hashing, no email-sending
capability and no SMTP configuration key. Until this is answered, US-01, US-02 and the editable-email
half of US-07 cannot be scoped. See [cross-cutting §3](cross-cutting.md#3--authentication-two-incompatible-models).

**2 · The whitelist is a closed-registration policy that v1.1 silently deletes.**
Today a new person cannot get an account unless an admin adds their address to `AllowedEmail` first
(`AuthService.LoginWithGoogleAsync`, 403 otherwise). US-01 describes open self-service registration.
Appendix A mentions the whitelist only in passing, inside A.2-5, and never asks whether dropping it
is intended. It is a product decision, not an implementation detail, and it is currently
unrecorded. See [cross-cutting §4](cross-cutting.md#4--the-whitelist-is-a-closed-registration-policy-v11-does-not-mention).

**3 · "Admin" means two different things, and v1.1 only defines one of them.**
The code has `AppUser.IsAdmin` — a *system-wide* flag, bootstrapped by hand with SQL, gating roughly
forty `/api/admin/*` endpoints that manage the whitelist, categories, users and every other user's
accounts and transactions. v1.1's Admin is *per-workspace*: the creator of a workspace. v1.1 §2.2
lists three user classes and the system administrator is not among them, so an entire implemented
permission tier is unaccounted for in the target design. See
[cross-cutting §2](cross-cutting.md#2--two-different-admins).

**4 · Creator-only transaction editing needs a column that does not exist.**
US-05 AC6–AC8 turn on who created a transaction. `TransactionBase` has no creator column. `PayerId`
looks like one and is not: it is nullable, it is *attribution* of who paid, it is `SetNull` when a
user is deleted, and a user can freely set it to someone else. Implementing US-05 means a new
non-nullable `CreatorId`, a backfill decision for existing rows, and a permission check on every
write path — seven leaf services, the share-children path and the Telegram ingest. See
[cross-cutting §5](cross-cutting.md#5--transaction-ownership-has-no-column-to-stand-on).

**5 · Two shipped features and the whole admin surface fall outside v1.1's seven modules.**
Telegram ingest with Notion filing (one of the two documented front doors) and income/expense shares
(paired lend/borrow children, [ADR 0007](../docs/decisions/0007-shares-as-paired-lend-borrow-rows.md))
are both live, both tested, and neither appears anywhere in v1.1 — not in the module table, not in
the data model, not in the open questions. Their fate under the workspace model is undecided by
omission rather than by choice. See [cross-cutting §9](cross-cutting.md#9--implemented-capabilities-v11-does-not-mention).

---

## Open questions, ranked

Kept current, unlike the rest of this snapshot. Appendix A.2 lists seven open questions. The analysis
found four more that v1.1 doesn't ask (`N-1` … `N-4`). Each has a kind and an impact:

- **Kind:** **A** information missing · **B** two readings · **C** the rule is unknown (a decision
  nobody has taken) · **D** not understood.
- **Impact:** **High** blocks a module or changes the data model · **Medium** shapes the contract of
  several stories · **Low** is local, or the code already answers it.

**Gate:** every High is answered by the product owner and recorded as an ADR before Modules 3–7 are
specified. **Met on 2026-11-10:** all six High questions are answered (ADRs 0023–0027); the FRS
revision that applies them is the next step ([ADR 0021](../docs/decisions/0021-frs-open-questions-ranked-high-ones-gate-modules-3-7.md)).

| # | Item | Question | Kind | Impact | Blocks | Status |
|---|---|---|---|---|---|---|
| 1 | A.2-5 | Passwords alongside Google SSO, instead of it, or keep SSO and drop US-01/US-02 as written? | C | **High** | Module 1: US-01, US-02, the email half of US-07 | **Answered:** keep Google sign-in; rewrite US-01/US-02 as first sign-in. [ADR 0023](../docs/decisions/0023-google-sign-in-and-whitelist-stay.md) |
| 2 | N-1 | Does the `AllowedEmail` whitelist survive, or does US-01's open registration replace it? | C | **High** | US-01, and who can join at all | **Answered:** the whitelist stays; registration stays closed. [ADR 0023](../docs/decisions/0023-google-sign-in-and-whitelist-stay.md) |
| 3 | A.2-8 | How do existing accounts, members and transactions move from per-resource membership to workspaces? | A | **High** | Modules 1, 3–6: the sharing boundary every module reads | **Answered:** no workspace; Admin/Member roles on each account's membership, earliest member becomes Admin. [ADR 0024](../docs/decisions/0024-roles-on-account-membership-no-workspace.md) |
| 4 | A.2-10 | Is "wallet (stock)" the same concept as a broker account? | B | **High** | Module 3's data model; stock transactions in Module 5 | **Answered:** no; the two entities stay, and a stock wallet is a broker account. [ADR 0025](../docs/decisions/0025-spend-and-broker-accounts-stay-separate.md) |
| 5 | N-2 | What becomes of the system-wide admin (`AppUser.IsAdmin`, `/api/admin/*`) next to v1.1's per-workspace Admin? | B | **High** | US-05; admin-managed categories (Module 4) | **Answered:** it stays, as a fourth user class; account roles are resolved per request. [ADR 0026](../docs/decisions/0026-system-administrator-stays-a-separate-tier.md) |
| 6 | N-3 | A.1-3 (owner-only editing) needs a creator column. What is the creator of an existing transaction? | A | **High** | US-05 AC6–AC8; every write path in Module 5 | **Answered:** new `CreatorId`; backfilled from `PayerId`, else the account Admin. [ADR 0027](../docs/decisions/0027-transaction-creator-backfilled-from-payer-else-admin.md) |
| 7 | A.2-7 | Who writes endpoints, data types and status codes, and when? | A | Medium | The contract of every story | Proposed: follow `openapi.json` and the conventions in [ADR 0012](../docs/decisions/0012-rest-conventions-and-non-goals.md). [cross-cutting §7](cross-cutting.md#7--the-error-contract-v11-specifies-messages-the-api-deliberately-does-not-send) |
| 8 | N-4 | Do Telegram ingest and income/expense shares survive under workspaces? | A | Medium | Module 5 | **Answered:** both stay and join the FRS. [ADR 0029](../docs/decisions/0029-telegram-ingest-and-shares-stay.md) |
| 9 | A.2-9 | Session timeout after inactivity: how long? | B | Low | US-02 AC | Open. Two readings: idle time (not tracked anywhere) or token lifetime (15-minute access, 30-day refresh). [US-02](module-1-auth-onboarding.md#us-02--log-in-to-an-existing-account) |
| 10 | A.2-6 | Confirm "Must" on every story, or set real priorities. | A | Low | Ordering only | Open, pending review. |
| 11 | A.2-4 | Does a display-name change apply to past transactions? | C | Low | — | **Answered:** yes, names are read live. [ADR 0028](../docs/decisions/0028-display-names-read-live.md) |

Ranked by impact, then by how many modules each question blocks. Rows 1 and 2 are best answered
together: the credential model and who may register at all are one decision.

## What is already right

Worth stating, because a gap analysis reads as if nothing works:

- **Per-resource sharing is finer-grained than the workspace model it would replace.** US-04 AC6 —
  "Member cannot see any wallet in the workspace that has not been shared with them" — is satisfied
  today by construction: there is no container, so access is per account and nothing leaks by
  default. A workspace adds a broader boundary that then needs a per-wallet share flag to claw the
  granularity back (v1.1 §4.1 marks `Wallet.Shared-with` as `[TBD]`, which is exactly this problem).
- **US-05 AC4** — a removed member's transactions survive — holds today. Membership removal is a
  join-row delete; transactions belong to the account.
- **US-07 AC5** — a display-name change shows up everywhere — holds today. Display name is always
  read through the `AppUser` navigation and is never denormalised into a transaction or member row.
- **Server-side permission enforcement** (v1.1 §5, Security) is the existing pattern: every check
  lives in a service, not a controller and not the UI ([`rules/access-control.md`](../docs/rules/access-control.md)).

---

## Method

Every claim here was checked against code or generated artifacts, in this order of authority — the
same order [`docs/README.md`](../docs/README.md) defines:

1. `docs/reference/openapi.json` and the C# source — routes, DTO shapes, entity columns.
2. `docs/rules/*` — invariants.
3. `docs/reference/*.md` — behaviour.
4. `docs/decisions/*` — why a thing is the way it is. Cited when v1.1 would supersede one.

Where v1.1 says `[TBD — to be defined]` and the code has a settled answer, that is recorded as an
answer available to the FRS rather than as a gap. There are more of those than there are gaps.

## Files

One file per module, plus the cross-cutting differences that belong to none of them.

| File | Covers |
|---|---|
| [`cross-cutting.md`](cross-cutting.md) | The nine structural differences that span modules |
| [`module-1-auth-onboarding.md`](module-1-auth-onboarding.md) | US-01 … US-05, AC by AC |
| [`module-2-profile.md`](module-2-profile.md) | US-06 … US-07, AC by AC, plus the profile field map |
| [`module-3-spend-account.md`](module-3-spend-account.md) | Wallets, sharing, the balance-write invariant, the wallet-type merge |
| [`module-4-category.md`](module-4-category.md) | The kind-bound lookup, and what "custom categories" would cost |
| [`module-5-transaction.md`](module-5-transaction.md) | Seven leaves, shares, and the recurring-transaction infrastructure gap |
| [`module-6-obligation.md`](module-6-obligation.md) | Counterparties, the obligations rollup, and contact scoping under a workspace |
| [`module-7-report-dashboard.md`](module-7-report-dashboard.md) | The greenfield: what exists to build on, and what a report spec must decide |

## Status of this folder

Working material, in the sense of [`docs/README.md`](../docs/README.md#not-documentation): input to
decisions, not a record of them. It describes a *proposed* target and is deliberately outside
`docs/`, which describes only what the system does today. When a decision here is taken, it belongs
in `docs/decisions/` as an ADR; when it is built, it belongs in `docs/reference/`.
