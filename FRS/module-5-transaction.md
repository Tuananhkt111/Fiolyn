# Module 5 — Transaction

v1.1 §3.1: *"Income, expense, transfer, recurring transactions"* — against the backend at `6c3c953`.

> **Verdict: Partial.** The largest implemented area in the system. §3.1's four-word description
> covers roughly half of what is built, and names one thing that does not exist at all.

v1.1 marks this module `⏳ Pending` and carries no stories for it, so this is a capability
comparison rather than an acceptance-criterion one — see
[the scope note](README.md#read-this-first--what-v11-actually-contains).

---

## Built — seven kinds, one ledger

An abstract `TransactionBase` with seven table-per-type leaves
([ADR 0006](../docs/decisions/0006-tpt-transaction-hierarchy.md)). The signed `Amount` on the base is
what moves `SpendAccount.Balance`:

| Leaf | Stored sign | Named in §3.1 |
|---|---|---|
| Income | + | ✓ |
| Expense | − | ✓ |
| Transfer | source −, destination + | ✓ |
| Lend | − | ✗ |
| Borrowing | + | ✗ |
| Adjustment | as supplied (signed) | ✗ |
| Stock | Buy −, Sell/Dividend + | ✗ |

**Sign is enforced server-side per leaf.** Clients send a positive magnitude and the server decides
the direction; `Adjustment` is the one leaf that accepts a signed value and therefore skips the
`[Range(1, …)]` validation.

Also built:

- **Atomic balance writes.** Every mutation goes through
  `TransactionBalanceWriter.SaveWithConcurrencyGuardAsync`: load the tracked account(s), mutate
  `Balance`, then one save that flushes the row change and the balance change together. `Balance`
  carries `[ConcurrencyCheck]`, so concurrent writers race and the loser gets 409.
- **Income/expense shares** — `shares: [{ counterpartyName, percentage }]` splits an amount between
  the caller and one auto-created child per share: a `BorrowingTransaction` under an income parent, a
  `LendTransaction` under an expense parent, with the parent absorbing rounding loss so the total is
  exact ([ADR 0007](../docs/decisions/0007-shares-as-paired-lend-borrow-rows.md)). The account
  balance moves by the full original total, once — the split only decides who owes whom.
- **A unified read** at `GET /api/transactions`, whose access predicate is a union: member of the
  row's spend account, **or** — for transfers — of the destination account, **or** — for stock rows —
  of the broker account. The destination branch is load-bearing: without it a user who only co-owns
  the destination would never see incoming transfers.
- **Admin read and delete** per leaf under `/api/admin/*-transactions`. Note there is **no admin
  update verb** on any transaction controller.
- **Telegram ingest** creating stock transactions through the same service and the same membership
  check as the REST API — it is not a privileged path.

Behaviour reference: [`reference/transactions.md`](../docs/reference/transactions.md).

---

## The gap: recurring transactions

**They do not exist.** No recurrence entity, no schedule column, no generator — and, more to the
point, **no scheduler of any kind in the deployment**: no `IHostedService`, no `BackgroundService`,
no job framework, and even migrations are deliberately never run at startup
([ADR 0004](../docs/decisions/0004-migrations-never-run-on-startup.md)).

This is the one gap across Modules 3–7 that needs **new infrastructure**, not just new code. A
recurring transaction has to actually create rows on a schedule, so the lazy-evaluation trick that
works for invite expiry ([cross-cutting §8](cross-cutting.md#8--no-scheduler-no-email-no-notifications))
does not help here. The options are a hosted service inside the Northflank container or a separate
scheduled job, which makes it a deployment decision as much as a design one
([`guides/deployment.md`](../docs/guides/deployment.md)).

Second-order questions the spec will have to answer once a scheduler exists: what happens to a
recurring series when its wallet is deleted, when its creator is removed from the workspace, or when
the generator misses a window and has to catch up.

---

## Other questions a later spec must answer

- **Creator-only editing.** US-05 already decided it (Appendix A.1-3), and `TransactionBase` has no
  column to enforce it with — `PayerId` is attribution, not authorship: nullable, `SetNull` on user
  delete, and freely settable to another user. Module 5 is where that bill comes due, across all
  seven leaf services plus the share path and the Telegram ingest. Full analysis in
  [cross-cutting §5](cross-cutting.md#5--transaction-ownership-has-no-column-to-stand-on).

- **Auto-created share children have no human author.** Under a creator-only rule, who owns them —
  the parent's creator, by inheritance? And may the counterparty on the other side of the split see
  or edit their own row? The spec has to say.

- **Appendix A.2-4 — does a display-name change apply retroactively to past transactions?** Marked
  "to be decided before Module 5 is written". It is already decided by construction: names are never
  copied into transaction rows, only read through the `AppUser` navigation, so every change is
  retroactive everywhere. See
  [`module-2-profile.md`](module-2-profile.md#us-07--edit-personal-profile-information). **A.2-4 can
  be closed as answered.**

- **Which workspace does a Telegram-created transaction belong to?** The message names a broker, not
  a workspace, and the sender may belong to several
  ([cross-cutting §9](cross-cutting.md#9--implemented-capabilities-v11-does-not-mention)).

- **Do shares survive into the target design?** v1.1 does not mention them anywhere, yet splitting a
  bill between household members is squarely inside its "family shared workspace" premise. Carry
  forward, rework, or retire — currently undecided by omission.

---

**Previous:** [Module 4 — Category](module-4-category.md) ·
**Next:** [Module 6 — Obligation](module-6-obligation.md) ·
**Index:** [README](README.md)
