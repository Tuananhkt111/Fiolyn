# Module 4 — Category

v1.1 §3.1: *"Default and custom transaction categories"* — against the backend at `6c3c953`.

> **Verdict: Partial.** A category system exists and is well-defined. The word **custom** names
> something that does not.

v1.1 marks this module `⏳ Pending` and carries no stories for it, so this is a capability
comparison rather than an acceptance-criterion one — see
[the scope note](README.md#read-this-first--what-v11-actually-contains).

---

## Built

`TransactionCategory` — a `Name` (max 50) and a `Kind`
(`Income | Expense | Lend | Borrowing | Transfer | Adjustment | Stock`), with:

- A **unique index on `(Kind, Name)`** — the same name may be reused across kinds ("Other" for both
  Income and Expense), but not twice within one kind.
- **`Kind` is immutable after create.** Only `Name` can be updated; changing a kind would silently
  invalidate every transaction already tagged with it.
- **Kind matching enforced on every transaction write**, through the shared
  `TransactionCategoryGuard.EnsureKindMatchesAsync` — a mismatched `CategoryId` is a 400.
- An optional FK from `TransactionBase` with `SetNull` on delete, so retiring a category leaves the
  transaction history intact.

Access: reads are open to every authenticated user (clients need the list to render a picker);
writes are admin-only, through a separate `AdminTransactionCategoryController` at
`/api/admin/transaction-categories`. Both list endpoints accept an optional `?kind=` filter and
return a `PagedResult<…>`.

Behaviour reference: [`reference/categories.md`](../docs/reference/categories.md).

---

## The gap

**Every category is global and admin-managed.** There is no per-user or per-workspace category, and
no way for a non-admin to create one. §3.1's "Default **and custom**" implies two tiers; only the
first exists.

---

## Questions a later spec must answer

- **What is a custom category scoped to — a user or a workspace?** The workspace answer is the
  consistent one given the rest of v1.1, and it means `TransactionCategory` gains a nullable
  `WorkspaceId`, with `null` meaning "default, global".

- **What happens to the `(Kind, Name)` unique index?** It has to become `(WorkspaceId, Kind, Name)`,
  or a workspace cannot create a category whose name an admin already used. That is a migration on a
  table the whole ledger points at.

- **Who seeds the defaults?** There is no seeding code today — categories are inserted through the
  admin API by hand. "Default categories" implies a seed, which means either a migration, a startup
  path (note that [ADR 0004](../docs/decisions/0004-migrations-never-run-on-startup.md) rules out
  running migrations at startup), or a per-workspace copy made at creation time. The last is the
  only one that lets a workspace then edit its own defaults.

- **Does the `Kind` binding survive?** It is what makes the picker correct per transaction type, and
  it is the least obvious thing about the current model. A fresh spec that omits it will produce
  categories attachable to the wrong kind of transaction, and the guard that currently prevents that
  will have nothing to check.

- **Who may manage a workspace's custom categories?** Presumably the Admin, by analogy with wallets
  — but that is the same per-workspace role that does not exist yet
  ([cross-cutting §2](cross-cutting.md#2--two-different-admins)).

---

**Previous:** [Module 3 — Spend Account](module-3-spend-account.md) ·
**Next:** [Module 5 — Transaction](module-5-transaction.md) ·
**Index:** [README](README.md)
