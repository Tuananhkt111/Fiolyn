# Module 7 — Report / Dashboard

v1.1 §3.1: *"Charts, filters, balance summaries"* — against the backend at `6c3c953`.

> **Verdict: Absent.** The only module with no implementation at all, and the largest greenfield in
> the programme.

v1.1 marks this module `⏳ Pending` and carries no stories for it, so this is a capability
comparison rather than an acceptance-criterion one — see
[the scope note](README.md#read-this-first--what-v11-actually-contains).

It is also a **dependency of Module 1**: §6.1 lists the Dashboard as "specified with Module 7", and
US-03's onboarding flow redirects to it on success. Whatever the Dashboard turns out to be, US-03
cannot be finished without it existing in some form.

---

## What exists to build on

| | |
|---|---|
| Raw data | `GET /api/transactions` — the unified read across all seven leaves, with the union access predicate |
| Aggregation precedent | `GET /api/obligations` is the only aggregate endpoint in the system; it computes **in memory**, not in SQL |
| Balances | `SpendAccount.Balance`, always current by construction, since every movement writes it atomically |
| Category tagging | `TransactionBase.CategoryId`, optional, kind-matched |
| Pagination | `PagedResult<T>` with a stable-ordering invariant on every list ([`rules/pagination.md`](../docs/rules/pagination.md)) |

---

## What is missing

- No aggregation endpoint of any kind — no totals by period, by category, by account or by member.
- No date-range, category or account filters on `GET /api/transactions`. Every list endpoint today
  is offset-paged and, apart from `?kind=` on categories and `?userId=` on the admin account
  endpoints, unfiltered.
- No chart-shaped response anywhere; nothing returns a time series or a bucketed total.
- No materialised or cached summary. Every number would be computed per request.

---

## Questions a later spec must answer

- **Where does aggregation run?** The obligations rollup computes in memory over the caller's rows.
  That does not generalise to "every transaction in a workspace over a year" — reporting wants SQL
  `GROUP BY`, which is a different pattern from anything in the codebase today and should be a stated
  decision rather than something a first implementation settles by accident.

- **What is the reporting scope?** Per wallet, per workspace, or across every workspace a user
  belongs to? This is the first place the "active workspace" question has a visible consequence — see
  [`module-2-profile.md`](module-2-profile.md#business-rules), where it first surfaces as "Role is
  derived from the active workspace".

- **Do reports respect transaction ownership?** If a Member may only *edit* their own transactions,
  may they *see* everyone's in a report? v1.1 §2.2 implies yes — "view shared wallets" is a Member
  right and a report reads the same data — but given how much of US-05 turns on ownership, it should
  be explicit.

- **Does the report include stock positions?** Today a `BrokerAccount` has no balance and no position
  tracking; holdings would have to be derived from the buy/sell history, including the
  fees-inclusive trade totals that are deliberately *not* re-derived from `qty × price`. That is a
  real feature, not a report.

- **Which currency and period conventions apply?** Money is `long` whole VND with no subunit
  ([ADR 0001](../docs/decisions/0001-money-as-long-not-decimal.md)), and transaction dates are UTC
  `DateTime` ([`rules/serialization.md`](../docs/rules/serialization.md)). A "monthly summary" needs
  a stated timezone, or month boundaries will be wrong for every user in UTC+7.

---

**Previous:** [Module 6 — Obligation](module-6-obligation.md) ·
**Index:** [README](README.md)
