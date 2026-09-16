# Fiolyn

A personal-finance API with two front doors:

- **Web REST API** — spend accounts, broker accounts and seven kinds of transaction, behind Google
  SSO and Fiolyn-signed JWTs.
- **Telegram ingest** — a webhook that resolves the sender to a user, records `stock buy/sell/div`
  commands as transactions, and files everything else to Notion.

Both write to the same ledger, and every money movement adjusts a spend-account balance atomically.

## Stack

C# 14 on .NET 10 · EF Core 10 · Neon (PostgreSQL) · Northflank (Docker) · GitHub Actions → GHCR ·
Notion API · Telegram Bot API · Google Sign-In · xUnit · NSubstitute · FluentAssertions

## Layout

```
Fiolyn/          application
Fiolyn.Tests/    unit + integration tests
docs/            rules, reference, decisions, guides (see below)
.claude/         CLAUDE.md (the index agents read first) + skills
planning/        transient working material — not documentation
```

## Quick start

```bash
cd Fiolyn
dotnet ef database update     # migrations are NOT applied on startup
dotnet run                    # http://localhost:5027, Swagger at /swagger
```

Requires `Fiolyn/appsettings.Development.json` — see
[docs/guides/configuration.md](docs/guides/configuration.md).

```bash
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj
```

## Documentation

[**docs/README.md**](docs/README.md) is the map. Four layers, each with one job:

| Looking for | Go to |
|---|---|
| Every route and request shape | [docs/reference/openapi.json](docs/reference/openapi.json) — generated |
| What the system does | [docs/reference/](docs/reference/) |
| Why it's built this way | [docs/decisions/](docs/decisions/) |
| How to run, configure, deploy it | [docs/guides/](docs/guides/) |
| How to write code for it | [docs/rules/](docs/rules/) |

When two sources disagree, [docs/README.md](docs/README.md#precedence) says which wins.

## Deployment

Push to `master` → GitHub Actions builds and pushes the image → Northflank restarts.
Schema changes need `dotnet ef database update` run **before** the new image boots —
[docs/guides/deployment.md](docs/guides/deployment.md).
