# Configuration Setup

Fiolyn uses .NET's `IConfiguration` + Options Pattern. Sources merge in priority order (lowest → highest):

```
appsettings.json → appsettings.Development.json (local, gitignored) → env vars (prod)
```

Nested keys use `:` in JSON and `__` in env vars. Each section binds to a strongly-typed class in `Fiolyn/ServiceRegisters/Settings/` via `IOptions<T>`.

---

## Required keys

| Config path                         | Env var                              | Bound to             | Notes                                                              |
|-------------------------------------|--------------------------------------|----------------------|--------------------------------------------------------------------|
| `ConnectionStrings:NeonDatabaseUrl` | `CONNECTIONSTRINGS__NEONDATABASEURL` | —                    | Neon Postgres connection string                                    |
| `NotionSettings:NotionApiKey`       | `NOTIONSETTINGS__NOTIONAPIKEY`       | `NotionSettings`     |                                                                    |
| `NotionSettings:NotionDatabaseId`   | `NOTIONSETTINGS__NOTIONDATABASEID`   | `NotionSettings`     |                                                                    |
| `Jwt:SigningKey`                    | `JWT__SIGNINGKEY`                    | `JwtSettings`        | **Required, ≥32 chars** — app fails fast at startup                |
| `Jwt:Issuer`                        | `JWT__ISSUER`                        | `JwtSettings`        | Default `Fiolyn`                                                   |
| `Jwt:Audience`                      | `JWT__AUDIENCE`                      | `JwtSettings`        | Default `FiolynWeb`                                                |
| `Jwt:AccessTokenExpiryMinutes`      | `JWT__ACCESSTOKENEXPIRYMINUTES`      | `JwtSettings`        | Default `15`                                                       |
| `Jwt:RefreshTokenExpiryDays`        | `JWT__REFRESHTOKENEXPIRYDAYS`        | `JwtSettings`        | Default `30`                                                       |
| `GoogleAuth:ClientId`               | `GOOGLEAUTH__CLIENTID`               | `GoogleAuthSettings` | Google OAuth 2.0 Client ID — expected `aud` for incoming ID tokens |

Note the section key is `Jwt` / `GoogleAuth` / `NotionSettings` (not `JwtSettings` / `GoogleAuthSettings` / `NotionSetting`).

---

## Local setup

Create `Fiolyn/appsettings.Development.json` (gitignored):

```json
{
  "ConnectionStrings": {
    "NeonDatabaseUrl": "postgresql://user:password@host/database?sslmode=require"
  },
  "NotionSettings": {
    "NotionApiKey": "your-notion-api-key",
    "NotionDatabaseId": "your-notion-database-id"
  },
  "Jwt": {
    "SigningKey": "<random >= 32 chars — e.g. `openssl rand -base64 48`>"
  },
  "GoogleAuth": {
    "ClientId": "<your-google-oauth-client-id>.apps.googleusercontent.com"
  }
}
```

`launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, so `dotnet run` picks this up automatically.

`Jwt:Issuer`, `Jwt:Audience`, and the expiry values can be omitted locally — the `JwtSettings` class defaults cover them.

---

## Production (Northflank)

Set the env vars in the Northflank dashboard. Env vars override `appsettings.json` automatically.

---

## Service registration

Each settings class is bound in its service register:

- `NotionSettings` → `NotionServiceRegister` (`configuration.GetSection("NotionSettings")`)
- `JwtSettings` + `GoogleAuthSettings` → `AuthServiceRegister` (`configuration.GetSection("Jwt")` / `"GoogleAuth"`)

`AuthServiceRegister` and `JwtService`'s constructor both throw `InvalidOperationException` if `Jwt:SigningKey` is missing or <32 chars — defence in depth.

---

## Telegram bot webhook setup

The Telegram bot token is **not** read by the running app — Fiolyn only ingests inbound webhooks. The token is only needed **once**, externally, to register the webhook URL with Telegram:

```bash
curl -X POST "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook" \
  -d "url=https://<your-host>/api/telegram"
```

See `docs/LocalWebhookTesting.md` for the full flow.