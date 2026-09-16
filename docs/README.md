# Fiolyn documentation

Four layers. Each answers one question. When two say different things, the list under
[Precedence](#precedence) decides which wins.

| Layer | Location | Answers | Tense | Changes when |
|---|---|---|---|---|
| **Rules** | [`rules/`](rules/) | *How to build it* — invariants, patterns, pitfalls | Imperative | A project-wide pattern changes |
| **Reference** | [`reference/`](reference/) | *What it does* — behaviour, access rules, semantics | Present | Behaviour changes |
| **Decisions** | [`decisions/`](decisions/) | *Why* — context and trade-offs, at a point in time | Past | Never (supersede instead) |
| **Guides** | [`guides/`](guides/) | *How to run it* — setup, deploy, debug | Imperative | Tooling or deployment changes |

`.claude/CLAUDE.md` sits above all four as the index an agent reads first.

## Precedence

When sources disagree, the higher entry wins:

1. **[`reference/openapi.json`](reference/openapi.json) and the code** — for routes, verbs, status
   codes and DTO shapes. Generated from the running app; it cannot drift.
2. **[`rules/`](rules/)** — for implementation patterns and invariants.
3. **[`reference/*.md`](reference/)** — for behaviour and semantics.
4. **[`decisions/`](decisions/)** — *never* authoritative on current state. An ADR records what was
   decided and why, as of its date. If it disagrees with layers 1–3, the ADR is the stale one.

## Two standing rules

**One fact, one place.** A reference doc that needs a rule links to it rather than restating it.
Every file under `reference/` opens with a pointer — this template goes at the top of
`reference/<topic>.md`:

```markdown
> **Canonical rules:** [rules/<topic>.md](../rules/<topic>.md)
```

The rule files point back the same way, so each pair states its fact once and links to the other.

Restating a rule creates a second copy that will drift from the first. This is exactly how the
previous `docs/` ended up with three different answers to "does `/balance-adjustments` exist?".

**No frozen counts.** Never write "154 tests pass", "31 list endpoints" or "23 REST endpoints".
Those are true for one commit and wrong forever after. Write "every list endpoint" or point at the
generated artifact.

## Map

### [`rules/`](rules/) — how to build it

| File | Covers |
|---|---|
| [`ef-pitfalls.md`](rules/ef-pitfalls.md) | Tracking, includes vs projections, query filters, migrations |
| [`controllers.md`](rules/controllers.md) | Thin controllers, response DTOs, refetch-through-read, Telegram 200 |
| [`access-control.md`](rules/access-control.md) | Ownership and membership filtering, admin controller split |
| [`auth.md`](rules/auth.md) | Signing key, claim constants, token staging, caller id |
| [`transactions.md`](rules/transactions.md) | Where a leaf goes, balance writes, the `Update()` prohibition |
| [`transactions-shares.md`](rules/transactions-shares.md) | Child wiring, integer split, cast-include translation |
| [`counterparty.md`](rules/counterparty.md) | Resolver usage, repoint-vs-rename, expression index |
| [`pagination.md`](rules/pagination.md) | Where to apply, the stable-ordering invariant |
| [`serialization.md`](rules/serialization.md) | Newtonsoft vs System.Text.Json, UTC dates, enums, schema ids |
| [`testing.md`](rules/testing.md) | Mocking `Query()`, SUT setup, claims, paged fixtures |
| [`deployment.md`](rules/deployment.md) | No HTTPS redirection, middleware order, no startup migrations |

### [`reference/`](reference/) — what the system does today

| File | Covers |
|---|---|
| [`openapi.json`](reference/openapi.json) | Every route, verb, status code and DTO shape. **Generated — do not hand-edit.** |
| [`data-model.md`](reference/data-model.md) | Entities, keys, foreign keys, delete behaviours, soft-delete filters |
| [`errors.md`](reference/errors.md) | `ProblemDetails` shape, exception→status map, validation-error shape |
| [`auth.md`](reference/auth.md) | Google SSO, JWT issue/rotation, whitelist, admin bootstrap |
| [`accounts.md`](reference/accounts.md) | Spend accounts, broker accounts, membership, the linking FK |
| [`transactions.md`](reference/transactions.md) | Seven leaves, sign rules, atomic balance writes, shares |
| [`counterparties.md`](reference/counterparties.md) | Contact list, resolution, obligations rollup |
| [`categories.md`](reference/categories.md) | Admin-managed lookup bound to one kind |
| [`telegram-ingest.md`](reference/telegram-ingest.md) | Webhook path, sender resolution, `stock` command grammar |

### [`decisions/`](decisions/) — why

Numbered, dated, immutable. See [`decisions/README.md`](decisions/README.md) for the index.

### [`guides/`](guides/) — how to run it

| File | Covers |
|---|---|
| [`local-development.md`](guides/local-development.md) | Prerequisites, running, migrations, webhook testing |
| [`configuration.md`](guides/configuration.md) | Every config key, local and Northflank |
| [`deployment.md`](guides/deployment.md) | CI/CD pipeline, Northflank, the migration step |

### Not documentation

`planning/` at the repo root holds transient working material — FRS source extracts and
requirements analysis. It is deliberately outside the four layers and outside `docs/`: it is
input to decisions, not a record of them. Nothing in `docs/` should link to it as a source of
truth.

## Regenerating `openapi.json`

```bash
cd Fiolyn && dotnet run --launch-profile http    # terminal 1
curl -s http://localhost:5027/swagger/v1/swagger.json > docs/reference/openapi.json
```

Requires `Fiolyn/appsettings.Development.json` (see [`guides/configuration.md`](guides/configuration.md)) —
`Jwt:SigningKey` is mandatory and the app fails fast at startup without it.

Regenerate whenever a change adds, removes or renames an endpoint, or alters a request/response
record.
