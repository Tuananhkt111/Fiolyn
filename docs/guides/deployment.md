# Deployment

## Pipeline

```
push to master
  → GitHub Actions (.github/workflows/deploy.yml)
  → docker build (context = repo root, dockerfile = Fiolyn/Dockerfile)
  → push ghcr.io/tuananhkt111/fiolyn:latest
  → POST api.northflank.com/v1/projects/fiolyn/services/fiolyn/restart
  → Northflank pulls the new image and restarts
```

The build context is the **repo root**, not `Fiolyn/` — the Dockerfile reaches outside its own
directory, so building from `Fiolyn/` will fail.

Only `master` triggers a deploy. There is no staging environment.

## Migrations are a separate, manual step

The pipeline does **not** touch the database. The application never applies migrations either — see
[0004](../decisions/0004-migrations-never-run-on-startup.md).

When a release contains a schema change, apply it **before** the new image boots:

```bash
cd Fiolyn && dotnet ef database update
```

Between that command and the restart completing, the *old* code is running against the *new*
schema. For an additive change that is harmless. For a column drop or rename it is an outage, so
take a brief maintenance window: stop the service, migrate, deploy.

Forgetting this step is the most likely way to break a deploy: the new code meets the old schema
and fails at query time, not at startup, so the service comes up healthy and then errors on use.

## Secrets

| Secret | Where | Source |
|---|---|---|
| `NORTHFLANK_API_TOKEN` | GitHub Actions secret | Northflank → account settings → API tokens |
| `CONNECTIONSTRINGS__NEONDATABASEURL` | Northflank env var | Neon → Dashboard → Connection Details |
| `JWT__SIGNINGKEY` | Northflank env var | Generate, e.g. `openssl rand -base64 48` |
| `GOOGLEAUTH__CLIENTID` | Northflank env var | Google Cloud Console → OAuth client |
| `NOTIONSETTINGS__NOTIONAPIKEY` | Northflank env var | notion.so/my-integrations |
| `NOTIONSETTINGS__NOTIONDATABASEID` | Northflank env var | Notion database URL |
| `CORS__ALLOWEDORIGINS__0` | Northflank env var | The web client's exact origin |

`GITHUB_TOKEN` is provided automatically — no setup needed for the GHCR push.

Full key reference: [`configuration.md`](configuration.md).

## Runtime shape

- The container listens on port **8080** (`ASPNETCORE_URLS=http://+:8080`).
- **Northflank terminates TLS externally and forwards plain HTTP.** `UseHttpsRedirection` must
  never be added — it produces a redirect loop that looks like a total outage. See
  [0003](../decisions/0003-no-https-redirection.md).
- Swagger is served in Development only, so it is not exposed in production.

## First-time setup

Only needed once, and already done for the current deployment:

1. **Neon** — create a project, copy the connection string.
2. **GHCR image visibility** — the package inherits the repo's private visibility, and Northflank
   cannot pull it without credentials. After the first successful Actions run, set the `fiolyn`
   package to **Public**. This exposes only the compiled image, not the source.
3. **Northflank** — create a deployment service from external image
   `ghcr.io/tuananhkt111/fiolyn:latest`, port 8080, then add the environment variables above.
4. **GitHub Actions** — add `NORTHFLANK_API_TOKEN` as a repository secret.
5. **Telegram webhook** — register the production URL once:
   ```bash
   curl -X POST "https://api.telegram.org/bot<TOKEN>/setWebhook" \
     -d "url=https://<your-host>/api/Telegram"
   ```
6. **Google OAuth** — add the web client's production origin to Authorized JavaScript origins.

## Diagnosing a failed deploy

1. GitHub Actions tab — did build and push succeed?
2. Northflank dashboard → service → **Logs**.
3. If the app starts and then fails on every request, suspect the missing migration step.
4. If startup throws about the signing key, `JWT__SIGNINGKEY` is missing or too short.
5. If Northflank cannot pull the image, the GHCR package is still private.
