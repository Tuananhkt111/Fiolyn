# Fiolyn Web App — Implementation Plan

## Context

The Fiolyn .NET 10 backend ships 23 REST endpoints (auth, users, broker accounts, stock transactions, allowed-emails) plus a Telegram webhook. There is **no web UI today** — users interact via Swagger or Telegram. This plan adds a first-party React SPA, bundled into the same Docker image as the backend, served by ASP.NET Core from `wwwroot/`. One container, one Northflank service, same-origin auth, no CORS surface.

**End state:** `https://<northflank-host>/` returns the SPA; `/api/*` still hits controllers; Google SSO logs the user in, tokens round-trip to localStorage/memory, and all CRUD for transactions / broker accounts / admin (users + allowed-emails) is reachable from the UI. Telegram webhook and Swagger continue to work unchanged.

## Decisions locked

- **Stack:** React 18 + Vite + TypeScript + Tailwind + shadcn/ui. TanStack Query for server state, react-hook-form + zod for forms, react-router-dom v6 for routing, axios for HTTP, `@react-oauth/google` for Google Sign-In.
- **Deploy:** bundle into backend `wwwroot/`. Multi-stage Dockerfile (`node:20-alpine` → `.NET SDK` → `.NET aspnet runtime`). Same image, same service.
- **Scope (MVP):** 1:1 with the API surface — login, profile, transaction CRUD (with symbol filter), broker-account CRUD, admin users page, admin allowed-emails page. No analytics, no charts.
- **Token storage:** access token in memory, refresh token in `localStorage` under key `fiolyn.refresh`. Acceptable trade-off for a single-tenant same-origin admin app. If XSS concerns emerge later, migrate to httpOnly cookie (one backend controller change).

## File-level changes

Absolute paths. All `Fiolyn.Web/` entries are **new files** unless marked MODIFY.

### 1. Frontend scaffolding — `D:\Fiolyn\Fiolyn.Web\`

- `package.json` — React 18, Vite 5, TypeScript 5, Tailwind 3, TanStack Query 5, axios, zod, react-hook-form, `@react-oauth/google`, Radix primitives, sonner, lucide-react, class-variance-authority, tailwind-merge, clsx. Dev: vitest, @testing-library/react, jsdom, msw, eslint, @vitejs/plugin-react.
- `tsconfig.json`, `tsconfig.node.json` — strict TS, path alias `@/* → src/*`.
- `vite.config.ts` — React plugin; `build.outDir = process.env.VITE_OUT_DIR || '../Fiolyn/wwwroot'` (env-configurable so Docker can redirect); `build.emptyOutDir = true`; dev proxy `/api → http://localhost:5000`; Vitest config (`jsdom`, `./src/test/setup.ts`).
- `tailwind.config.ts`, `postcss.config.js`, `index.html`, `.eslintrc.cjs`, `.gitignore` (ignores `node_modules`, `dist`, `.vite`, `coverage`), `.env.example` (`VITE_GOOGLE_CLIENT_ID=`, `VITE_API_BASE_URL=`), `components.json` (shadcn config), `README.md` (install/dev/build notes).
- `src/main.tsx` — mounts `<App/>`, wraps in `GoogleOAuthProvider`, `QueryClientProvider`, `BrowserRouter`, `<Toaster/>`.
- `src/App.tsx` — route table (see §5 below).
- `src/index.css` — Tailwind directives + shadcn CSS variables.
- `src/vite-env.d.ts` — augments `ImportMetaEnv`.
- `src/lib/utils.ts` — `cn()` helper.
- `src/test/setup.ts` — jest-dom, RTL cleanup.

### 2. shadcn/ui primitives — `src/components/ui/`

Generated via shadcn CLI: `button`, `input`, `label`, `dialog`, `dropdown-menu`, `table`, `select`, `textarea`, `form` (RHF-bound), `alert`, `skeleton`, `badge`, `card`, `toaster` (sonner wrapper).

### 3. Layout + guards — `src/components/`

