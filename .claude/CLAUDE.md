# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fiolyn is a .NET 8 web API that receives Telegram bot messages via webhook, checks the sender against an allowed-user whitelist (Neon PostgreSQL via EF Core), and saves the message as a new page in a Notion database.

**Stack:** C# .NET 8 · Northflank (Docker) · GitHub Actions → GHCR · Neon PostgreSQL · Notion API · Telegram Bot API

---

## Commands

All commands run from the `Fiolyn/` subdirectory (where the `.csproj` lives):

```bash
dotnet run                                                        # run locally
dotnet ef migrations add <Name> --output-dir Data/Migrations      # new migration
dotnet ef database update                                         # apply migrations
```

Build/publish is handled by Docker — no manual `dotnet publish` needed locally.

---

## Local Configuration

In `Development`, the app reads config from `Fiolyn/appsettings.Development.json` (already gitignored). Set these keys:

```json
{
  "DatabaseUrl": "postgresql://...",
  "TelegramBotToken": "...",
  "NotionApiKey": "...",
  "NotionDatabaseId": "..."
}
```

In production (Northflank), the app falls back to environment variables: `DATABASE_URL`, `TELEGRAM_BOT_TOKEN`, `NOTION_API_KEY`, `NOTION_DATABASE_ID`.

`DATABASE_URL` on Northflank must be the **postgresql:// URI format** from Neon, not the ADO.NET format.

---

## Architecture

Request path: `POST /api/telegram` → `TelegramController` → checks `AllowedUsers` table via EF Core → calls `NotionService.AppendAsync()`.

- `Program.cs` — wiring only (DI, middleware, `db.Database.Migrate()` on startup). No logic here.
- `Controllers/TelegramController.cs` — parses raw JSON body, skips non-text/non-allowed updates, always returns 200.
- `Services/NotionService.cs` — thin HTTP wrapper around the Notion pages API.
- `Data/AppDbContext.cs` + `Entities/AllowedUser.cs` — EF Core Code First; schema lives in C#, never raw SQL.
- `Data/Migrations/` — auto-generated, committed, applied automatically on startup via `Migrate()`.

---

## Code Style

- 2-space indentation, no tabs. Max 120 chars per line.
- Opening brace `{` on its own line for types, methods, and lambdas.
- Spaces inside all parentheses: `TelegramController( AppDbContext db )`.
- `var` when type is obvious. `async`/`await` for all I/O — never `.Result`/`.Wait()`.
- Primary constructors (C# 12) preferred to reduce boilerplate.
- `record` for DTOs. One class per file, file name matches class name.

Namespaces: `Fiolyn.Controllers` · `Fiolyn.Services` · `Fiolyn.Entities` · `Fiolyn.Data`. `Program.cs` uses top-level statements (no namespace).

---

## Deployment

Push to `master` → GitHub Actions builds Docker image → pushes to GHCR → triggers Northflank service restart.

- Dockerfile is at `Fiolyn/Dockerfile`; the CI workflow specifies `file: Fiolyn/Dockerfile` with `context: .` (repo root).
- The app listens on port 8080 (`ASPNETCORE_URLS=http://+:8080`) — Northflank terminates TLS externally.
- **Do not use `UseHttpsRedirection`** — the container receives plain HTTP from Northflank's reverse proxy, causing redirect failures.
- Secrets live in Northflank env vars (`DATABASE_URL`, `TELEGRAM_BOT_TOKEN`, `NOTION_API_KEY`, `NOTION_DATABASE_ID`) and GitHub Actions secrets (`NORTHFLANK_API_TOKEN`).

---

## Key Rules

- Always return 200 from the Telegram webhook — Telegram retries on any non-2xx response.
- Never use `EnsureCreated()` — it bypasses migrations and corrupts schema history.
- Never edit generated migration files — regenerate instead.
- Never delete a migration that has been applied to any environment.