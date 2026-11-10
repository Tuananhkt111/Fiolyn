# Local development

## Prerequisites

- .NET 10 SDK (pinned by `global.json`)
- EF Core CLI — `dotnet tool install --global dotnet-ef`
- A Postgres database (Neon, or local)
- `Fiolyn/appsettings.Development.json` — see [`configuration.md`](configuration.md)

Docker is optional, only for testing the container image locally.

Once per clone, run `bash scripts/setup.sh`. It points git at `scripts/hooks/`, whose pre-commit
hook refuses an edit to a merged ADR, runs docs-lint, and builds with `-warnaserror` when C# is
staged ([0014](../decisions/0014-versioned-git-hooks.md)).

## Run

```bash
cd Fiolyn
dotnet run
```

Listens on `http://localhost:5027` (the `http` launch profile). Swagger UI is served at
`http://localhost:5027/swagger` in Development only.

## Migrations

**Migrations are not applied on startup** — see
[0004](../decisions/0004-migrations-never-run-on-startup.md). Apply them yourself:

```bash
cd Fiolyn
dotnet ef database update                                     # apply pending
dotnet ef migrations add <DescriptiveName> --output-dir Data/Migrations   # create new
```

Rules that matter:

- Review the generated migration before committing — EF can generate destructive changes.
- Never edit or delete an **applied** migration. Roll forward with a new one.
- To undo an **unapplied** migration use `dotnet ef migrations remove`, not hand-deletion.
- Never use `EnsureCreated()` — it bypasses the migration history table entirely.

## Tests

```bash
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj                              # full suite
dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --filter "FullyQualifiedName~AuthServiceTests"
```

Run only the classes covering what you touched; fall back to the full suite for cross-cutting
changes (`IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes) and as the
pre-commit gate.

## Guards

Three layers check the rules that used to be instructions only:

| Layer | Where | Runs |
|---|---|---|
| CI | `.github/workflows/deploy.yml`, job `verify` | tests and docs-lint, before any deploy ([0013](../decisions/0013-deploy-gated-on-tests-and-docs-lint.md)) |
| Git | `scripts/hooks/pre-commit`, enabled by `scripts/setup.sh` | merged-ADR immutability, docs-lint, `-warnaserror` build ([0014](../decisions/0014-versioned-git-hooks.md)) |
| Claude Code | `.claude/hooks/fiolyn_hooks.py`, registered in `.claude/settings.json` | `openapi.json` edits, `decimal` money, push account, docs-lint at turn end ([0015](../decisions/0015-claude-code-guards.md)) |

After changing the Claude Code guards, run their tests:

```bash
python -I -X utf8 .claude/hooks/test_fiolyn_hooks.py
```

## Testing the Telegram webhook

You can exercise the webhook entirely locally — Telegram's servers are not involved. The payload is
just JSON you POST yourself.

### 1. Link a Telegram id to your user

The webhook only processes messages whose `from.id` matches an active `AppUser.TelegramId`, so you
need an `AppUser` first. Log into the web app once with Google SSO using an email that is in
`AllowedEmail` — that creates the row.

Then link an id, either through the API:

```bash
curl -X PUT http://localhost:5027/api/users/me/telegram-id \
  -H "Authorization: Bearer <ACCESS_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"telegramId": 12345}'
```

or directly in SQL:

```sql
UPDATE "AppUser" SET "TelegramId" = 12345 WHERE "Email" = 'your.email@example.com';
```

Any number works — it need not be a real Telegram account. The column is uniquely indexed, so
reusing an id already linked to another user returns 409.

### 2. Send a payload

```bash
curl -X POST http://localhost:5027/api/Telegram \
  -H "Content-Type: application/json" \
  -d '{"message":{"from":{"id":12345},"text":"test message"}}'
```

A plain message is filed to Notion. To exercise the stock parser instead, send text starting with
`stock ` — the broker name must resolve and you must be a member of it:

```bash
curl -X POST http://localhost:5027/api/Telegram \
  -H "Content-Type: application/json" \
  -d '{"message":{"from":{"id":12345},"text":"stock buy VNM 100 45000 4501500 broker:VPS"}}'
```

### 3. Read the result

**The endpoint always returns HTTP 200**, including for failures — Telegram retries non-2xx
responses, so the real outcome is in the body and the logs, never the status code.

Worth testing deliberately: an unlinked `from.id` (skipped), a payload with no `text` (skipped), a
payload with no `message` (skipped), and a `stock` message naming a broker you don't belong to
(error). [`../reference/telegram-ingest.md`](../reference/telegram-ingest.md) lists every outcome.

Watch the console for the matching log line:

```
info: Fiolyn.Services.External.Telegram.TelegramMessageService[0]
      Message from Telegram user 12345 saved to Notion
```

## Regenerating the API document

After adding, removing or renaming an endpoint, or changing a request record:

```bash
cd Fiolyn && dotnet run                                                   # terminal 1
curl -s http://localhost:5027/swagger/v1/swagger.json > docs/reference/openapi.json
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Startup throws `InvalidOperationException` about the signing key | `Jwt:SigningKey` missing or under 32 chars — deliberate, no fallback |
| Webhook says "User not allowed" | No active `AppUser` has that `TelegramId` |
| Stock message says "Not authorized for broker" | The broker resolved, but you are not a member of it |
| Stock message says "Invalid stock message format" | Too few values, a non-numeric field, or the broker name didn't resolve |
| Queries fail after pulling a schema change | Migration not applied — run `dotnet ef database update` |
| Notion returns 403 | Integration not shared with the target database |
| Browser calls fail with a network error, not a 401 | CORS — the origin isn't in `Cors:AllowedOrigins` |
