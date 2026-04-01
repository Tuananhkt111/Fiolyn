# Plan: Telegram → Notion via .NET on Northflank

## Context

A single .NET 8 service that handles the Telegram webhook — receives messages, checks a user whitelist, and saves to Notion. The architecture is structured from the start to support future REST API endpoints without restructuring.

Deployed as a Docker container on Northflank (always-on free tier), with CI/CD via GitHub Actions. Allowed user IDs are stored in **Neon PostgreSQL** via **Entity Framework Core**.

Neon is chosen over Supabase because its free tier **never auto-pauses**.
Northflank is chosen over Render because its free tier has **no cold starts**.

**GitHub repo:** `Tuananhkt111/Fio` (to be created)
**Northflank URL:** assigned after first deploy (e.g. `https://fio--<project>.code.run`)

---

## Stack

| Concern        | Technology                                      |
|----------------|-------------------------------------------------|
| Language       | C# (.NET 8, MVC Controllers)                    |
| Hosting        | Northflank (Docker container, always-on free)   |
| CI/CD          | GitHub Actions → GHCR → Northflank deploy hook |
| Message store  | Notion API                                      |
| Whitelist DB   | Neon (PostgreSQL) via Entity Framework Core     |
| Bot            | Telegram Bot API (webhook)                      |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                  Northflank Service                  │
│                                                      │
│  POST /api/telegram        ← Telegram Bot API        │
│  ├── check allowed_users (Neon)                      │
│  └── append to Notion                                │
│                                                      │
│  [future REST endpoints go here]                     │
└─────────────────────────────────────────────────────┘
         │                        │
         ▼                        ▼
    Neon (PostgreSQL)         Notion API
    allowed_users table       message log
```

---

## Flow

```
Telegram user sends message
    → Telegram POST /api/telegram
    → check Neon: is telegram_id in allowed_users?
    → if not → return skipped
    → call Notion API → create page
    → return ok
```

---

## File Structure

```
fio/
├── .github/
│   └── workflows/
│       └── deploy.yml                  # CI/CD: build image → push GHCR → trigger Northflank
├── src/
│   └── Fio/
│       ├── Fio.csproj
│       ├── Program.cs                  # Service registration, middleware, maps endpoints
│       ├── Controllers/
│       │   └── TelegramController.cs   # POST /api/telegram
│       ├── Data/
│       │   ├── AppDbContext.cs         # EF Core DbContext
│       │   └── Migrations/             # EF Core migration files (auto-generated)
│       ├── Entities/
│       │   └── AllowedUser.cs          # EF Core entity — whitelist
│       └── Services/
│           └── NotionService.cs        # Notion HTTP helper
├── Dockerfile
├── .dockerignore
├── .env.example
├── .gitignore
└── README.md
```

---

## Step 1 — Create GitHub Repo

1. Go to github.com → New repository → name it `Fio` → Private → Create
2. Clone locally:
   ```bash
   git clone https://github.com/Tuananhkt111/Fio.git
   cd Fio
   ```

---

## Step 2 — Initialize .NET Project

```bash
dotnet new webapi -n Fio -o src/Fio --no-openapi
cd src/Fio
```

`dotnet new webapi` generates sample files — delete them:

```bash
rm Controllers/WeatherForecastController.cs WeatherForecast.cs Fio.http
```

Create `.gitignore` and `.dockerignore` from repo root:

```bash
cd ../..
dotnet new gitignore
```

Create `.dockerignore` to prevent `bin/` and `obj/` from being copied into the Docker build context:

```bash
cat > .dockerignore << 'EOF'
**/bin
**/obj
**/.git
**/.vs
EOF
```

Install required NuGet packages:

```bash
cd src/Fio
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
```

Install EF Core CLI tools (once, globally):

```bash
dotnet tool install --global dotnet-ef
```

---

## Step 3 — `src/Fio/Entities/AllowedUser.cs`

Entity representing one permitted Telegram user.

```csharp
namespace Fio;

