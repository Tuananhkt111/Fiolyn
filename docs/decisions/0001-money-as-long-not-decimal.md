# 0001 — Money and quantities are `long`, not `decimal`

**Date:** 2026-04-23 · **Status:** Accepted

## Context

The obvious .NET default for money is `decimal`. It exists precisely to avoid binary
floating-point error on fractional currency, and most guidance recommends it without qualification.

That guidance assumes a currency with a fractional subunit. Fiolyn's primary currency is VND, which
has none — there is no "cent". Amounts are always whole units. Separately, Vietnamese exchanges
trade whole-share lots, so stock quantities are integral too.

Using `decimal` here would buy precision that no value in the domain can ever use, while costing
storage, arithmetic speed, and — more importantly — inviting rounding bugs at every boundary where
a fractional value could silently appear and then be truncated somewhere downstream.

## Decision

Every monetary and quantity field is `long`, at every layer: entity, DTO, service, and parse.
This covers balances, prices, totals, amounts and quantities.

`decimal` appears nowhere as a CLR type in `Fiolyn/`.

The one place fractional precision genuinely exists is share **percentages** (2 decimal places).
That is handled without `decimal` by scaling to integer hundredths — see
[`rules/transactions-shares.md`](../rules/transactions-shares.md).

## Consequences

- Split arithmetic stays in exact integer math. The share splitter scales percentages to hundredths
  and lets the parent absorb rounding loss, so `parent + Σchildren == original` holds exactly.
- Introducing a currency with a subunit means revisiting this decision across the whole stack, not
  changing one column. That is deliberate: it should be a conscious migration, not a silent widening.
- Reintroducing `decimal` requires a genuine sub-unit requirement and explicit confirmation.
