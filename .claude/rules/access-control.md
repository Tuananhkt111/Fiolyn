# Access Control Rules

Read this when touching ownership / membership filtering on resources, or adding admin endpoints.

## Patterns

- **User-owned resources (direct `AppUserId` FK):** enforce ownership in the *service*, not the controller. Service methods are user-scoped: `GetAllForUserAsync(userId)`, `GetByIdForUserAsync(id, userId)`, `SoftDeleteAsync(id, userId)`. Filter by `AppUserId`; treat "not owned by caller" as 404. Controllers pipe `User.GetUserId()` into every call.

- **Shared / multi-member resources (no `AppUserId`, joined via `Xxx`/`XxxMember`):** membership = access. User-scoped reads filter:
  ```csharp
  .Where(r => r.Members.Any(m => m.AppUserId == userId))
  ```
  "Not a member" → 404, same as the single-owner case. Don't write `!m.AppUser.IsDeleted` — the global query filter already drops soft-deleted users transitively.

- **Member-list management endpoints** live on the resource controller and require the caller to be a member. `GetMembersAsync` / `AddMemberAsync` / `RemoveMemberAsync` bake the caller-membership check *into* the same query that fetches the data — one round-trip, not a separate `Ensure...` precondition. `GetMembersAsync` uses `.Include(a => a.Members).ThenInclude(m => m.AppUser)` so the response DTO can read `m.AppUser.Email` / `DisplayName`. `AddMemberAsync` / `RemoveMemberAsync` only need FK columns and stay on a `Select` projection. See `SpendAccountService` / `BrokerAccountService`.

- **Transitive access for child resources:** when a child (e.g. `StockTransaction`) is owned by a shared parent (e.g. `BrokerAccount`), access flows through the parent's membership. Filter:
  ```csharp
  .Where(c => c.Parent.Members.Any(m => m.AppUserId == userId))
  ```
  For *writes*, inline the membership filter into a single combined query that also projects whatever else the write needs (FK columns, related-entity scalars) — null result → 404 regardless of whether the row or the membership was missing. Membership-resolving subqueries belong on the *child's* service, not the parent's, to avoid DI cycles. See `StockTransactionService.{Create,Update}Async`.

## Admin endpoints

- **Cross-user admin endpoints go in a separate `Admin<X>Controller`.** Route `/api/admin/<resource>`, `[Authorize(Policy = AuthConstants.AdminPolicyName)]` at class level, *same* service interface — add admin methods (no user scoping; usually with eager-loading for owner/member enrichment). The split avoids both response-shape overloading and policy-per-action sprawl. See `AdminSpendAccountController` vs `SpendAccountController`.

- **Trusted-internal ingest bypass: name the method, don't nullable the arg.** When a background path (e.g. cron) genuinely can't identify a caller, expose a dedicated method like `CreateFromCronAsync(entity)` rather than making `userId` nullable on the user-scoped `CreateAsync`. Document as "for trusted internal callers only" in the interface XML docs. No live example currently — Telegram ingest now resolves the sender to an `AppUser` via `AppUser.TelegramId`.
