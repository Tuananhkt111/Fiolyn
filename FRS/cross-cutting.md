# Cross-cutting differences

Nine structural gaps between `FRS_Fiolyn_v1.1.docx` and the backend at `6c3c953`. None belongs to a
single story; each one changes how several modules get built. Story-level detail is in
[`module-1-auth-onboarding.md`](module-1-auth-onboarding.md) and
[`module-2-profile.md`](module-2-profile.md).

Ordered by how much they block: §1–§5 have to be settled before Module 1 can be scoped, §6–§9 before
Modules 3–7 can be.

---

## 1 · The workspace does not exist

**v1.1** makes `Workspace` the sharing boundary (§1.3, §2.1, §4.1). Every user gets one at
registration and may belong to others. `WorkspaceMember` carries the role. A wallet belongs to a
workspace. Members see the wallets shared inside it.

**Today** there is no container of any kind. Sharing is per resource, through two join entities:

| | `SpendAccountMember` | `BrokerAccountMember` |
|---|---|---|
| Key | `(SpendAccountId, AppUserId)` | `(BrokerAccountId, AppUserId)` |
| Columns beyond the keys | `JoinedAt` | `JoinedAt` |
| Role column | none | none |
| Auditable / soft-delete | neither — joining is an insert, leaving is a hard delete | same |

Access is one predicate, applied in every user-scoped service query
([`rules/access-control.md`](../docs/rules/access-control.md)):

```csharp
.Where( a => a.Members.Any( m => m.AppUserId == userId ) )
```

### What this costs

Adding a workspace is not additive. Every user-scoped read and write in the system resolves access
through that predicate; a workspace inserts a level above it, and each call site has to decide
whether it filters by workspace, by membership, or by both.

The migration is the harder half, and v1.1 flags it as open (Appendix A.2-8) without proposing a
path. Existing rows carry no workspace: for every current `SpendAccount` with *n* members, someone
must decide whose workspace it lands in and what the other *n−1* members become.

### The granularity trap

The per-resource model is **finer**-grained than the workspace model, not coarser. Today a user sees
exactly the accounts they were added to. Under a workspace, membership admits you to the container,
and v1.1 then needs a per-wallet share flag to keep private wallets private (US-04 business rules,
"private wallets remain hidden") — which §4.1 leaves as `Wallet.Shared-with [TBD — to be defined]`.

That `[TBD]` is the whole granularity question. Whatever it resolves to will look very much like
`SpendAccountMember`.

### Supersedes

[ADR 0011 — membership is access](../docs/decisions/0011-membership-is-access.md), which considered
a role column on the join and rejected it on the grounds that "for Fiolyn's actual users that tier
split has no meaning". v1.1 asserts the opposite. That is a legitimate change of mind about the
product — but it is a change of mind, and it needs a superseding ADR rather than a quiet rewrite.

---

## 2 · Two different Admins

The word "Admin" names two unrelated things, and v1.1 defines only one of them.

| | **v1.1 Admin** | **`AppUser.IsAdmin`** |
|---|---|---|
| Scope | One workspace | The whole system |
| How you get it | Automatic — you created the workspace | Manual `UPDATE "AppUser" SET "IsAdmin" = true`; there is no promotion code path anywhere |
| What it grants | Manage that workspace's wallets, members and transactions | Every `/api/admin/*` route: the email whitelist, transaction categories, other users' admin flags, and read/delete on every other user's accounts and transactions |
| Where it lives | `WorkspaceMember.Role` (proposed) | A boolean column on `AppUser`, carried in the JWT as the string claim `isAdmin` |
| In v1.1 §2.2 | Defined | **Absent** |

v1.1 §2.2 lists three user classes — Admin, Member, unauthenticated visitor. The system
administrator is not one of them, yet roughly forty implemented endpoints exist only for that role,
and one of them (`/api/allowed-emails`) is the sole way anyone gets an account at all today.

### What this costs

Three questions, none answered in v1.1:

