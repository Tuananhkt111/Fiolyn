# InitProject.md — Fiolyn (.NET)

## 1. Project Overview

Fiolyn is a single .NET 8 web API service (MVC Controllers) that:
- Receives messages from a Telegram bot via webhook
- Resolves the sender's Telegram user ID to an `AppUser` via `AppUser.TelegramId` (no match → skip)
- Saves the message as a new entry in a Notion database (or parses `stock buy/sell/div` into a scoped `StockTransaction`)

**Stack:**
- Language: C# (.NET 8 LTS)
- Hosting: Northflank (Docker, always-on free tier)
- CI/CD: GitHub Actions → GHCR → Northflank deploy hook
- Database: Neon (PostgreSQL) via Entity Framework Core (Code First)
- Message store: Notion API
- Bot: Telegram Bot API (webhook)

**Repo:** `Tuananhkt111/Fiolyn` (private)
**Live URL:** assigned by Northflank after first deploy

---

## 2. Local Development Setup

### Prerequisites
- .NET 8 SDK
- Docker (optional, for local container testing)
- EF Core CLI: `dotnet tool install --global dotnet-ef`

### Environment variables
.NET does not auto-load `.env` files. Set variables in your shell before running:

```bash
# Windows (PowerShell)
$env:TELEGRAM_BOT_TOKEN="..."
$env:NOTION_API_KEY="..."
$env:NOTION_DATABASE_ID="..."
$env:DATABASE_URL="postgresql://..."

# macOS/Linux
export TELEGRAM_BOT_TOKEN="..."
export NOTION_API_KEY="..."
export NOTION_DATABASE_ID="..."
export DATABASE_URL="postgresql://..."
```

### Apply migrations locally
Requires `DATABASE_URL` to be set in your shell (see Environment variables above).

To apply all existing migrations to your local database:

```bash
cd Fiolyn
dotnet ef database update
```

To create a new migration after changing an entity:

```bash
dotnet ef migrations add <DescriptiveName> --output-dir Data/Migrations
dotnet ef database update
```

### Run locally
```bash
cd Fiolyn
dotnet run
```

### Test the webhook locally
`dotnet run` starts on port 5027 by default — check console output for the exact URL.

Your Telegram ID must be linked to an `AppUser` before testing. Log into the web app once (Google SSO with a whitelisted `AllowedEmail`) to create your `AppUser` row, then either call the API (`PUT /api/users/me/telegram-id` with body `{"telegramId": YOUR_TELEGRAM_ID}`) or run SQL directly:

```sql
UPDATE "AppUser" SET "TelegramId" = YOUR_TELEGRAM_ID WHERE "Email" = 'your.email@example.com';
```

Then send a test request:

```bash
curl -X POST http://localhost:5027/api/telegram \
  -H "Content-Type: application/json" \
  -d '{"message":{"text":"test","from":{"id":YOUR_TELEGRAM_ID},"chat":{"id":1}}}'
```

Expected: `{"ok":true}` and a new row in Notion. If you see `{"ok":true,"skipped":true}`, no `AppUser` has that `TelegramId`.

---

## 3. File Structure

```
fiolyn/
├── .github/
│   └── workflows/
│       └── deploy.yml                  # CI/CD pipeline
├── src/
│   └── Fiolyn/
│       ├── Fiolyn.csproj
│       ├── Program.cs                  # Service registration, middleware, endpoint mapping
│       ├── Controllers/
│       │   └── TelegramController.cs   # POST /api/telegram
│       ├── Data/
│       │   ├── AppDbContext.cs         # EF Core DbContext
│       │   └── Migrations/             # Auto-generated — do not edit manually
│       ├── Entities/
│       │   └── AllowedUser.cs          # EF Core entity
│       └── Services/
│           └── NotionService.cs        # Notion HTTP helper
├── Dockerfile
├── .dockerignore
├── .editorconfig
├── .env.example
├── .gitignore
└── CLAUDE.md
```

**Namespace rules:**
- Controllers → `namespace Fiolyn.Controllers;` — add `using Fiolyn.Data; using Fiolyn.Entities; using Fiolyn.Services;` as needed
- Entities → `namespace Fiolyn.Entities;`
- DbContext → `namespace Fiolyn.Data;`
- Services → `namespace Fiolyn.Services;`
- `Program.cs` → no namespace (top-level statements) — add `using Fiolyn.Data; using Fiolyn.Services;` at the top

