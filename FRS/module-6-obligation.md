# Module 6 — Obligation

v1.1 §3.1: *"Loan tracking, dashboard, counterparty management"* — against the backend at `6c3c953`.

> **Verdict: Met** at capability level. §3.1 names three things and all three exist. The open
> questions are about what the workspace model does to them, not about building them.

v1.1 marks this module `⏳ Pending` and carries no stories for it, so this is a capability
comparison rather than an acceptance-criterion one — see
[the scope note](README.md#read-this-first--what-v11-actually-contains).

---

## Built

| §3.1 says | Today |
|---|---|
| Counterparty management | `Counterparty`, a **per-user private contact list** with full CRUD at `/api/counterparties`. Per-user case-insensitive uniqueness, enforced by a Postgres expression index `UNIQUE ("AppUserId", LOWER("Name"))` |
| Loan tracking | `LendTransaction` and `BorrowingTransaction`, each with an optional `DueDate` and a required `CounterpartyId` |
| Dashboard | `GET /api/obligations` — per counterparty: **receivable** (sum of lend magnitudes), **payable** (sum of borrow magnitudes), **net**. Rows where both are zero are filtered out |

Behaviour reference: [`reference/counterparties.md`](../docs/reference/counterparties.md).

---

## Two behaviours to carry into any later spec

Neither is obvious, and both are easy to specify backwards.

**The write contract is name-driven, not id-driven.** Lend and borrow requests carry
`counterpartyName` as a string, and the service resolves or creates the contact in the same save.
This is deliberate — [ADR 0008](../docs/decisions/0008-counterparty-entity-over-free-text.md) — so
that no client needs a contact picker before it can record a loan, including the Telegram path,
which has no UI at all. A failed transaction write rolls the new contact back with it, so there are
no orphans.

**Sending a different name on an existing lend repoints that row to a different contact; it does not
rename the original.** Renaming everywhere is `PUT /api/counterparties/{id}`. Getting this backwards
would silently rewrite every other transaction pointing at the contact.

Two smaller facts worth keeping:

- Share children are ordinary lend/borrow rows carrying a `ParentTransactionId`, so they are already
  counted in the rollup. There is no second source to reconcile — that was the point of
  [ADR 0007](../docs/decisions/0007-shares-as-paired-lend-borrow-rows.md).
- Soft-deleting a counterparty fails with 400 while any active lend, borrow or share row still
  references it.

---

## Questions a later spec must answer

- **Do counterparties stay private under a workspace?** Every obligation query is scoped by
  `c.AppUserId == userId`, and because contacts are private there is deliberately **no admin twin**
  of this endpoint. If contacts become workspace-shared, the expression index changes, every query
  scope changes, and the Admin's "edit any transaction in the workspace" right starts reaching into
  another member's contact list.

  There is also a subtlety that will bite: spend-account co-members can already read a counterparty's
  *name* through the eager-loaded navigation on a shared transaction, while only the owner can rename
  or delete it. That split is why the `Counterparty` soft-delete filter is `!IsDeleted` only and
  deliberately omits `!c.AppUser.IsDeleted` — adding it would null out those eager-loaded navs and
  crash the response builder. Any change to contact scoping has to preserve that.

- **"Dashboard" here is not Module 7's dashboard.** §3.1 uses the word for the obligations rollup;
  §6.1 uses it for the post-onboarding landing screen. Two different screens reading different data —
  worth disambiguating in the spec before someone builds one and thinks they built both.

- **Does the rollup scale to a workspace?** It is computed in memory and paged with the synchronous
  `.ToPagedResult(pagination)` overload rather than being forced into SQL — fine at current volumes,
  and the thing to revisit if a workspace multiplies row counts.

- **Are obligations per user or per workspace?** Today "what am I owed" is strictly personal. In a
  household workspace, "what does this household owe" is a plausible and different question, and
  v1.1 does not say which one Module 6 answers.

---

**Previous:** [Module 5 — Transaction](module-5-transaction.md) ·
**Next:** [Module 7 — Report / Dashboard](module-7-report-dashboard.md) ·
**Index:** [README](README.md)