1. Does the system administrator survive the move to workspaces? If it does, §2.2 needs a fourth
   user class and §8's `PERMISSION_DENIED` needs to distinguish the two tiers.
2. If it does not, what replaces the whitelist, the category admin and the cross-user admin surface?
3. What does the `isAdmin` JWT claim mean once a user can be Admin in one workspace and Member in
   another? A per-workspace role cannot live in a token claim that is minted at login, before a
   workspace is chosen — it has to be resolved per request against `WorkspaceMember`.

Point 3 is the one that leaks into implementation. The current `Admin` authorization policy reads a
claim ([`rules/auth.md`](../docs/rules/auth.md)); a workspace role cannot be checked that way.

---

## 3 · Authentication: two incompatible models

v1.1's Appendix A.2-5 raises this and marks it "⏳ Pending — blocks Module 1". It is the correct
call. The two models share no component.

| | **v1.1 (US-01, US-02)** | **Today** |
|---|---|---|
| Credential | Email + password, min 8 chars | Google ID token — no password anywhere in the schema |
| Who may register | Anyone, self-service | Only an address an admin already added to `AllowedEmail` |
| Account creation | Explicit registration form | Implicit, on first successful Google login |
| Session | "Remember me"; inactivity timeout `[TBD]` | 15-minute HS256 access token + 30-day opaque refresh token, rotated on every use, with reuse detection that revokes the whole chain |
| Recovery | "Forgot password" → email | N/A — Google owns the credential |
| Failure message | Generic "Incorrect email or password" | 401 for an invalid token, **403** for a valid token whose email is not whitelisted |

The refresh model is not a weaker version of "Remember me" — it is a different and stronger
mechanism, with rotation, reuse detection and a `[ConcurrencyCheck]` on `RevokedAt` so two
concurrent refreshes race deterministically ([`reference/auth.md`](../docs/reference/auth.md)).
Whatever is decided, that machinery should be kept; it is the part of the current auth stack with
real value in it.

### Two problems inside v1.1 itself

- **Email delivery contradicts a stated constraint.** §2.4 says "the system sends no email, SMS or
  external message". US-02 requires "Forgot password → triggers password reset flow via email". §7
  notices the tension and marks the delivery mechanism `[TBD]`. It is not just undecided — the
  system has **no email capability at all**: no SMTP configuration key exists
  ([`guides/configuration.md`](../docs/guides/configuration.md) lists every key), no mail client is
  registered in DI, and no package is referenced. Password reset is a new external dependency, not a
  new endpoint.
- **The generic-error rule is weaker than it looks.** US-02 requires failures not to reveal which
  field was wrong. Today a non-whitelisted address gets 403 while a bad token gets 401, so the
  status code already distinguishes "we don't want you" from "that credential is wrong". If the
  whitelist survives (see §4), that distinction has to be collapsed deliberately.

### The three ways out

| Option | Consequence |
|---|---|
| **Add passwords alongside Google SSO** | Two credential paths per account, an account-linking rule, password storage and reset infrastructure. US-01/US-02 buildable as written; every existing account keeps working. |
| **Replace Google SSO with passwords** | US-01/US-02 buildable as written. Every existing user needs a password set through some out-of-band flow, and `GoogleSub` — currently a unique, required column and half the login lookup — is retired. |
| **Keep Google SSO, rewrite US-01/US-02** | No new infrastructure. "Register" becomes "first login"; "Remember me" becomes refresh-token retention; "forgot password" disappears. §8's `PASSWORD_*` and `INVALID_CREDENTIALS` rows go with it. |

The third is the only one that needs no work outside the FRS, which is worth weighing against the
fact that nothing else in v1.1 depends on passwords existing.

---

## 4 · The whitelist is a closed-registration policy v1.1 does not mention

Today, registration is closed. `AuthService.LoginWithGoogleAsync` checks the incoming email against
`AllowedEmail` and throws `ForbiddenAccessException` — 403 — before any user row is created. No
admin action, no account. The `AllowedEmail` row even carries `Note` and `InvitedByUserId`, making it
the closest thing the system currently has to an invitation.