public class AllowedUser {
  public int Id { get; set; }
  public long TelegramId { get; set; }
  public string? Label { get; set; }      // "owner", "friend", etc.
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## Step 4 — `src/Fio/Data/AppDbContext.cs`

EF Core DbContext wired to Neon PostgreSQL.

```csharp
using Microsoft.EntityFrameworkCore;

namespace Fio;

public class AppDbContext : DbContext {
  public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

  public DbSet<AllowedUser> AllowedUsers => Set<AllowedUser>();

  protected override void OnModelCreating(ModelBuilder modelBuilder) {
    modelBuilder.Entity<AllowedUser>(e => {
      e.ToTable("allowed_users");
      e.HasKey(x => x.Id);
      e.Property(x => x.TelegramId).HasColumnName("telegram_id").IsRequired();
      e.Property(x => x.Label).HasColumnName("label");
      e.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
      e.HasIndex(x => x.TelegramId).IsUnique();
    });
  }
}
```

---

## Step 5 — `src/Fio/Program.cs`

Registers services and maps controllers. Wiring only — no logic here.

```csharp
using Fio;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
  options.UseNpgsql(Environment.GetEnvironmentVariable("DATABASE_URL")));

// Notion
builder.Services.AddSingleton(new NotionService(
  Environment.GetEnvironmentVariable("NOTION_API_KEY")!,
  Environment.GetEnvironmentVariable("NOTION_DATABASE_ID")!
));

builder.Services.AddControllers();

var app = builder.Build();

// Auto-apply migrations on startup
using (var scope = app.Services.CreateScope()) {
  var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
  db.Database.Migrate();
}

app.MapControllers();

app.Run();
```

---

## Step 6 — `src/Fio/Controllers/TelegramController.cs`

Handles the Telegram webhook.

```csharp
using Fio;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fio.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelegramController : ControllerBase {
  private readonly AppDbContext _db;
  private readonly NotionService _notion;

  public TelegramController(AppDbContext db, NotionService notion) {
    _db = db;
    _notion = notion;
  }

  [HttpPost]
  public async Task<IActionResult> Post() {
    using var reader = new StreamReader(Request.Body);
    var raw = await reader.ReadToEndAsync();
    using var doc = JsonDocument.Parse(raw);
    var body = doc.RootElement;

    // Skip non-message updates (edited messages, stickers, photos, etc.)
    if (!body.TryGetProperty("message", out var message))
      return Ok(new { ok = true, skipped = true });

    if (!message.TryGetProperty("from", out var from))
      return Ok(new { ok = true, skipped = true });

    var fromId = from.GetProperty("id").GetInt64();

    var isAllowed = await _db.AllowedUsers.AnyAsync(u => u.TelegramId == fromId);
    if (!isAllowed)
      return Ok(new { ok = true, skipped = true });

    // Skip non-text messages (stickers, photos, voice, etc.)
    if (!message.TryGetProperty("text", out var textElement))
      return Ok(new { ok = true, skipped = true });

    var text = textElement.GetString();
    if (string.IsNullOrEmpty(text))
      return Ok(new { ok = true, skipped = true });

    await _notion.AppendAsync(text);
    return Ok(new { ok = true });
  }
}
```

---

## Step 7 — `src/Fio/Services/NotionService.cs`

HTTP client that creates a new page in the Notion database.

```csharp
namespace Fio;

public class NotionService {
  private readonly HttpClient _http;
  private readonly string _databaseId;

  public NotionService(string apiKey, string databaseId) {
    _databaseId = databaseId;
    _http = new HttpClient();
    _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    _http.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
  }

  public async Task AppendAsync(string text) {
    var payload = new {
      parent = new { database_id = _databaseId },
      properties = new {
        Name = new {
          title = new[] { new { text = new { content = text } } }
        }
      }
    };

    var json = System.Text.Json.JsonSerializer.Serialize(payload);
    using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
    await _http.PostAsync("https://api.notion.com/v1/pages", content);
  }
}
```

---

## Step 8 — `src/Fio/Fio.csproj`

`dotnet new webapi` auto-generates this file and `dotnet add package` populates the `<ItemGroup>`. One line needs to be added manually — `<InvariantGlobalization>true</InvariantGlobalization>` — which reduces the Docker image size by ~10MB by stripping unused globalization data.

The final file should look like:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

---

## Step 9 — EF Core Migration

Run once locally (requires `DATABASE_URL` in your environment):

```bash
cd src/Fio
dotnet ef migrations add InitAllowedUsers --output-dir Data/Migrations
dotnet ef database update
```

This creates the `allowed_users` table in your Neon database.
The `--output-dir Data/Migrations` flag ensures migration files land in the correct folder per the project structure.
The app also calls `db.Database.Migrate()` on startup, so Northflank applies migrations automatically on every deploy.

---

## Step 10 — `Dockerfile`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Fio/ .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Fio.dll"]
```

---

## Step 11 — `.env.example`

```
TELEGRAM_BOT_TOKEN=
NOTION_API_KEY=
NOTION_DATABASE_ID=
DATABASE_URL=postgresql://<user>:<password>@<host>.neon.tech/<dbname>?sslmode=require
```

Never commit real values. Set these as shell environment variables locally — see `CLAUDE.md` section 2.

---

## Step 12 — `.github/workflows/deploy.yml`

```yaml
name: Deploy

on:
  push:
    branches: [main]

env:
  IMAGE_NAME: ghcr.io/${{ github.repository_owner }}/fio

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push image
        uses: docker/build-push-action@v5
        with:
          context: .
          push: true
          tags: ${{ env.IMAGE_NAME }}:latest

