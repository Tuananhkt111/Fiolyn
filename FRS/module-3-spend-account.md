# Module 3 — Spend Account

v1.1 §3.1: *"Wallet management, balance adjustment, sharing"* — against the backend at `6c3c953`.

> **Verdict: Partial.** The most complete area of the system, and the one where v1.1's terminology
> diverges most. Blocked on the wallet-type question (Appendix A.2-10).

v1.1 marks this module `⏳ Pending` and carries no stories for it, so this is a capability
comparison rather than an acceptance-criterion one — see
[the scope note](README.md#read-this-first--what-v11-actually-contains). It also inherits the two
structural conflicts in [`cross-cutting.md`](cross-cutting.md): the workspace (§1) and the permission
model (§2, §5) both have to be settled before this module can be scoped, because every read here
resolves access through the same membership predicate.

---

## Built

| Capability | Where |
|---|---|
| Create, read, update, soft-delete a spend account | `POST/GET/PUT/DELETE /api/spend-accounts` |
| Types `Cash` and `Credit` | `SpendAccountType` |
| Signed balance in whole VND, may go negative | `SpendAccount.Balance`, `long`, `[ConcurrencyCheck]` |
| Sharing: list, add by email, remove | `/api/spend-accounts/{id}/members` |
| The same five for broker accounts | `/api/broker-accounts`, `/api/broker-accounts/{id}/members` |
| Balance correction | `POST /api/adjustment-transactions` — a signed amount through the ledger |
| Cross-user administration | `/api/admin/spend-accounts`, `/api/admin/broker-accounts`, both with an optional `?userId=` filter |
| Delete protection | Soft-delete fails with 400 while an active broker, transaction or transfer destination still references the account |

Behaviour reference: [`reference/accounts.md`](../docs/reference/accounts.md).

---

## The invariant a later spec must not break

**`Balance` is never written directly.** `PUT /api/spend-accounts/{id}` updates `Name` and `Type`
only. Every balance change flows through a transaction leaf, so the ledger and the balance cannot
disagree, and a manual correction is an `AdjustmentTransaction` rather than a balance edit.

v1.1 §3.1's phrase "balance adjustment" is compatible with this, and §4.1 gives `Wallet` a `Balance`
field without saying how it is written. The later spec should state explicitly that there is no
balance-edit endpoint — it is the single most likely thing for a fresh spec to reintroduce by
accident.

The other non-obvious rule, for broker accounts: **repointing a broker to a different spend account
is not retroactive.** Historical transactions stay on the old account; only future ones land on the
new one. Setting or changing the link requires membership of *both* accounts, and the re-check runs
only when the FK actually changed, so a rename skips it.

---

## Gaps and open questions

- **The wallet-type merge (Appendix A.2-10).** v1.1's one `Wallet` with `type ∈ {cash, credit,
  stock}` versus today's `SpendAccount` + `BrokerAccount`, where a broker has **no balance column of
  its own** and carries a required FK to the spend account its cash moves through. Full analysis in
  [cross-cutting §6](cross-cutting.md#6--wallet-versus-spendaccount-plus-brokeraccount). This is the
  question that decides whether Module 3 is a rewrite or an extension.

  Collapsing the two entities means absorbing that FK, which is load-bearing in three places: stock
  trades write the linked account's balance; repointing is deliberately non-retroactive; and the FK
  is `Restrict` on purpose, because cascade forms a dependency loop
  ([ADR 0010](../docs/decisions/0010-broker-spend-account-fk-restrict.md)).

- **`Wallet.Shared-with [TBD]` (§4.1)** is the per-wallet share mechanism the workspace model needs
  in order to keep private wallets private (US-04's business rules require it).
  `SpendAccountMember` is exactly that mechanism, already built — see
  [cross-cutting §1](cross-cutting.md#1--the-workspace-does-not-exist).

- **Who may create, edit and delete a wallet.** Today **any member may**, including soft-deleting the
  account outright, and any member may add or remove any other member. v1.1 §2.2 reserves all of
  that for the Admin. The only current guard is that the last member cannot be removed — delete the
  account instead.

- **Workspace ownership.** `Wallet` gains a `WorkspaceId`, and every existing account needs one
  assigned. For an account with *n* members, someone has to decide whose workspace it lands in and
  what the other *n−1* members become (Appendix A.2-8).

- **The onboarding wallet form already works.** `POST /api/spend-accounts` takes exactly the three
  fields US-03 asks for — `Name`, `Type`, `Balance` — and `Balance` has no positive constraint, so a
  zero opening balance is already accepted. The gap is only the third type: a stock wallet is a
  `BrokerAccount`, which needs an existing spend account to link to and therefore cannot be the
  first thing a new user creates. See
  [`module-1-auth-onboarding.md`](module-1-auth-onboarding.md#us-03--mandatory-wallet-creation-before-accessing-the-app).

---

**Next:** [Module 4 — Category](module-4-category.md) ·
**Index:** [README](README.md)
