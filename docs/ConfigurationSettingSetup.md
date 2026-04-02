# Configuration Setup

## Overview

Fiolyn uses .NET's built-in `IConfiguration` pipeline with the **Options Pattern** to manage secrets and settings across environments. The pipeline merges sources in priority order (lowest → highest):

```
appsettings.json
  → appsettings.Development.json   (local secrets, gitignored)
    → Environment variables         (Northflank prod, overrides appsettings)
```

Configuration is bound to strongly-typed classes in `ServiceRegisters/Settings/` using `IOptions<T>`.

---

## Configuration Structure

Nested configuration uses `:` (colon) in JSON and `__` (double underscore) in environment variables.

### appsettings.json (or appsettings.Development.json)

```json
{
  "ConnectionStrings": {
    "NeonDatabaseUrl": "postgresql://..."
  },
  "TelegramBotToken": "...",
  "NotionSetting": {
    "NotionApiKey": "...",
    "NotionDatabaseId": "..."
  }
}
```

### Environment Variables (Northflank)

```
CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
TELEGRAMBOTTOKEN=...
NOTIONSETTING__NOTIONAPIKEY=...
NOTIONSETTING__NOTIONDATABASEID=...
```

---

## Options Classes

All configuration is bound to strongly-typed classes via `IOptions<T>`:

- **`NotionSettings`** — `Fiolyn/ServiceRegisters/Settings/NotionSettings.cs`
  ```csharp
  public class NotionSettings
  {
    public string NotionApiKey { get; set; }
    public string NotionDatabaseId { get; set; }
  }
  ```

Services receive configuration via primary constructor:
```csharp
public class NotionService( IOptions<NotionSettings> options, ... )
{
  var settings = options.Value ;
}
```

---

## Local Setup

1. Create `Fiolyn/appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "NeonDatabaseUrl": "Host=..;Database=..;Username=..;Password=..;SSL Mode=Require"
  },
  "TelegramBotToken": "your-token",
  "NotionSetting": {
    "NotionApiKey": "your-api-key",
    "NotionDatabaseId": "your-db-id"
  }
}
```

2. Run the project — `launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, which causes .NET to automatically load `appsettings.Development.json`:

```bash
cd Fiolyn
dotnet run
```

No manual environment variable exports needed.

---

## Production (Northflank)

Set these environment variables in the Northflank dashboard (use `__` for nested keys):

```
CONNECTIONSTRINGS__NEONDATABASEURL=postgresql://...
TELEGRAMBOTTOKEN=...
NOTIONSETTING__NOTIONAPIKEY=...
NOTIONSETTING__NOTIONDATABASEID=...
```

Environment variables take higher priority than `appsettings.json`, so they override any defaults automatically.