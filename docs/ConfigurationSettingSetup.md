# Configuration Setup

## Overview

Fiolyn uses .NET's built-in `IConfiguration` pipeline to manage secrets and settings across environments. The pipeline merges sources in priority order (lowest → highest):

```
appsettings.json
  → appsettings.Development.json   (local secrets, gitignored)
    → Environment variables         (Northflank prod, overrides appsettings)
```

This means the same code works in both local and production — no manual `export` needed locally.

---

## Why IConfiguration over `Environment.GetEnvironmentVariable`

| Capability                           | `IConfiguration`      | `Environment.GetEnvironmentVariable` |
|--------------------------------------|-----------------------|--------------------------------------|
| Reads `appsettings.*.json`           | Yes                   | No                                   |
| Reads env vars                       | Yes (higher priority) | Yes                                  |
| Works locally without manual exports | Yes                   | No                                   |
| .NET idiomatic                       | Yes                   | No                                   |

Using `Environment.GetEnvironmentVariable` bypasses the config pipeline entirely, which means `appsettings.Development.json` is never read locally.

---

## Key Names

All config keys use **PascalCase** to match `appsettings.json` conventions:

| Key                | Description                      |
|--------------------|----------------------------------|
| `DatabaseUrl`      | PostgreSQL connection URI (Neon) |
| `TelegramBotToken` | Telegram bot token               |
| `NotionApiKey`     | Notion integration token         |
| `NotionDatabaseId` | Target Notion database ID        |


---

## Local Setup

1. Create `Fiolyn/appsettings.Development.json` (gitignored):

```json
{
  "DatabaseUrl": "postgresql://user:password@host/dbname?sslmode=require",
  "TelegramBotToken": "your-telegram-token",
  "NotionApiKey": "your-notion-api-key",
  "NotionDatabaseId": "your-notion-database-id"
}
```

1. Run the project — `launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, which causes .NET to automatically load `appsettings.Development.json`:

```bash
cd Fiolyn
dotnet run
```

No environment variables need to be manually exported.

---

## Production (Northflank)

Set these environment variables in the Northflank dashboard:

```
DatabaseUrl=postgresql://...
TelegramBotToken=...
NotionApiKey=...
NotionDatabaseId=...
```

Env vars take higher priority than `appsettings.json`, so they override any defaults automatically.