US-01 describes open self-service registration: "A first-time visitor with no existing account can
register using email and password", with the only stated rules being password length and email
uniqueness.

**These cannot both be true.** v1.1 mentions the whitelist exactly once — inside Appendix A.2-5,
describing the baseline — and never asks whether removing it is intended. It is a product decision
about who is allowed to have an account, currently unrecorded on either side.

If the whitelist survives, US-01 needs a rejection path that §8 does not have (a valid registration
for an address nobody authorised), and the "in-app invite" of US-04 starts to look like the same
mechanism seen from the other end.

---

## 5 · Transaction ownership has no column to stand on

US-05 makes creator identity a permission input: only the creator edits or deletes, with an Admin
override (AC6, AC7, AC8). Appendix A.1-3 records it as a decision already taken.

`TransactionBase` has no creator column. The fields that exist:

| Column | What it is | Why it is not a creator |
|---|---|---|
| `PayerId` | The user *attributed as having paid* | Nullable; `SetNull` when that user is deleted; the caller may set it to any user; defaulted to the caller only when omitted on create |
| `SpendAccountId` | Which account the money moved in | Carries access today, via that account's membership |
| `Created` / `LastModified` | Audit timestamps, stamped in `SaveChangesAsync` | Timestamps, not identity |

So today **any member of a shared account can edit or delete any transaction in it**, including one
another user recorded — which is [ADR 0011](../docs/decisions/0011-membership-is-access.md) working
as designed, and exactly what US-05 AC8 forbids.

### What this costs

- A new **non-nullable** `CreatorId` on `TransactionBase` (TPT base table), with `Restrict` or
  `SetNull` on user delete — and `SetNull` would reintroduce ownerless rows, so `Restrict` is the
  consistent choice.
- A **backfill decision for existing rows.** `PayerId` is the only candidate and it is both nullable
  and semantically different; where it is null there is no recoverable answer. A "system" sentinel
  or an admin-assigned owner has to be chosen deliberately.
- A **check at every write path**: the five services under `StandardTransactionServiceBase<T>`,
  `TransferTransactionService`, `StockTransactionService`, the auto-created share children
  ([ADR 0007](../docs/decisions/0007-shares-as-paired-lend-borrow-rows.md)), and the Telegram ingest
  — which creates transactions with no HTTP caller at all and resolves its author from
  `AppUser.TelegramId`.
- A resolution of the **403-vs-404 conflict** in §7 below.

### One free win

US-05 AC4 — "transactions previously created by a removed member remain visible" — already holds.
Removing a member deletes a join row; nothing touches the ledger. No work.

---

## 6 · Wallet, versus SpendAccount plus BrokerAccount

Appendix A.2-10 asks whether "wallet (stock)" is the same concept as a broker account. It is not,
and the difference is structural rather than cosmetic.

| | **v1.1 `Wallet`** | **Today** |
|---|---|---|
| Entities | One, with `Type ∈ {cash, credit, stock}` | Two: `SpendAccount` (`Cash \| Credit`) and `BrokerAccount` |
| Balance | Every wallet has one | `SpendAccount.Balance` only. **A `BrokerAccount` has no balance column.** |
| Relationship | — | `BrokerAccount.SpendAccountId` is a **required** FK: a broker's cash moves through a spend account, and a trade adjusts *that* account's balance |
| Sharing | Workspace + `Shared-with [TBD]` | Two independent membership tables |

A single `Wallet` with `type = stock` and its own balance would have to absorb the linking FK. That
FK is load-bearing in three places: stock trades write the linked spend account's balance;
repointing a broker is deliberately non-retroactive; and the FK is `Restrict` on purpose, because
cascade forms a dependency loop ([ADR 0010](../docs/decisions/0010-broker-spend-account-fk-restrict.md)).

Collapsing the two entities means deciding what happens to all three. Keeping them separate means
US-03's wallet-type picker offers a type that creates a different entity through a different
endpoint — workable, but it should be a stated decision rather than a surprise in the UI.

