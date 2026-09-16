# Deployment Rules

Read this when touching `Program.cs`'s middleware pipeline, the `Dockerfile`, `.github/workflows/`, or anything about how the app is hosted.

> **How to actually deploy** (pipeline, secrets, first-time setup): [guides/deployment.md](../guides/deployment.md) ·
> **Why:** [ADR 0002 — Neon and Northflank](../decisions/0002-neon-and-northflank-hosting.md) · [0003 — no HTTPS redirection](../decisions/0003-no-https-redirection.md) · [0004 — no startup migrations](../decisions/0004-migrations-never-run-on-startup.md)

## Never add `UseHttpsRedirection`

Northflank terminates TLS at its edge and forwards **plain HTTP** to the container. With the middleware enabled every forwarded request looks insecure, the app 307s back to `https://`, the proxy terminates TLS again and forwards plain HTTP again — an infinite redirect loop that presents as the service being completely unreachable, with nothing obviously wrong in the logs.

It is in the default ASP.NET template, so it gets re-added by accident. Don't.

## Middleware order is fixed

```
UseExceptionHandler → UseCors → UseAuthentication → UseAuthorization → MapControllers
```

`UseCors` must come **before** `UseAuthentication` — preflight requests are unauthenticated. A CORS failure surfaces in the browser as a network error rather than a 401, which makes it easy to misdiagnose as an auth bug.

## Never apply migrations from the app

`Program.cs` contains no `Migrate()` and no `EnsureCreated()`, and neither may be added. Migrations are applied out-of-band, **before** the new image boots:

```bash
cd Fiolyn && dotnet ef database update
```

Forgetting this is the most likely way to break a release: the new code meets the old schema and fails at query time, not at startup, so the service comes up healthy and then errors on use.

## Two settings that look like defaults but aren't

- **Port 8080** (`ASPNETCORE_URLS=http://+:8080`) is contractual with the Northflank service port. Changing one without the other takes the service down.
- **The Docker build context is the repo root**, not `Fiolyn/`. The Dockerfile reaches outside its own directory.

The pipeline, secrets and first-time setup are operator concerns, not code rules — see [guides/deployment.md](../guides/deployment.md) and [guides/configuration.md](../guides/configuration.md).

## Never commit a secret

`appsettings.json` is tracked and stays minimal. Local secrets go in the gitignored `appsettings.Development.json`; production values are Northflank environment variables.