      - name: Trigger Northflank deploy
        continue-on-error: true
        run: curl -s -f -X POST "${{ secrets.NORTHFLANK_DEPLOY_HOOK }}"
```

---

## Step 13 — External Setup (Manual Steps)

> **Order matters.** Follow these sections in sequence — Northflank depends on the GHCR image existing first.

### Neon
1. Go to neon.tech → Sign up (free, no credit card)
2. Create a new project → choose a region closest to your Northflank deployment
3. Go to **Dashboard → Connection Details** → copy the connection string URI
4. Use this as `DATABASE_URL`

### Telegram Bot
Same bot as the existing project, or create a new one via `@BotFather`.

### Notion Integration + Database
Same integration as the existing project (reuse `NOTION_API_KEY` and `NOTION_DATABASE_ID`).

### Push code to trigger first build
Before setting up Northflank, the Docker image must exist in GHCR. Push the code to `main`:

```bash
git add .
git commit -m "feat: initial setup"
git push origin main
```

Go to the **Actions** tab in GitHub and confirm the workflow completes successfully.

### Make GHCR image public
By default the GHCR package inherits your private repo visibility — Northflank cannot pull it without credentials. After the first successful Actions run:
1. Go to github.com → **Packages** (top nav) → find `fio`
2. **Package settings** → **Change visibility** → set to **Public**

> This only exposes the compiled Docker image, not your source code.

### Northflank
1. Go to northflank.com → Sign up (free) → Create a new project
2. Inside the project → **Add service** → **Deployment service**
3. Choose **External image** → Image: `ghcr.io/tuananhkt111/fio:latest`
4. Set **Port** to `8080`
5. Go to **Environment** tab → add:
   - `TELEGRAM_BOT_TOKEN`
   - `NOTION_API_KEY`
   - `NOTION_DATABASE_ID`
   - `DATABASE_URL`
6. Deploy → note the generated public URL
7. Go to service **Settings** → **CI/CD** → copy the **Trigger URL**

### GitHub Actions Secrets
Go to GitHub repo → Settings → Secrets and variables → Actions → add:
- `NORTHFLANK_DEPLOY_HOOK` — Trigger URL from Northflank service settings

> `GITHUB_TOKEN` is automatic — no manual setup needed for GHCR push.
>
> After adding the secret, push any change to `main` to trigger a full end-to-end deploy.

---

## Step 14 — Register Telegram Webhook

Visit this URL once in your browser:

```
https://api.telegram.org/bot<TELEGRAM_BOT_TOKEN>/setWebhook?url=https://YOUR-APP.code.run/api/telegram
```

Expected response: `{"ok":true,"result":true,"description":"Webhook was set"}`

---

## Managing Allowed Users

All user management is done directly in Neon — no code changes or redeployment needed.

> **Note:** The `allowed_users` table is created on the first Northflank deploy (when `db.Database.Migrate()` runs on startup). Run these SQL commands only after Northflank has deployed successfully at least once.

Use the **Neon SQL Editor** (Dashboard → SQL Editor):

| Action      | SQL                                                                  |
|-------------|----------------------------------------------------------------------|
| Add user    | `INSERT INTO allowed_users (telegram_id, label) VALUES (123, 'x');` |
| Remove user | `DELETE FROM allowed_users WHERE telegram_id = 123;`                 |
| List users  | `SELECT * FROM allowed_users;`                                       |

---

## Verification

### Local test
Set environment variables first (see `CLAUDE.md` section 2), then insert your Telegram ID into `allowed_users` via the Neon SQL Editor — the whitelist check will reject any ID not in the table:

```sql
INSERT INTO allowed_users (telegram_id, label) VALUES (YOUR_TELEGRAM_ID, 'owner');
```

Then run the app:

```bash
cd src/Fio
dotnet run
```

`dotnet run` starts on port 5000 by default (check the console output for the exact URL). In another terminal:

```bash
curl -X POST http://localhost:5000/api/telegram \
  -H "Content-Type: application/json" \
  -d '{"message":{"text":"test message","from":{"id":YOUR_TELEGRAM_ID},"chat":{"id":1}}}'
```

Expected response: `{"ok":true}`. Check Notion — should see a new row titled "test message".

If you see `{"ok":true,"skipped":true}`, the Telegram ID is not in `allowed_users`.

### Production test
1. Push to `main` → GitHub Actions builds and deploys automatically
2. Send a message to your Telegram bot → check Notion

---

## Secrets Reference

| Secret                   | Where to set              | Value source                          |
|--------------------------|---------------------------|---------------------------------------|
| `TELEGRAM_BOT_TOKEN`     | Northflank env vars       | `@BotFather` on Telegram              |
| `NOTION_API_KEY`         | Northflank env vars       | notion.so/my-integrations            |
| `NOTION_DATABASE_ID`     | Northflank env vars       | Notion database URL                   |
| `DATABASE_URL`           | Northflank env vars       | Neon → Dashboard → Connection Details |
| `NORTHFLANK_DEPLOY_HOOK` | GitHub Actions secret     | Northflank service → Settings → CI/CD |

---

## Troubleshooting

| Symptom                          | Likely cause                                        |
|----------------------------------|-----------------------------------------------------|
| Notion not updating              | Integration not connected to the database           |
| 403 from Notion API              | Wrong `NOTION_API_KEY` or integration not shared    |
| Bot ignores all messages         | `telegram_id` not in `allowed_users` table          |
| EF Core migration error          | `DATABASE_URL` missing or wrong format              |
| GitHub Actions push fails        | GHCR image name mismatch or permissions issue       |
| Northflank can't pull image      | GHCR package is still private — set visibility to Public |
| Northflank not deploying         | Deploy hook URL wrong or not saved as secret        |
| Works locally, not on Northflank | Env vars missing in Northflank environment tab      |
| Telegram webhook not triggering  | Webhook URL wrong or Northflank service not running |