### What is already fine

`POST /api/spend-accounts` takes `Name`, `Type` and an initial `Balance`, and `Balance` is a plain
`long` with no positive constraint — so US-03's wallet form and its AC5 ("balance value of 0 is
accepted") are satisfiable today for cash and credit, without touching the endpoint.

---

## 7 · The error contract: v1.1 specifies messages the API deliberately does not send

v1.1 §8 gives eleven error codes with a "Message Shown to User" column, and calls that copy "the copy
of record" — "This email is already registered", "Incorrect email or password", "permission denied".

`GlobalExceptionHandler` writes RFC 7807 `ProblemDetails` in which **`detail` always equals
`title`**, both derived from the status code. An exception message never reaches a client, because
it can carry entity ids, email addresses or SQL fragments
([`reference/errors.md`](../docs/reference/errors.md), [ADR 0012](../docs/decisions/0012-rest-conventions-and-non-goals.md)).
A client distinguishes failures by status code alone.

Two consequences:

- Every §8 message has to be **produced client-side** by mapping a status code, or the error
  contract has to change to carry a machine-readable code. The latter is a real option — a stable
  `code` field leaks nothing — but it is a change to a deliberate decision, and it needs its own ADR.
- §8 leaves every HTTP status `[TBD]` while the code has a settled map. The FRS can simply adopt it:

| v1.1 error code | Existing exception | Status today |
|---|---|---|
| `EMAIL_ALREADY_REGISTERED`, `EMAIL_IN_USE` | `ConflictException` (unique index, Postgres `23505`) | 409 |
| `PASSWORD_MISMATCH`, `PASSWORD_TOO_SHORT`, `REQUIRED_FIELD_MISSING` | data-annotation validation, short-circuited by `[ApiController]` | 400 — and in the **`ValidationProblemDetails` shape**, with a per-field `errors` map, which is what US-01's inline field errors need |
| `INVALID_CREDENTIALS` | `UnauthorizedAccessException` | 401 |
| `PERMISSION_DENIED` | see the conflict below | — |
| `ONBOARDING_INCOMPLETE` | — | nothing exists |
| `INVITE_EXPIRED`, `INVITE_DECLINED` | — | nothing exists |

### The 403-versus-404 conflict

US-05 requires a member attempting an Admin-only action to be "rejected with a permission denied
message" — 403 with copy.