**Folder rules:**
- New controllers go in `Controllers/`, one file per controller
- New EF Core entities go in `Entities/`, one file per entity
- `AppDbContext.cs` and `Migrations/` always stay in `Data/`
- New service classes go in `Services/`
- Never place business logic inside `Program.cs` — keep it as wiring only

---

## 4. Code Style

### 4.1 Indentation and Formatting
- **Indentation**: Use 2 spaces. Do not use tabs.
- **Line length**: Maximum 120 characters per line. Break lines appropriately when exceeding this limit.
- **Blank lines**: Insert one blank line between related code blocks for visual separation. Redundant blank lines are prohibited.
- **Braces**: Opening brace `{` on a new line for types, methods, and lambdas.
- **Spaces**: Inside method call and declaration parentheses, and inside control flow parentheses.

```csharp
// correct
public class TelegramController : ControllerBase
{
  private readonly AppDbContext _db;

  public TelegramController( AppDbContext db )
  {
    _db = db;
  }
}

// wrong — brace on same line, no spaces inside parens, tab indentation
public class TelegramController : ControllerBase {
        private readonly AppDbContext _db;
}
```

### 4.2 Naming Conventions

| Target | Convention | Example |
|---|---|---|
| Classes | PascalCase | `NotionService` |
| Methods | PascalCase | `AppendAsync` |
| Properties | PascalCase | `TelegramId` |
| Public fields | PascalCase | `CreatedAt` |
| Private fields | _camelCase | `_databaseId` |
| Local variables | camelCase | `fromId`, `isAllowed` |
| Enumerations | PascalCase | `MessageType` |
| Compile-time constants | `const` + PascalCase | `const string DefaultLabel` |
| Runtime constants | `static readonly` + PascalCase | `static readonly TimeSpan Timeout` |

### 4.3 Additional Rules
- Prefer `var` when the type is obvious from the right-hand side
- Use `async`/`await` for all I/O operations — never `.Result` or `.Wait()`
- Use primary constructors where it reduces boilerplate (C# 12+)
- Prefer `record` for DTOs and request/response models
- One class per file. File name must match class name.

---

## 5. Database (EF Core Code First)

### The Code First rule
**Always define schema in C# entities, never in raw SQL.**
The database is the output of the code — not the other way around.

### Adding or changing schema
1. Modify or add the entity class (e.g. `AllowedUser.cs`)
2. Generate a migration:
   ```bash
   dotnet ef migrations add <MigrationName> --output-dir Data/Migrations
   ```
3. Verify the generated migration file in `Migrations/` before committing
4. Apply locally:
   ```bash
   dotnet ef database update
   ```
5. Commit the migration files — they are part of the source code
6. On deploy, migrations apply automatically via `db.Database.Migrate()` in `Program.cs`

### Rules
- **Never** use `db.Database.EnsureCreated()` — it bypasses migrations
- **Never** write raw `CREATE TABLE` or `ALTER TABLE` SQL to manage schema
- **Never** edit generated migration files manually
- **Never** delete a migration that has already been applied to any environment
- Migration names must be descriptive: `AddTransactionsTable`, `AddLabelToAllowedUser`
- Always review the generated migration before committing — EF Core can generate destructive changes

---

## 6. Deployment

### How it works
Push to `main` → GitHub Actions builds the Docker image → pushes to GHCR → triggers Northflank restart via API → Northflank pulls and runs the new image.

No manual steps are needed after the initial setup.

### Secrets
All secrets live in **Northflank environment variables**. Never in code, `.env`, or committed files.

| Secret | Where |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Northflank env vars |
| `NOTION_API_KEY` | Northflank env vars |
| `NOTION_DATABASE_ID` | Northflank env vars |
| `DATABASE_URL` | Northflank env vars |
| `NORTHFLANK_API_TOKEN` | GitHub Actions secret |

### Checking a failed deploy
1. GitHub Actions tab → check build/push step
2. Northflank dashboard → service → **Logs** tab
3. Startup migration errors appear in Northflank logs on first boot

---

## 7. What NOT To Do

- **Don't put logic in `Program.cs`** — it is wiring only. Controllers and services hold logic.
- **Don't use `.Result` or `.Wait()`** on async calls — always `await`.
- **Don't use `EnsureCreated()`** — it skips migrations and will corrupt the schema history.
- **Don't edit migration files** after they have been generated — regenerate instead.
- **Don't delete applied migrations** — roll forward with a new migration if you need to undo.
- **Don't commit `.env`** or any file containing real secrets.
- **Don't hardcode credentials or tokens** anywhere in source code.
- **Don't return 5xx on Telegram webhook errors** — Telegram will retry repeatedly. Always return 200.
