# Module 2 — Profile

v1.1 §3.3, stories US-06 and US-07, against the backend at `6c3c953`.

The closest fit in the whole FRS. Both stories map onto endpoints that already exist; the gaps are
in *which* fields are editable and in how many requests it takes, not in whether the capability is
there.

| Story | Verdict |
|---|---|
| [US-06 View personal profile](#us-06--view-personal-profile-information) | **Partial** |
| [US-07 Edit personal profile](#us-07--edit-personal-profile-information) | **Partial** |

Existing surface:

```
GET    /api/users/me                 read own profile
PATCH  /api/users/me                 update display name — and nothing else
PUT    /api/users/me/telegram-id     link or change Tele ID
DELETE /api/users/me/telegram-id     unlink Tele ID
```

---

## The field map

v1.1's Profile Fields Reference (§3.3) against `UserResponse` and the `AppUser` entity:

| v1.1 field | v1.1 says | Today | Gap |
|---|---|---|---|
| **Name** | Required, editable — "full legal name or preferred name" | **Does not exist** | A second name field, distinct from Display name. New column |
| **Email** | Required, **editable**, unique | `AppUser.Email`, required, unique index, normalised to lowercase in the property setter. **Not editable** | The SSO identity. Editing it is meaningless until the credential model is decided |
| **Display name** | Required, editable | `AppUser.DisplayName`, editable via `PATCH /api/users/me`. **Nullable**, max 256, sourced from the Google profile | Nullability: v1.1 requires it, the schema permits null |
| **Tele ID** | Optional, editable, clearable | `AppUser.TelegramId`, `long?`, unique index. Editable — through **two separate endpoints**, not the profile save | Shape, not capability |
| **Role** | Read-only, Admin or Member, from the workspace | `AppUser.IsAdmin`, a **system-wide boolean** | Different concept entirely — [cross-cutting §2](cross-cutting.md#2--two-different-admins) |

### Fields the API returns that v1.1 does not list

`UserResponse` carries eight fields; v1.1 specifies five. The extras:

| Field | What it is |
|---|---|
| `PictureUrl` | The Google profile picture, refreshed on every login. v1.1 has no avatar anywhere in Module 2, though §6.1 assumes a "top-right avatar" for navigation |
| `LastLoginAt` | UTC timestamp, set on every successful login |
| `Created` | Account creation timestamp |

US-06 AC1 says "all 5 fields are displayed correctly". Either the FRS adopts these three, or the
response is trimmed, or the frontend ignores them. `PictureUrl` is the one worth a decision — it is
live data with a natural place in the UI that v1.1 simply does not mention.

---

## US-06 — View personal profile information

**Code:** `UserController` · `UserService` · `UserResponse` · `ClaimsPrincipalExtensions` · tests tagged `[Trait( "Story", "US-06" )]`

**Verdict: Partial.** The endpoint exists and is correctly scoped. The field set differs.

### What exists

```csharp
[HttpGet( "me" )]
public async Task<IActionResult> GetMeAsync()
{
  var user = await userService.GetByIdAsync( User.GetUserId() ) ;
  return Ok( new UserResponse( user ) ) ;
}
```

The caller id comes from `User.GetUserId()`, which reads the `sub` claim — so a user can only ever
fetch themselves. There is no `GET /api/users/{id}` for non-admins. US-06's business rule "a user can
only view their own profile" is structurally enforced, not checked.

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · all 5 fields displayed | **Partial** | `Name` is missing; `Role` is a different concept; three unlisted fields are returned |
| AC2 · Role read-only, not clickable | **Met in substance** | `IsAdmin` is not writable through any `/me` route. Changing it needs `PATCH /api/users/{id}` under the `Admin` policy |
| AC3 · Display name reflects registration or last edit | **Met** | With one wrinkle — see below |
| AC4 · Tele ID blank when unset, not an error | **Met** | `long?`, serialised as `null` |
| AC5 · unauthenticated → redirected to Login | **Frontend** | Backend returns 401 via the class-level `[Authorize]`, which is the right input to that redirect |

### The wrinkle in AC3

A user's edited display name does not survive their next login. `LoginWithGoogleAsync` overwrites it
from the Google profile on every re-authentication:

```csharp
user.DisplayName = payload.Name ?? user.DisplayName ;
```

So "Display name reflects what was set during registration or **last edited**" holds until the token
expires and the user signs in again, at which point Google's value wins. Under v1.1, where Display
name is a user-owned field edited from Profile, this is a bug waiting to be reported. It is
independent of the authentication decision and could be fixed on its own — set `DisplayName` from
Google only when creating the user, not when updating one.

### Business rules

| Rule | Status |
|---|---|
| A user can only view their own profile | **Met** for `/me`. But `GET /api/users` returns the full `UserResponse` for **every** user — an admin capability v1.1 §2.2 does not account for ([cross-cutting §2](cross-cutting.md#2--two-different-admins)) |
| Role derived from the active workspace | **Absent** — no workspace, and no "active workspace" concept to derive from |

"Active workspace context" is a new idea with consequences beyond this module: with a user in several
workspaces, something has to hold which one is current — a client-side selection sent per request, or
server-side state. v1.1 does not say which, and Module 2 is where the question first surfaces.

---

## US-07 — Edit personal profile information

**Code:** `UserController` · `UserService` · `UpdateMeRequest` · `AppUser` · tests tagged `[Trait( "Story", "US-07" )]`

**Verdict: Partial.** One of the four editable fields is editable as specified, one is editable
through different endpoints, one does not exist, and one is blocked on the authentication decision.

### Field by field

| Field | v1.1 | Today |
|---|---|---|
| Name | Editable | **No column** |
| Email | Editable, with a uniqueness check before save | **Not editable.** It is the Google identity and half the login lookup (`GoogleSub == sub \|\| Email == email`) |
| Display name | Editable | ✓ `PATCH /api/users/me` — the only field it accepts |
| Tele ID | Editable, clearable | ✓ but via `PUT` / `DELETE /api/users/me/telegram-id` |

### Acceptance criteria

| AC | Status | Note |
|---|---|---|
| AC1 · edit Name, save, shown immediately | **Absent** | No `Name` column |
| AC2 · email already used → error, save blocked | **Absent** | No email-edit path. The unique index exists, so a 409 would surface via `ConflictException` if one were added |
| AC3 · clear Tele ID, saved empty, no error | **Met** | `DELETE /api/users/me/telegram-id`, a different call than v1.1 implies |
| AC4 · Cancel reverts all fields | **Frontend** | No backend involvement |
| AC5 · updated Display name reflected everywhere | **Met** | Display name is always read through the `AppUser` navigation — member lists use `.Include( a => a.Members ).ThenInclude( m => m.AppUser )` and read `m.AppUser.DisplayName`. Nothing is denormalised, so there is no propagation step to get wrong |
| AC6 · Role not changeable from the profile form | **Met** | `SetAdminAsync` is a separate admin-only route |
| AC7 · Name or Display name empty → validation error | **Conflict** | The opposite holds: `UpdateMeRequest` is `[MaxLength(256)] string? DisplayName` with no `[Required]`, so sending `null` **clears** the display name and returns 200. And there is no `Name` |

AC5 is worth noting as a genuine structural win, and it also answers an open question v1.1 leaves
pending. **Appendix A.2-4** asks whether a Display name update applies retroactively to past
transactions, and marks it "to be decided before Module 5 is written". The current design has already
decided: names are never copied into transaction rows, so an update is retroactive everywhere by
construction. Nothing needs building; the FRS can close A.2-4 as answered.

### Business rules

| Rule | Status |
|---|---|
| Name and Display name cannot be saved empty | **Conflict** — `DisplayName` is nullable in the schema *and* clearable through the API: `PATCH /api/users/me` with `displayName: null` succeeds and blanks it |
| Email unique after change | **N/A** — no change path. The index would enforce it (409) |
| Tele ID can be cleared | **Met** |
| Changing email does not require re-authentication | **Moot** — under Google SSO the email is not user-settable |
| A user cannot edit another user's profile | **Met** — `/me` routes take the caller id from the token; the `{id}` routes are admin-only |

### The contract question

v1.1 describes one edit form with a Save button: four fields, one commit, one "Profile updated"
confirmation. Today that is up to three requests — `PATCH /api/users/me` for the display name, and
`PUT` or `DELETE` on the telegram-id route — with no transaction across them. A user who changes both
fields and hits a failure on the second gets a half-saved profile.

Consolidating into one `PATCH /api/users/me` that accepts every editable field is a small change and
the right one. It supersedes the v1.0 story "US-07 — Link / unlink my Telegram ID", which shipped
Tele ID as a standalone capability; whether the dedicated routes are retired or kept as an alias is a
decision to record.

### To implement as written

1. **Blocked on [cross-cutting §3](cross-cutting.md#3--authentication-two-incompatible-models)** for
   the Email field. Editable email is coherent only once the credential model is settled — under
   Google SSO it is not the user's to change.
2. `Name` column on `AppUser`; `DisplayName` becomes non-nullable in the schema **and** `[Required]`
   on `UpdateMeRequest`, with a backfill for rows where Google supplied no name.
3. Widen `UpdateMeRequest` and `PATCH /api/users/me` to accept `Name`, `DisplayName` and `TelegramId`
   in one call, keeping the 409 on a Tele ID already claimed by another user.
4. Stop overwriting `DisplayName` from the Google profile on re-login (the AC3 wrinkle above) — worth
   doing regardless of every other decision here.
5. Decide `PictureUrl`, `LastLoginAt` and `Created`: adopt into the FRS, or drop from the response.

### Out of scope, correctly

v1.1's closing note excludes password change from this module. That holds trivially today — there is
no password. If the credential model changes it comes back, and the note's suggested location
("a dedicated Change password form in a future iteration") is the right place for it.
