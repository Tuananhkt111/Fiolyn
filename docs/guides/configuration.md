# Configuration

Fiolyn uses .NET's `IConfiguration` with the options pattern. Sources merge lowest priority first:

```
appsettings.json → appsettings.Development.json (local, gitignored) → environment variables (prod)
```

Nested keys use `:` in JSON and `__` in environment variables. Each section binds to a
strongly-typed class in `Fiolyn/ServiceRegisters/Settings/` via `IOptions<T>`.

`appsettings.json` is tracked and intentionally holds only `Logging` and `AllowedHosts`. No secret
ever goes in it.

## Keys

| Config path | Environment variable | Bound to | Notes |
|---|---|---|---|
| `ConnectionStrings:NeonDatabaseUrl` | `CONNECTIONSTRINGS__NEONDATABASEURL` | — | Neon Postgres connection string |
| `NotionSettings:NotionApiKey` | `NOTIONSETTINGS__NOTIONAPIKEY` | `NotionSettings` | |
| `NotionSettings:NotionDatabaseId` | `NOTIONSETTINGS__NOTIONDATABASEID` | `NotionSettings` | |
| `Jwt:SigningKey` | `JWT__SIGNINGKEY` | `JwtSettings` | **Required, ≥32 chars** — startup fails without it |
| `Jwt:Issuer` | `JWT__ISSUER` | `JwtSettings` | Defaults to `Fiolyn` |
| `Jwt:Audience` | `JWT__AUDIENCE` | `JwtSettings` | Defaults to `FiolynWeb` |
| `Jwt:AccessTokenExpiryMinutes` | `JWT__ACCESSTOKENEXPIRYMINUTES` | `JwtSettings` | Defaults to `15` |
| `Jwt:RefreshTokenExpiryDays` | `JWT__REFRESHTOKENEXPIRYDAYS` | `JwtSettings` | Defaults to `30` |
| `GoogleAuth:ClientId` | `GOOGLEAUTH__CLIENTID` | `GoogleAuthSettings` | Expected `aud` for incoming Google ID tokens |
| `Cors:AllowedOrigins:0` | `CORS__ALLOWEDORIGINS__0` | `CorsSettings` | Array. Exact origin, scheme + host, **no trailing slash** |

Section names are `Jwt`, `GoogleAuth`, `Cors` and `NotionSettings` — note that three of the four
don't match their class name.

`Cors:AllowedOrigins` is an array, so each entry gets its own index suffix in environment-variable
form. An empty list means no browser origin can call the API; server-to-server callers and the
Telegram webhook are unaffected, since CORS is a browser mechanism.

## Local setup

Create `Fiolyn/appsettings.Development.json` — gitignored:

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
    "SigningKey": "at least 32 characters — e.g. openssl rand -base64 48"
  },
  "GoogleAuth": {
    "ClientId": "your-client-id.apps.googleusercontent.com"
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173" ]
  }
}
```

`launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`, so `dotnet run` picks this up
automatically. `Jwt:Issuer`, `Jwt:Audience` and the expiry values can be omitted — the
`JwtSettings` defaults cover them.

## Production

Set the same keys as environment variables in the Northflank dashboard, using the `__` form.
Environment variables override `appsettings.json` automatically.

## Fail-fast on `Jwt:SigningKey`

Both `AuthServiceRegister` (at startup) and `JwtService`'s constructor throw
`InvalidOperationException` when the key is missing or shorter than 32 characters. That is defence
in depth and deliberate: there is no silent development fallback, because a predictable dev key
that leaks into production forges valid tokens for every user.

Practical consequence: anything that builds the application host — including generating
[`../reference/openapi.json`](../reference/openapi.json) — needs this key present.

## Telegram bot token

The running app **never reads a Telegram bot token.** Fiolyn only ingests inbound webhooks; it
never calls the Telegram API. The token is needed once, externally, to register the webhook URL:

```bash
curl -X POST "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook" \
  -d "url=https://<your-host>/api/Telegram"
```

See [`local-development.md`](local-development.md) for testing the webhook without Telegram.