- `layout/AppLayout.tsx` — topbar (app name, `UserMenu`), side nav. Admin links rendered only when `me.isAdmin`.
- `layout/UserMenu.tsx` — avatar from `me.pictureUrl`, displayName, logout.
- `guards/AuthGuard.tsx` — redirects unauthed to `/login`.
- `guards/AdminGuard.tsx` — renders 403 Alert for non-admins.
- `shared/ConfirmDialog.tsx` — reused by all delete buttons.

### 4. Auth + API client — `src/auth/`, `src/api/`

- `auth/tokenStore.ts` — in-memory access token (module-scope `let` + subscribers), refresh token persisted in `localStorage`.
- `auth/AuthContext.tsx` — `AuthProvider` exposing `{ me, isLoading, login(googleIdToken), logout() }`. On mount: if a refresh token is present, calls `POST /api/auth/refresh` then `GET /api/users/me` to rehydrate.
- `auth/useAuth.ts` — hook.
- `api/http.ts` — single axios instance. `baseURL = import.meta.env.VITE_API_BASE_URL || ''` (empty string → same origin). Request interceptor attaches `Authorization: Bearer`. Response interceptor on 401 **coalesces concurrent 401s into one refresh** via a module-level `refreshPromise`; retries the original request once with the new token; on refresh failure, clears the store and forces logout. **Does not** retry `/api/auth/refresh` or `/api/auth/google`.
- `api/types.ts` — TS mirrors of backend DTOs. Key records:
  - `AuthResponse { accessToken; refreshToken; expiresAt }`
  - `UserResponse { id; email; displayName; pictureUrl; isAdmin; lastLoginAt; created }`
  - `BrokerAccountResponse { id; name; created; lastModified }`
  - `StockTransactionResponse { id; symbol; transactionType; quantity; price; totalAmount; transactionDate; brokerAccountId; brokerAccountName; notes; created; lastModified }` (matches `Fiolyn/Controllers/DTOs/Responses/StockTransaction/StockTransactionResponse.cs`)
  - `AllowedEmailResponse { id; email; note; invitedByUserId; created }`
  - `TransactionType = 'Buy' | 'Sell' | 'Dividend'` — **depends on §7 backend fix**.
- `api/auth.ts`, `api/users.ts`, `api/brokerAccounts.ts`, `api/stockTransactions.ts`, `api/allowedEmails.ts` — thin wrappers over `http.ts`.
- `api/queryKeys.ts` — `['me']`, `['users']`, `['broker-accounts']`, `['stock-transactions', { symbol }]`, `['allowed-emails']`.

### 5. Pages + routes — `src/pages/`

Route table (in `App.tsx`):

| Path | Component | Guard |
|---|---|---|
| `/login` | `LoginPage` | unauthed-only (redirects to `/` if authed) |
| `/` | `IndexRedirect` | — (sends authed → `/transactions`, else `/login`) |
| `/transactions` | `TransactionsPage` | `AuthGuard` |
| `/broker-accounts` | `BrokerAccountsPage` | `AuthGuard` |
| `/profile` | `ProfilePage` | `AuthGuard` |
| `/admin/users` | `UsersPage` | `AuthGuard` + `AdminGuard` |
| `/admin/allowed-emails` | `AllowedEmailsPage` | `AuthGuard` + `AdminGuard` |
| `*` | `NotFoundPage` | — |

Each page file ≤ ~150 lines, uses TanStack Query for list + mutations, shadcn Dialog for create/edit, `ConfirmDialog` for delete, form validation via `src/forms/*Schema.ts` (zod). Dividend transactions hide qty/price inputs and submit `{ quantity: 0, price: 0 }`. Symbol input is not client-normalized — the backend uppercases it (see `StockTransaction.Symbol` setter).

### 6. Tests (minimum viable)

