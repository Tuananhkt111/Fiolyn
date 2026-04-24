# Local Webhook Testing Guide

This guide walks you through testing the Telegram webhook locally without needing to configure Telegram's servers.

---

## Prerequisites

- ✅ App running locally (`dotnet run` from `Fiolyn/`)
- ✅ Database available (migrations auto-applied on startup)
- ✅ Configuration set up (`appsettings.Development.json`)

---

## Step 1: Start the App

```bash
cd Fiolyn
dotnet run
```

You should see output like:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5027
```

The webhook endpoint is at: **`http://localhost:5027/api/telegram`**

---

## Step 2: Link a Telegram ID to an AppUser

The webhook only processes messages whose `from.id` matches an active `AppUser.TelegramId`. You need to link a Telegram ID to an existing `AppUser` first.

### Prerequisite

You need an `AppUser` row. If you haven't logged into the web app yet, do that first — Google SSO with an email that's in `AllowedEmail` will create the `AppUser` on first login.

### Option A: Via the API (recommended)

Call the self-service endpoint with the access token you got from `/api/auth/google`:

```bash
curl -X PUT http://localhost:5027/api/users/me/telegram-id \
  -H "Authorization: Bearer <YOUR_ACCESS_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"telegramId": 12345}'
```

Response is a `UserResponse` with `telegramId: 12345`. An admin can also link for any user via `PUT /api/users/{id}/telegram-id`.

### Option B: Manual SQL

If you'd rather update the DB directly:

```sql
UPDATE "AppUser" SET "TelegramId" = 12345 WHERE "Email" = 'your.email@example.com';
```

Use any Telegram user ID you like (can be a real account or arbitrary number). The column has a unique index — reusing an ID that's already linked to another user will fail (and the API surfaces it as 409).

---

## Step 3: Run Webhook Tests

### Option A: PowerShell (Windows)

```powershell
.\test-webhook.ps1
```

### Option B: Bash (Linux/Mac)

```bash
bash test-webhook.sh
```

### Option C: Manual cURL

Test a valid allowed user:

```bash
curl -X POST http://localhost:5027/api/telegram \
  -H "Content-Type: application/json" \
  -d '{
    "message": {
      "from": {
        "id": 12345
      },
      "text": "Hello from webhook test"
    }
  }'
```

Expected response:
```json
{
  "ok": true,
  "success": true,
  "message": "Success"
}
```

---

## Understanding Test Scenarios

The test scripts validate four scenarios:

### ✅ Test 1: Valid User with Text

```json
{
  "message": {
    "from": { "id": 12345 },
    "text": "Hello webhook"
  }
}
```

**Expected:** 
- ✅ `"success": true`
- ✅ Message saved to Notion
- ✅ App logs: `Message from {UserId} saved to Notion`

---

### ⏭️ Test 2: Invalid User (No linked AppUser)

```json
{
  "message": {
    "from": { "id": 99999 },
    "text": "I'm not allowed"
  }
}
```

**Expected:**
- ✅ `"success": false`
- ✅ Message skipped (not saved to Notion)
- ✅ App logs: `Telegram user {TelegramId} not linked to any AppUser`
- ✅ HTTP 200 returned (Telegram requirement)

---

### ⏭️ Test 3: Missing Message Field

```json
{
  "update_id": 123456
}
```

**Expected:**
- ✅ `"success": false`
- ✅ Webhook rejected gracefully
- ✅ App logs: `Webhook missing message or from field`

---

### ⏭️ Test 4: No Text Field

```json
{
  "message": {
    "from": { "id": 12345 },
    "photo": []
  }
}
```

**Expected:**
- ✅ `"success": false`
- ✅ Photo/non-text ignored
- ✅ App logs: `Message from {TelegramId} has no text`

---

## Checking Results

### View App Logs

Watch the console while running tests:

```bash
# Terminal where app is running shows:
info: Fiolyn.Services.External.Telegram.TelegramMessageService[0]
      Message from Telegram user 12345 saved to Notion
```

### Check Swagger UI

Open browser to: **`http://localhost:5027/swagger/`**

This shows the available endpoints and lets you test directly in the UI.

---

## Troubleshooting

### ❌ "Database connection failed"

**Problem:** Setup script can't connect to database

**Solution:**
1. Verify `appsettings.Development.json` has correct connection string
2. Check database is running (Neon, local PostgreSQL, etc.)
3. Run SQL manually instead of using script

---

### ❌ "psql: command not found"

**Problem:** PostgreSQL client not installed

**Solution:**
1. Install PostgreSQL tools for your OS
2. Or run SQL manually using any database client (pgAdmin, DBeaver, etc.)

---

### ❌ Webhook returns 500 error

**Problem:** Application error during processing

**Solution:**
1. Check app console logs for error details
2. Verify `appsettings.Development.json` has all required keys (TelegramBotToken, NotionApiKey, etc.)
3. Check network connectivity to Notion API

---

### ❌ Message saved but Notion shows error

**Problem:** Message processed but Notion API failed

**Solution:**
1. Verify `NotionApiKey` and `NotionDatabaseId` are correct in config
2. Check Notion API key has permissions for the database
3. Database might be archived or deleted

---

## Next Steps

Once local testing works:

1. **Configure Telegram Bot Webhook** — Point Telegram to your production URL
2. **Monitor Logs** — Use Northflank logs dashboard
3. **Set up Alerts** — Monitor for webhook failures in production

See `docs/ConfigurationSettingSetup.md` for configuration details.

---

## Quick Reference

| Task | Command |
|------|---------|
| Start app | `dotnet run` (from `Fiolyn/`) |
| Setup test user (PowerShell) | `.\setup-test-user.ps1` |
| Setup test user (Bash) | `bash setup-test-user.sh` |
| Run tests (PowerShell) | `.\test-webhook.ps1` |
| Run tests (Bash) | `bash test-webhook.sh` |
| View Swagger | `http://localhost:5027/swagger/` |
| Webhook endpoint | `http://localhost:5027/api/telegram` |