Today, "not a member" is reported as **404**, deliberately and consistently, so that a caller learns
nothing about the existence of resources they cannot see
([ADR 0011](../docs/decisions/0011-membership-is-access.md),
[`reference/errors.md`](../docs/reference/errors.md#status-mapping)).

Both behaviours are defensible and they are mutually exclusive. The distinction v1.1 needs is
between *"you are in this workspace but not allowed to do that"* (403 is right, and leaks nothing new
— the caller already knows the resource exists) and *"you are not in this workspace"* (404 should
stay). That split is workable, but it has to be written down; applied bluntly, US-05 turns the
current 404 policy into an existence oracle.

---

## 8 · No scheduler, no email, no notifications

Three pieces of infrastructure v1.1 assumes, none of which the system has.

| Assumed by | Needs | Status |
|---|---|---|
| US-04 — invites expire after 7 days | A scheduled job, or lazy evaluation on read | **Absent.** No `IHostedService`, no `BackgroundService`, no Hangfire or Quartz, no cron. Even migrations are deliberately never run at startup ([ADR 0004](../docs/decisions/0004-migrations-never-run-on-startup.md)) |
| US-02 — password reset via email | An SMTP client and credentials | **Absent.** No mail package, no configuration key ([`guides/configuration.md`](../docs/guides/configuration.md)) |
| US-04 — the bell-icon invite notification | A notification store and a read endpoint | **Absent.** No entity, no endpoint |
| Module 5 — recurring transactions (§3.1) | The same scheduler | **Absent** |

The expiry rule has a cheaper answer worth considering: store `ExpiresAt` on the invite (v1.1 §4.1
already does) and treat any pending invite past it as expired **on read**. No scheduler, no
background job, correct answer every time it is asked. The same trick does not help recurring
transactions, which have to actually create rows.

Northflank runs the API as a container, so a scheduled path means either a hosted service inside it
or a separate scheduled job — a deployment decision, not just a code one
([`guides/deployment.md`](../docs/guides/deployment.md)).

---

## 9 · Implemented capabilities v1.1 does not mention

Three areas are live, tested and documented, and appear nowhere in v1.1 — not in the module table
(§3.1), the data model (§4.1), the interfaces (§6) or the open questions (Appendix A).

### Telegram ingest and Notion filing

One of the two front doors the project has. `POST /api/Telegram` resolves the sender against
`AppUser.TelegramId`, records `stock buy/sell/div` commands as transactions through the same service
and the same membership check as the REST API, and files everything else to Notion verbatim
([`reference/telegram-ingest.md`](../docs/reference/telegram-ingest.md)).

v1.1 uses Tele ID **only** as an invitation address (§1.3, US-04, §4.1). Under the target design it
is unstated whether ingest survives, and if it does, which workspace a Telegram-created transaction
lands in — the message names a broker, not a workspace, and the sender may belong to several.

### Income / expense shares

An income or expense can carry `shares: [{ counterpartyName, percentage }]`, splitting the amount
between the caller and one auto-created child per share — a `BorrowingTransaction` under an income
parent, a `LendTransaction` under an expense parent, with the parent absorbing rounding loss
([ADR 0007](../docs/decisions/0007-shares-as-paired-lend-borrow-rows.md)).

This is how a household actually splits a bill, so it is squarely inside v1.1's "family shared
workspace" premise, and v1.1 §3.1's one-line description of Module 5 — "Income, expense, transfer,
recurring transactions" — leaves it out. Under US-05's creator-only rule the auto-created children
also need an answer: they have no human author.

### The system-admin surface

Covered in §2. Roughly forty `/api/admin/*` endpoints, plus `/api/allowed-emails`, plus
`GET /api/users` and the admin routes on `UserController`.

---

## Where v1.1's `[TBD]`s already have answers

v1.1 marks a value `[TBD — to be defined]` rather than leaving it blank, "so an empty row is never
mistaken for one nobody thought about". Several of those rows can be filled from the running system
today, without a decision:

| v1.1 `[TBD]` | Answer available in the codebase |
|---|---|
| §2.3 hosting, runtime, database | .NET 10 API on Northflank (Docker, GHCR, GitHub Actions) over Neon PostgreSQL ([ADR 0002](../docs/decisions/0002-neon-and-northflank-hosting.md), [`guides/deployment.md`](../docs/guides/deployment.md)) |
| §4.2 `USER.Id` data type | `long`, identity primary key |
| §4.2 `USER.Email` | `string`, max 256, unique index, normalised to lowercase in the property setter |
| §4.2 `USER.DisplayName` | `string?`, max 256 — **nullable today**, where v1.1 requires it |
| §4.2 `USER.TeleId` | `long?`, unique index; a duplicate surfaces as 409 |
| §6.2 endpoint index | [`docs/reference/openapi.json`](../docs/reference/openapi.json), generated from the running app |
| §8 HTTP statuses | The exception→status map in [`reference/errors.md`](../docs/reference/errors.md) — see §7 above |
| §5 money and quantity types | `long`, never `decimal`; VND has no fractional subunit ([ADR 0001](../docs/decisions/0001-money-as-long-not-decimal.md)) |
| §5 pagination | Offset pagination, `PagedResult<T>`, stable ordering required on every list ([ADR 0009](../docs/decisions/0009-offset-pagination.md), [`rules/pagination.md`](../docs/rules/pagination.md)) |
| §5 audit | Every `IAuditable` entity is stamped in `SaveChangesAsync`; most entities soft-delete rather than hard-delete |