- `src/api/__tests__/http.test.ts` — (a) bearer attached, (b) concurrent 401s cause exactly one `/refresh` call, (c) refresh failure clears store + propagates 401, (d) no infinite loop when `/refresh` itself 401s.
- `src/components/guards/__tests__/AuthGuard.test.tsx` — unauthed → redirect.
- `src/components/guards/__tests__/AdminGuard.test.tsx` — non-admin → 403.

Mocks via `msw`.

### 7. Backend wiring — small, focused edits

- **MODIFY `D:\Fiolyn\Fiolyn\Program.cs`** — insert static-file + fallback plumbing. Final middleware order:
  ```
  app.UseExceptionHandler() ;
  app.UseDefaultFiles() ;
  app.UseStaticFiles() ;
  app.UseCors() ;
  app.UseAuthentication() ;
  app.UseAuthorization() ;
  app.MapControllers() ;
  app.MapFallbackToFile( "index.html" ) ;
  ```
  `UseDefaultFiles` + `UseStaticFiles` go **before** auth (static assets must be public). `MapFallbackToFile` goes **after** `MapControllers` so attribute-routed API wins. `/api/Telegram` (POST, explicit route) is unaffected — confirmed against `Fiolyn/Controllers/TelegramController.cs`.

- **MODIFY `D:\Fiolyn\Fiolyn\ServiceRegisters\WebApiServiceRegister.cs`** — add `StringEnumConverter` so `TransactionType` serializes as `"Buy"|"Sell"|"Dividend"` instead of `0|1|2`. Insert inside the existing `AddNewtonsoftJson` options lambda:
  ```csharp
  options.SerializerSettings.Converters.Add( new StringEnumConverter() ) ;
  ```
  (Add `using Newtonsoft.Json.Converters ;`.) Without this, the frontend would have to work with numeric enums — ugly and fragile.

- **MODIFY `D:\Fiolyn\Fiolyn\Fiolyn.csproj`** — add one safety clause so `wwwroot/**` is always included at publish time:
  ```xml
  <ItemGroup>
    <Content Include="wwwroot\**" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
  ```

- **MODIFY `D:\Fiolyn\Fiolyn\Dockerfile`** — rewrite to multi-stage. Build context is repo root (confirmed in `.github/workflows/deploy.yml`):
  ```dockerfile
  # --- SPA build ---
  FROM node:20-alpine AS web
  WORKDIR /web
  COPY Fiolyn.Web/package*.json ./
  RUN npm ci
  COPY Fiolyn.Web/ ./
  ARG VITE_GOOGLE_CLIENT_ID
  ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID
  ENV VITE_OUT_DIR=/web/dist
  RUN npm run build

  # --- .NET build ---
  FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
  WORKDIR /src
  COPY Fiolyn/ ./Fiolyn/
  COPY --from=web /web/dist ./Fiolyn/wwwroot/
  WORKDIR /src/Fiolyn
  RUN dotnet publish -c Release -o /app/publish

  # --- Runtime ---
  FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
  WORKDIR /app
  COPY --from=build /app/publish .
  EXPOSE 8080
  ENV ASPNETCORE_URLS=http://+:8080
  ENTRYPOINT ["dotnet", "Fiolyn.dll"]
  ```

- **MODIFY `D:\Fiolyn\.github\workflows\deploy.yml`** — one field added to the `docker/build-push-action@v5` step so the client ID is baked into the Vite bundle at build time:
  ```yaml
  with:
    context: .
    file: Fiolyn/Dockerfile
    push: true
    tags: ${{ env.IMAGE_NAME }}:latest
    build-args: |
      VITE_GOOGLE_CLIENT_ID=${{ secrets.GOOGLE_CLIENT_ID }}
  ```
  User must add the `GOOGLE_CLIENT_ID` secret in GitHub repo settings before the first deploy.

### 8. Repo-level hygiene

