# Module 1 — Authentication & Onboarding

v1.1 §3.2, stories US-01 … US-05, against the backend at `6c3c953`.

v1.1 calls this the foundation layer with no dependencies, on which every other module depends. That
is accurate, and it is why the two conflicts inside it — authentication
([cross-cutting §3](cross-cutting.md#3--authentication-two-incompatible-models)) and the workspace
([cross-cutting §1](cross-cutting.md#1--the-workspace-does-not-exist)) — gate the whole programme.

| Story | Verdict |
|---|---|
| [US-01 Register a new account](#us-01--register-a-new-account) | **Conflict** |
| [US-02 Log in to an existing account](#us-02--log-in-to-an-existing-account) | **Conflict** |
| [US-03 Mandatory wallet creation](#us-03--mandatory-wallet-creation-before-accessing-the-app) | **Absent** |
| [US-04 Admin invites a member](#us-04--admin-invites-a-member-to-a-shared-workspace) | **Absent** |
| [US-05 Admin and Member permissions](#us-05--manage-admin-and-member-permissions-within-a-workspace) | **Conflict** |

Existing surface for the whole module — three endpoints:

```
POST /api/auth/google      id token in, access + refresh token out
POST /api/auth/refresh     rotate
POST /api/auth/logout      revoke
```

---

## US-01 — Register a new account

**Verdict: Conflict.** There is no registration. An `AppUser` row appears as a side effect of the
first successful Google login, and only for an address an administrator has already whitelisted.

### What exists

`AuthService.LoginWithGoogleAsync` (`Fiolyn/Services/Internal/AuthService.cs`):

1. Verify the Google ID token against `GoogleAuth:ClientId`; reject an unverified email.
2. Normalise the email to lowercase.
3. **Check `AllowedEmail`. Not present → `ForbiddenAccessException` → 403, no user created.**
4. Find the user by `GoogleSub` *or* `Email`; create if neither matches.
5. Issue an access token and a refresh token, and save both with the user in one `SaveChangesAsync`
   — so a partial registration cannot leave a token without its user.

### Field by field

| v1.1 registration field | Today |
|---|---|
| Display name | Not collected — taken from the Google profile, and **nullable** where v1.1 requires it |
| Email | Not collected — taken from the verified Google ID token. Unique index ✓ |
| Password | **No column, no hashing, no verification** |
| Confirm password | N/A |
| Tele ID (optional, skippable) | Not collected at any point in login. Set afterwards via `PUT /api/users/me/telegram-id` — which matches v1.1's "can be filled in later via Profile settings" |

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · valid email + password ≥ 8 → account created, auto-logged-in, redirected to Onboarding | **Absent** | No password. No onboarding. The account-created-and-logged-in half happens, through a different flow |
| AC2 · email already in use → inline error, no account created | **N/A** | Unreachable: a repeat Google login *is* a login. The unique index on `AppUser.Email` exists and would surface 409, but no code path reaches it |
| AC3 · password mismatch → inline error | **Absent** | No password |
| AC4 · required field empty → client-side validation blocks submit | **Frontend** | Backend-side, `[ApiController]` already returns `ValidationProblemDetails` with a per-field `errors` map — the right shape for inline errors |
| AC5 · user is authenticated after registration, no second login | **Met in substance** | `POST /api/auth/google` returns the token pair on the same call that creates the user |

### Business rules

| Rule | Status |
|---|---|
| Password minimum 8 characters | **Absent** |
| Email unique across the system | **Met** — unique index on `AppUser.Email`, plus one on `GoogleSub` |
| New users get the Admin role in their own workspace | **Absent** — no workspace, no role. `IsAdmin` defaults to `false` and is a system-wide flag ([cross-cutting §2](cross-cutting.md#2--two-different-admins)) |
| A personal workspace is created automatically | **Absent** |

### To implement as written

- Resolve [cross-cutting §3](cross-cutting.md#3--authentication-two-incompatible-models) first.
  Nothing below can be scoped until the credential model is chosen.
- Decide the whitelist's fate ([cross-cutting §4](cross-cutting.md#4--the-whitelist-is-a-closed-registration-policy-v11-does-not-mention)).
  US-01 as written deletes it.
- `Password` column plus a hashing choice; `DisplayName` becomes non-nullable, which needs a
  backfill for any existing row where Google supplied no name.
- `Workspace` + `WorkspaceMember`, created in the same save as the user — the existing single-save
  discipline in `LoginWithGoogleAsync` is the pattern to follow.
- A `POST /api/auth/register` endpoint. Note that v1.1 §4.1 gives `User` a `Name` **and** a
  `DisplayName` as separate required fields; the code has only `DisplayName`
  (see [`module-2-profile.md`](module-2-profile.md#the-field-map)).

---

## US-02 — Log in to an existing account

**Verdict: Conflict.** Login exists and works; it takes a different credential, returns a different
kind of session, and has no notion of onboarding completeness.

### Side by side

| v1.1 | Today |
|---|---|
| Email + password | Google ID token, verified against Google's JWKS with `GoogleAuth:ClientId` as the audience |
| "Remember me" persists the session across browser restarts | No server-side flag. The 30-day refresh token already survives a restart **if the client stores it** — so "Remember me" is a client storage choice (`localStorage` vs `sessionStorage`), not an API change |
| "Forgot password" → reset via email | Absent, and no email capability exists at all |
| Session times out after inactivity, duration `[TBD]` | **Not inactivity-based.** A 15-minute access token, refreshable for up to 30 days. Idle time is not tracked anywhere |
| Redirect to Dashboard, or Onboarding if incomplete | No onboarding state exists to branch on |
| Generic "Incorrect email or password" | 401 for a bad token; **403** for a valid token whose email is not whitelisted — a distinction the caller can see |

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · correct credentials → logged in, redirected to Dashboard | **Partial** | Login works with a different credential; the redirect is frontend |
| AC2 · wrong credentials → generic error, neither field blamed | **Partial** | No field-level leak, but 401 and 403 distinguish "bad credential" from "not authorised" |
| AC3 · onboarding incomplete → redirected to Onboarding | **Absent** | See US-03 |
| AC4 · "Remember me" → session survives a browser restart | **Frontend** | The refresh token already makes this possible; no backend work |

### Worth keeping

Refresh rotation is the strongest part of the current auth stack and no v1.1 requirement conflicts
with it: every refresh inserts a new token and revokes the old one in a single save; replaying a
revoked token revokes the entire chain for that user on the assumption of a leak; and `RevokedAt`
carries `[ConcurrencyCheck]` so two concurrent refreshes race deterministically, the loser getting
401 ([`reference/auth.md`](../docs/reference/auth.md)). Whatever credential is chosen, this should
survive.

### To implement as written

Password verification and a reset flow with token issue, expiry and single use — plus the SMTP
dependency the system does not have. An inactivity timeout is a new concept: it needs either a
sliding server-side session or a last-activity timestamp checked on each request, neither of which
exists.

---

## US-03 — Mandatory wallet creation before accessing the app

**Verdict: Absent.** No gate, no onboarding state, no redirect. A user with no accounts simply gets
empty lists from every endpoint.

### What already fits

The wallet form itself needs no backend change for two of the three types.
`POST /api/spend-accounts` takes exactly the three fields US-03's step 2 asks for:

```csharp
public record SpendAccountRequest(
  [Required] [MaxLength( 100 )] string Name,
  [Required] SpendAccountType Type,
  [Required] long Balance ) ;
```

`Balance` is a plain `long` with no positive constraint, so **AC5 — "balance value of 0 is accepted
without error" — already holds.** `SpendAccountService.CreateAsync` adds the creator as the first
member in the same save, so the account is immediately usable, satisfying the "full-featured wallet,
identical to wallets created later" rule (AC4 likewise, since it is literally the same endpoint).

The gap in the form is the third type: `SpendAccountType` is `Cash | Credit`. A stock wallet is a
`BrokerAccount`, a different entity created through a different endpoint, and it requires an existing
spend account to link to — so it cannot be the *first* thing a new user creates
([cross-cutting §6](cross-cutting.md#6--wallet-versus-spendaccount-plus-brokeraccount)).

### The bypass condition

"User has accepted a workspace invite and the workspace contains at least one shared wallet
accessible to them" maps cleanly onto the current model without a workspace:

```csharp
// "does this user have access to any wallet?" — today, in one query
spendAccounts.Query().Any( a => a.Members.Any( m => m.AppUserId == userId ) )
```

That is the same predicate every user-scoped read already uses.

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · new user with no shared wallet lands on Onboarding, cannot reach the Dashboard | **Absent** | Needs both a state to check and a client-side guard |
| AC2 · any URL while incomplete → redirected to Onboarding | **Absent / frontend** | A route guard. The backend does not 403 an accountless user, and probably should not — see below |
| AC3 · wallet created → Dashboard, Onboarding never shown again | **Absent** | Derivable: "has ≥ 1 account membership" |
| AC4 · same validation as the regular wallet form | **Met** | Same endpoint, same DTO annotations |
| AC5 · balance 0 accepted | **Met** | No range constraint on `Balance` |
| AC6 · invited user with a shared wallet skips onboarding | **Partial** | The membership predicate exists; the invite it depends on does not (US-04) |

### Design note — do not build a flag

"Onboarding complete" is fully derivable from "the user is a member of at least one account". A
stored `HasCompletedOnboarding` boolean would be a second source of truth for a fact the membership
table already answers, and would drift the first time an account is deleted or a membership removed.

The cheapest honest implementation is a field on the existing profile response — `GET /api/users/me`
is already called on app load — carrying the derived answer. That adds one predicate to one query
and no new state.

The redirect itself is frontend. The backend should **not** start returning 403 to a user with no
accounts: every endpoint already behaves correctly for them by returning empty pages, and adding a
global gate would break the Telegram ingest path, which has no browser and no onboarding.

---

## US-04 — Admin invites a member to a shared workspace

**Verdict: Absent.** The largest single gap in Module 1. No invite entity, no accept, no decline, no
expiry, no cancellation, no status, no notification.

### The nearest existing thing, and why it is not an invite

`POST /api/spend-accounts/{id}/members` takes an email and adds that user to the account.
Structurally it is the opposite of an invitation:

| US-04 | `AddMemberAsync` today |
|---|---|
| Only an Admin may invite | **Any existing member may add anyone.** There is no role to check ([ADR 0011](../docs/decisions/0011-membership-is-access.md)) |
| The invitee accepts or declines | **No consent at all.** The membership row is inserted immediately |
| An invite to an unregistered address is stored and auto-applied on registration | **404.** The target user must already exist: `?? throw new KeyNotFoundException( $"User with email {normalized} not found." )` |
| Pending → Accepted / Declined / Expired / Cancelled | No status. A membership row exists or it does not |
| Expires after 7 days | No expiry, no scheduler ([cross-cutting §8](cross-cutting.md#8--no-scheduler-no-email-no-notifications)) |
| Addressable by email **or** Tele ID | Email only — though `AppUser.TelegramId` is unique and indexed, so a Tele ID lookup is a one-line addition |

Idempotence is already right: adding an existing member returns the current roster rather than
failing.

The other half-match is `AllowedEmail`, which carries `Note` and `InvitedByUserId` — a
pre-authorisation for an address that has no account yet. It is admin-managed, system-wide, and has
no accept step, but it is the closest existing analogue to "store an invite for someone who has not
registered", and it is where that logic would most naturally be generalised if the whitelist survives.

### Acceptance criteria

| AC | Status |
|---|---|
| AC1 · existing account → in-app notification on next login | **Absent** — no notification store |
| AC2 · non-existing email/Tele ID → stored, auto-applied at registration | **Absent** — currently a 404 |
| AC3 · accept → shared wallets appear immediately | **Absent** (the *effect* is what `AddMemberAsync` already does, minus consent) |
| AC4 · decline → not joined, admin sees "Declined" | **Absent** |
| AC5 · no response in 7 days → "Expired" | **Absent** |
| AC6 · a member cannot see an unshared wallet | **Met** — by construction, since access is per account |
| AC7 · "Invite member" visible to Admins only | **Absent / frontend** — no role to key it on |

AC6 is worth pausing on: it is satisfied today *because* there is no workspace. Introducing one
creates the leak that AC6 forbids, and then requires a per-wallet share mechanism to close it again
([cross-cutting §1](cross-cutting.md#1--the-workspace-does-not-exist)).

### To implement as written

New `Invite` entity — v1.1 §4.1 already sketches it correctly:
`(Id, WorkspaceId, InvitedBy, Address, Status, CreatedAt, ExpiresAt)`. Then:

- Endpoints for create, cancel, accept, decline and list — for the admin and for the invitee.
- Address resolution against both `AppUser.Email` and `AppUser.TelegramId`.
- Deferred application: on registration, match the new account's email or Tele ID against pending
  invites and apply them. This is the piece that interacts with US-01 and cannot be built first.
- Expiry **evaluated on read** against `ExpiresAt`, rather than by a scheduled job — same answer, no
  new infrastructure ([cross-cutting §8](cross-cutting.md#8--no-scheduler-no-email-no-notifications)).
- A notification surface for the bell icon. The invite list endpoint may be enough; a general
  notification system is a much larger build and nothing else in v1.1 needs one.

---

## US-05 — Manage Admin and Member permissions within a workspace

**Verdict: Conflict.** This story asks for the exact model
[ADR 0011](../docs/decisions/0011-membership-is-access.md) considered and rejected. It is the
decision-heaviest story in the FRS.

### The permission matrix, row by row

v1.1 §2.2 against current behaviour:

| Action | v1.1 Admin | v1.1 Member | Today |
|---|---|---|---|
| View shared wallets | ✓ | ✓ | ✓ — any member |
| Create transactions in a shared wallet | ✓ | ✓ | ✓ — any member |
| Edit own transactions | ✓ | ✓ | ✓ — though "own" is not a concept; any member may |
| Delete own transactions | ✓ | ✓ | same |
| Edit/delete **other members'** transactions | ✓ | ✗ | **Any member may.** No creator column to check ([cross-cutting §5](cross-cutting.md#5--transaction-ownership-has-no-column-to-stand-on)) |
| Create / edit / delete wallets | ✓ | ✗ | **Any member may**, including soft-deleting the whole account |
| Invite members | ✓ | ✗ | **Any member may** add another directly |
| Remove members | ✓ | ✗ | **Any member may** remove any other, including the creator. The only guard: the last member cannot be removed — `BadRequestException`, 400 |
| Edit own profile | ✓ | ✓ | ✓ |

Five of nine rows differ, and they differ in the same direction: the current model grants every
member what v1.1 reserves for the Admin.

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · member deleting a wallet → "permission denied" | **Conflict** | Any member may delete. Blocked only if an active broker or transaction still references it (400) |
| AC2 · member cannot see "Invite member" | **Absent / frontend** | No role to key on |
| AC3 · removed member loses access immediately | **Met** | Membership is evaluated per query; the next request already excludes them |
| AC4 · a removed member's transactions stay visible | **Met** | Removal deletes a join row; the ledger is untouched. `PayerId` also survives — removal is not a user delete |
| AC5 · Admin can view the full member list | **Partial** | `GET /api/spend-accounts/{id}/members` exists and returns email + display name — but to *any* member, not only an admin |
| AC6 · member edits only their own transactions | **Conflict** | No creator column |
| AC7 · Admin edits any transaction in the workspace | **Partial** | A *system* admin can already read and delete any transaction via `/api/admin/*` — but cannot **edit**: every admin *transaction* controller exposes GET and DELETE only, never PUT (the admin account and category controllers do have PUT) |
| AC8 · member editing another's transaction → denied | **Conflict** | Allowed today |

AC7 hides a real design question. Across all eight admin transaction controllers there is no update
verb — an administrator can observe and remove, but not rewrite someone else's ledger entry, and the
asymmetry is consistent enough to look deliberate (the admin controllers for accounts and categories
*do* expose PUT). US-05 grants the workspace Admin full edit rights over every transaction. Worth
confirming that is intended before the asymmetry is dissolved.

### Business rules

| Rule | Status |
|---|---|
| Admin may remove a member at any time | **Partial** — any member may |
| Removal revokes wallet access immediately | **Met** |
| A removed member's transactions are retained | **Met** |
| An Admin cannot downgrade themselves if they are the only Admin | **Absent** — no roles. The structural analogue, "the last member cannot be removed", exists and is enforced |
| Creator-only edit/delete, with an Admin override | **Conflict** — [cross-cutting §5](cross-cutting.md#5--transaction-ownership-has-no-column-to-stand-on) |

### To implement as written

1. A superseding ADR for [0011](../docs/decisions/0011-membership-is-access.md). This is the
   decision; everything else is consequence.
2. `WorkspaceMember.Role`, and a way to evaluate it **per request** — it cannot live in the JWT,
   because a user can be Admin in one workspace and Member in another
   ([cross-cutting §2](cross-cutting.md#2--two-different-admins)).
3. `CreatorId` on `TransactionBase`, non-nullable, with a backfill decision for existing rows.
4. A permission check on every write path: the five services under
   `StandardTransactionServiceBase<T>`, `TransferTransactionService`, `StockTransactionService`, the
   auto-created share children, and the Telegram ingest.
5. A resolution of the 403-versus-404 question
   ([cross-cutting §7](cross-cutting.md#7--the-error-contract-v11-specifies-messages-the-api-deliberately-does-not-send)).
   US-05 wants 403 with a message where the system deliberately returns an indistinguishable 404.
6. Role checks on wallet create/update/delete and on member add/remove, which today check only
   membership.