- **MODIFY `D:\Fiolyn\.gitignore`** — append `Fiolyn/wwwroot/`, `Fiolyn.Web/node_modules/`, `Fiolyn.Web/dist/`, `Fiolyn.Web/.vite/`, `Fiolyn.Web/coverage/`.
- **MODIFY `D:\Fiolyn\.dockerignore`** — append `Fiolyn/wwwroot/`, `Fiolyn.Web/node_modules`, `Fiolyn.Web/dist`. The Docker build regenerates `wwwroot/` inside the image, so any local dev build staying out of the image is the correct behavior.

### 9. Config

- `Fiolyn/appsettings.Development.json` (gitignored) — developer sets `"GoogleAuth": { "ClientId": "<dev client id>" }`.
- Northflank env var — `GoogleAuth__ClientId=<prod client id>`.
- Same client ID is passed as `VITE_GOOGLE_CLIENT_ID` in local `Fiolyn.Web/.env` (gitignored) and via GH Actions secret in CI.

## Implementation order

Each step ends in a verifiable working state. Run `dotnet build` + `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` after every backend edit per CLAUDE.md.

1. **Backend plumbing, no SPA yet.**
   - Apply §7 edits to `Program.cs`, `WebApiServiceRegister.cs`, `Fiolyn.csproj`.
   - Drop a stub `Fiolyn/wwwroot/index.html` with `<h1>Hello</h1>`.
   - `dotnet run` → `/` serves the stub; `/api/broker-accounts` still 401s; `/deep/nested/path` serves the stub (fallback works); POST `/api/Telegram` still reaches controller.
   - Verify `/api/stock-transactions` response includes `"transactionType": "Buy"` (string), not a number.
   - Delete the stub, add the `.gitignore` / `.dockerignore` entries.
   - Run diagnostics + tests.

2. **Scaffold empty SPA.**
   - Create `Fiolyn.Web/` with package.json, vite.config, tsconfig, Tailwind pipeline, shadcn init, `src/main.tsx` rendering `<h1>Fiolyn</h1>`.
   - `npm install && npm run build` → verify `Fiolyn/wwwroot/index.html` + hashed assets appear.
   - `dotnet run` → `/` shows the React stub. **SPA pipeline is working before any auth code exists.**

3. **Multi-stage Docker build.**
   - Apply new Dockerfile; `docker build -t fiolyn:smoke -f Fiolyn/Dockerfile --build-arg VITE_GOOGLE_CLIENT_ID=<id> .` (from repo root).
   - `docker run --rm -p 8080:8080 --env-file <local-env> fiolyn:smoke` → `:8080/` serves the stub. Full deploy pipeline validated before real code.

4. **Auth layer.**
   - Implement `tokenStore`, `http.ts` (with coalescing interceptor), `api/auth.ts`, `AuthContext`, `LoginPage`.
   - Seed one `AllowedEmail` row for the developer's email via SQL.
   - Log in with Google locally → verify access token in memory, refresh token in `localStorage`, `GET /api/users/me` returns profile.
   - Write + pass interceptor unit tests (step blocker for §5).

5. **Routing + guards + layout.**
   - `AuthGuard`, `AdminGuard`, `AppLayout`, `/`, `/profile`.
   - Verify unauthed `/transactions` redirects; admin links hidden for non-admin; `/admin/users` direct URL shows 403 as non-admin.
   - Write + pass guard tests.

6. **Broker accounts module** (simplest CRUD first).
   - `api/brokerAccounts.ts`, `pages/brokerAccounts/*`, query invalidation, confirm-delete.
   - End-to-end: create → edit → delete.

7. **Stock transactions module.**
   - Symbol filter bound to URL query param.
   - Broker picker fed by the broker-accounts query.
   - Dividend special case: hides qty/price, submits 0/0.
   - Verify symbol `"vnm"` entered returns `"VNM"` in response.

8. **Admin modules.**
   - Users: toggle `isAdmin`, soft-delete. UI disables self-demote / self-delete (backend is source of truth).
   - Allowed-emails: list, create, delete.
   - Toggle admin flag on self via DB, re-login, confirm admin pages appear.

9. **Polish.**
   - Global TanStack Query error handler → toast on non-401 errors.
   - 404 + 500 boundaries.
   - Loading skeletons on list pages.

10. **CI + Northflank.**
    - Add `GOOGLE_CLIENT_ID` GitHub secret.
    - Commit `deploy.yml` edit.
    - Push to `master` → watch GH Actions → confirm Northflank restarts → hit the prod URL → full smoke test (§below).

## Verification (end-to-end smoke test)

From repo root `D:\Fiolyn\`, after step 10:

1. `docker build -t fiolyn:smoke -f Fiolyn/Dockerfile --build-arg VITE_GOOGLE_CLIENT_ID=$CID .` — expect two clear stages in build output.
2. `docker run --rm -p 8080:8080 -e ConnectionStrings__NeonDatabaseUrl=... -e Jwt__SigningKey=... -e Jwt__Issuer=Fiolyn -e Jwt__Audience=FiolynWeb -e GoogleAuth__ClientId=$CID -e TelegramBotToken=... -e NotionSettings__NotionApiKey=... -e NotionSettings__NotionDatabaseId=... fiolyn:smoke`
3. Browser → `http://localhost:8080/` → login page renders.
4. Sign in → redirected to `/transactions`.
5. DevTools: `POST /api/auth/google` returns tokens; access token absent from localStorage; refresh token present under `fiolyn.refresh`.
6. Create a broker account → 201, list refreshes.
7. Create a Buy with symbol `vnm` → response has `"VNM"` and `"transactionType": "Buy"` (string, not 0).
8. Create a Dividend → qty/price hidden; server stores 0/0.
9. Edit, delete — list updates.
10. **Refresh coalescing:** clear the in-memory access token, fire 3 parallel queries — Network tab shows exactly one `/api/auth/refresh`, three retried originals.
11. **Admin gate:** non-admin visit `/admin/users` → 403 Alert page.
12. **Reuse detection:** replay a used refresh token via curl → second call 401; in the UI the next query triggers forced logout.
13. **Webhook regression:** `curl -X POST http://localhost:8080/api/Telegram -d '{}'` → 200.
14. **Swagger regression:** `http://localhost:8080/swagger` in Development → still renders.

## Critical files

Modified:
- `D:\Fiolyn\Fiolyn\Program.cs`
- `D:\Fiolyn\Fiolyn\ServiceRegisters\WebApiServiceRegister.cs`
- `D:\Fiolyn\Fiolyn\Fiolyn.csproj`
- `D:\Fiolyn\Fiolyn\Dockerfile`
- `D:\Fiolyn\.github\workflows\deploy.yml`
- `D:\Fiolyn\.gitignore`, `D:\Fiolyn\.dockerignore`

New (anchors):
- `D:\Fiolyn\Fiolyn.Web\vite.config.ts`
- `D:\Fiolyn\Fiolyn.Web\src\api\http.ts`
- `D:\Fiolyn\Fiolyn.Web\src\auth\AuthContext.tsx`
- `D:\Fiolyn\Fiolyn.Web\src\App.tsx`

## Risks / notes

- **Enum serialization** — resolved by adding `StringEnumConverter` in step 1. Do **not** skip this; the frontend type design assumes string enums throughout.
- **Google client ID in Docker** — baked in at build time via `VITE_GOOGLE_CLIENT_ID` build arg. Alternative (a `/api/config` endpoint that serves it at runtime) is more flexible but adds a request and a controller — not worth it for MVP.
- **Token storage** — `localStorage` refresh token is XSS-exposed; acceptable for this single-tenant admin app per the locked decision. Migration path is an httpOnly cookie on the backend if needed later.
- **Telegram webhook + fallback collision** — verified safe: explicit `[Route("api/Telegram")]` + `[HttpPost]` wins over `MapFallbackToFile` for the POST.
- **Swagger** — unaffected; registered as endpoints, not static files.
- **Testing scope** — only auth interceptor + guards covered. Form-submission tests (e.g., dividend qty=0 enforcement) intentionally deferred.
