# Fiolyn Web App — Implementation Plan

## Context

The Fiolyn .NET 10 backend ships 23 REST endpoints (auth, users, broker accounts, stock transactions, allowed-emails) plus a Telegram webhook. There is **no web UI today** — users interact via Swagger or Telegram. This plan adds a first-party React SPA in a **separate repository** (`C:\FiolynWeb`), deployed to Vercel, calling the existing backend on Northflank over HTTPS.

**Two repos, two deployments, cross-origin:**
- `C:\Tuan Anh\Fiolyn` — existing .NET backend, Northflank, unchanged except for CORS + enum wiring.
- `C:\FiolynWeb` — new React SPA, Vercel, auto-deploys on push to `master`.

**End state:** `https://<vercel-host>/` returns the SPA; `https://<northflank-host>/api/*` serves the API; Google SSO logs the user in, tokens round-trip to localStorage/memory, and all CRUD for transactions / broker accounts / admin (users + allowed-emails) is reachable from the UI. Telegram webhook and Swagger continue to work unchanged.

## Decisions locked

- **Stack:** React 18 + Vite + TypeScript + Tailwind + shadcn/ui. TanStack Query for server state, react-hook-form + zod for forms, react-router-dom v6 for routing, axios for HTTP, `@react-oauth/google` for Google Sign-In.
- **Deploy:** `C:\FiolynWeb` → Vercel (GitHub integration, auto-deploys on push). Backend stays on Northflank unchanged. Cross-origin — CORS required on the backend.
- **Scope (MVP):** 1:1 with the API surface — login, profile, transaction CRUD (with symbol filter), broker-account CRUD, admin users page, admin allowed-emails page. No analytics, no charts.
- **Token storage:** access token in memory, refresh token in `localStorage` under key `fiolyn.refresh`. Acceptable trade-off for a single-tenant admin app. Migration path if XSS concerns emerge: switch to httpOnly cookie (one backend controller change, no frontend token-storage logic needed).

## File-level changes

### 1. New repo setup — `C:\FiolynWeb\`

**Git + Claude:**
- `README.md` — project overview, local dev setup (`npm install`, `npm run dev`), env var list, link to backend repo.
- `.gitignore` — `node_modules/`, `dist/`, `.vite/`, `coverage/`, `.env`.
- `.env.example` — `VITE_GOOGLE_CLIENT_ID=`, `VITE_API_BASE_URL=`.
- `.claude/CLAUDE.md` — frontend-specific conventions (see §below). Mirrors the backend's CLAUDE.md structure: overview, commands, config, architecture, code style, key rules, workflow.
- `.github/workflows/ci.yml` — on every push/PR: `npm ci`, `npm run type-check`, `npm run lint`, `npm run test`. Mirrors the backend's GitHub Actions pattern. Vercel deploys separately via its own GitHub integration (no deploy step in CI).

**CLAUDE.md content (`.claude/CLAUDE.md`):**
```markdown
# CLAUDE.md

## Project Overview
FiolynWeb is the React SPA for Fiolyn — a stock transaction manager.
Backend: https://github.com/<org>/Fiolyn (Northflank, .NET 10).
Stack: React 18 · Vite 5 · TypeScript 5 · Tailwind 3 · shadcn/ui ·
       TanStack Query 5 · react-hook-form · zod · axios · react-router-dom v6.

## Commands
npm run dev          # Vite dev server at http://localhost:5173
npm run build        # production build → dist/
npm run type-check   # tsc --noEmit
npm run lint         # eslint src/
npm run test         # vitest run

## Config
Local secrets go in .env (gitignored). Required vars:
  VITE_GOOGLE_CLIENT_ID — Google OAuth client ID
  VITE_API_BASE_URL     — full backend origin, e.g. http://localhost:5000

## Architecture
src/api/       — axios instance + per-resource wrappers + TS types
src/auth/      — tokenStore (in-memory), AuthContext, useAuth hook
src/components/— ui/ (shadcn), layout/, guards/, shared/
src/pages/     — one directory per route
src/forms/     — zod schemas

## Code Style
- TypeScript strict mode. No `any`.
- Named exports only (no default exports except pages).
- TanStack Query for all server state — no local useState for remote data.
- Path alias: @/* → src/*.
- Tailwind for all styling — no inline styles, no CSS modules.

## Key Rules
- VITE_API_BASE_URL must be set. http.ts throws at module load if falsy.
- Access token lives in memory only. Refresh token in localStorage under fiolyn.refresh.
- All list pages use QueryActive pattern: invalidate on mutation, no manual state sync.
- AuthGuard wraps all protected routes. AdminGuard wraps admin-only routes.

## Workflow
After every change: npm run type-check && npm run test (all must pass).
```

**Frontend scaffolding:**
- `package.json` — React 18, Vite 5, TypeScript 5, Tailwind 3, TanStack Query 5, axios, zod, react-hook-form, `@react-oauth/google`, Radix primitives, sonner, lucide-react, class-variance-authority, tailwind-merge, clsx. Dev: vitest, @testing-library/react, jsdom, msw, eslint, @vitejs/plugin-react.
- `tsconfig.json`, `tsconfig.node.json` — strict TS, path alias `@/* → src/*`.
- `vite.config.ts` — React plugin; default `build.outDir = 'dist'`; dev proxy `/api → http://localhost:5000` (local dev only); Vitest config (`jsdom`, `./src/test/setup.ts`).
- `vercel.json` — SPA routing fallback so deep links reload correctly:
  ```json
  {
    "rewrites": [{ "source": "/((?!api/).*)", "destination": "/index.html" }]
  }
  ```
- `tailwind.config.ts`, `postcss.config.js`, `index.html`, `.eslintrc.cjs`, `components.json` (shadcn config).
- `src/main.tsx` — mounts `<App/>`, wraps in `GoogleOAuthProvider`, `QueryClientProvider`, `BrowserRouter`, `<Toaster/>`.
- `src/App.tsx` — route table (see §5 below).
- `src/index.css` — Tailwind directives + shadcn CSS variables.
- `src/vite-env.d.ts` — augments `ImportMetaEnv` (adds `VITE_GOOGLE_CLIENT_ID` and `VITE_API_BASE_URL`).
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
- `api/http.ts` — single axios instance. `baseURL = import.meta.env.VITE_API_BASE_URL` (throw at module load time if falsy). Request interceptor attaches `Authorization: Bearer`. Response interceptor on 401 **coalesces concurrent 401s into one refresh** via a module-level `refreshPromise`; retries the original request once with the new token; on refresh failure, clears the store and forces logout. **Does not** retry `/api/auth/refresh` or `/api/auth/google`.
- `api/types.ts` — TS mirrors of backend DTOs. Key records:
  - `AuthResponse { accessToken; refreshToken; expiresAt }`
  - `UserResponse { id; email; displayName; pictureUrl; isAdmin; lastLoginAt; created }`
  - `BrokerAccountResponse { id; name; created; lastModified }`
  - `StockTransactionResponse { id; symbol; transactionType; quantity; price; totalAmount; transactionDate; brokerAccountId; brokerAccountName; notes; created; lastModified }`
  - `AllowedEmailResponse { id; email; note; invitedByUserId; created }`
  - `TransactionType = 'Buy' | 'Sell' | 'Dividend'` — **depends on §6 backend fix**.
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

Each page file ≤ ~150 lines, uses TanStack Query for list + mutations, shadcn Dialog for create/edit, `ConfirmDialog` for delete, form validation via `src/forms/*Schema.ts` (zod). Dividend transactions hide qty/price inputs and submit `{ quantity: 0, price: 0 }`. Symbol input is not client-normalized — the backend uppercases it.

### 6. Tests (minimum viable)

- `src/api/__tests__/http.test.ts` — (a) bearer attached, (b) concurrent 401s cause exactly one `/refresh` call, (c) refresh failure clears store + propagates 401, (d) no infinite loop when `/refresh` itself 401s.
- `src/components/guards/__tests__/AuthGuard.test.tsx` — unauthed → redirect.
- `src/components/guards/__tests__/AdminGuard.test.tsx` — non-admin → 403.

Mocks via `msw`.

### 7. Backend wiring — `C:\Tuan Anh\Fiolyn\`

No changes to `.gitignore`, `.dockerignore`, `Dockerfile`, or `deploy.yml` — the frontend is fully decoupled.

- **MODIFY `Fiolyn\ServiceRegisters\WebApiServiceRegister.cs`** — two changes:

  1. Add `StringEnumConverter` so `TransactionType` serializes as `"Buy"|"Sell"|"Dividend"` instead of `0|1|2`. Insert inside the existing `AddNewtonsoftJson` options lambda:
     ```csharp
     options.SerializerSettings.Converters.Add( new StringEnumConverter() ) ;
     ```
     (Add `using Newtonsoft.Json.Converters ;`.)

  2. Add a CORS policy that allows the Vercel origin:
     ```csharp
     services.AddCors( options =>
     {
       options.AddDefaultPolicy( policy =>
         policy
           .WithOrigins( corsSettings.AllowedOrigins )
           .AllowAnyHeader()
           .AllowAnyMethod() ) ;
     } ) ;
     ```
     `AllowedOrigins` is bound from `CorsSettings` — see §8 for values per environment.

- **NEW `Fiolyn\ServiceRegisters\Settings\CorsSettings.cs`**:
  ```csharp
  public class CorsSettings
  {
    public string[] AllowedOrigins { get ; set ; } = [] ;
  }
  ```

- **MODIFY `Fiolyn\Program.cs`** — add `UseCors()` in the correct position. Final middleware order:
  ```
  app.UseExceptionHandler() ;
  app.UseCors() ;
  app.UseAuthentication() ;
  app.UseAuthorization() ;
  app.MapControllers() ;
  ```
  `UseCors` must come before `UseAuthentication`.

### 8. Config

**Backend (Northflank env vars):**
- `Cors__AllowedOrigins__0=https://<vercel-host>` (the production Vercel URL)
- All existing secrets unchanged.

**Backend (local `Fiolyn/appsettings.Development.json`, gitignored):**
```json
{
  "GoogleAuth": { "ClientId": "<dev client id>" },
  "Cors": { "AllowedOrigins": ["http://localhost:5173"] }
}
```

**Frontend (Vercel dashboard — Environment Variables):**
- `VITE_GOOGLE_CLIENT_ID` = Google OAuth client ID
- `VITE_API_BASE_URL` = `https://<northflank-host>` (no trailing slash)

**Frontend (local `C:\FiolynWeb\.env`, gitignored):**
```
VITE_GOOGLE_CLIENT_ID=<dev client id>
VITE_API_BASE_URL=http://localhost:5000
```

**Google OAuth client:** add both the Vercel production domain and `http://localhost:5173` to **Authorized JavaScript origins** in Google Cloud Console.

## Implementation order

Each step ends in a verifiable working state.

**Frontend:** `npm run type-check && npm run test` after every change.
**Backend:** `dotnet build` + `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj` after every change (per CLAUDE.md).

1. **Backend CORS + enum fix.**
   - Add `CorsSettings`, wire `AddCors` in `WebApiServiceRegister`, add `UseCors()` to `Program.cs`.
   - Add `StringEnumConverter`.
   - `dotnet run` locally; curl with `Origin: http://localhost:5173` → verify `Access-Control-Allow-Origin` header returned.
   - Verify `/api/stock-transactions` response includes `"transactionType": "Buy"` (string), not a number.
   - Run diagnostics + tests.

2. **Create `C:\FiolynWeb` repo.**
   - `git init`, create GitHub repo, set `master` as default branch (mirroring backend convention).
   - Add `.claude/CLAUDE.md`, `.gitignore`, `.env.example`, `README.md`.
   - First commit: repo skeleton only.

3. **Scaffold empty SPA.**
   - Add `package.json`, `vite.config.ts`, `tsconfig`, Tailwind pipeline, shadcn init, `vercel.json`, `src/main.tsx` rendering `<h1>Fiolyn</h1>`.
   - `npm install && npm run dev` → `http://localhost:5173` shows the stub.
   - `npm run build` → `dist/` produced. `npm run type-check && npm run lint` — clean.
   - Add `.github/workflows/ci.yml`; push → CI passes.

4. **Vercel project setup.**
   - Connect the new GitHub repo to a Vercel project (root directory = repo root).
   - Set `VITE_GOOGLE_CLIENT_ID` and `VITE_API_BASE_URL` env vars in Vercel.
   - Push to `master` → Vercel auto-deploys → `https://<vercel-host>/` shows the React stub. Full deploy pipeline validated before any auth code.

5. **Auth layer.**
   - Implement `tokenStore`, `http.ts` (with coalescing interceptor), `api/auth.ts`, `AuthContext`, `LoginPage`.
   - Seed one `AllowedEmail` row for the developer's email via SQL.
   - Log in with Google locally → verify access token in memory, refresh token in `localStorage`, `GET /api/users/me` returns profile.
   - Write + pass interceptor unit tests (step blocker for §6).

6. **Routing + guards + layout.**
   - `AuthGuard`, `AdminGuard`, `AppLayout`, `/`, `/profile`.
   - Verify unauthed `/transactions` redirects; admin links hidden for non-admin; `/admin/users` direct URL shows 403 as non-admin.
   - Verify deep-link reload works (Vercel rewrite rule serves `index.html`).
   - Write + pass guard tests.

7. **Broker accounts module** (simplest CRUD first).
   - `api/brokerAccounts.ts`, `pages/brokerAccounts/*`, query invalidation, confirm-delete.
   - End-to-end: create → edit → delete.

8. **Stock transactions module.**
   - Symbol filter bound to URL query param.
   - Broker picker fed by the broker-accounts query.
   - Dividend special case: hides qty/price, submits 0/0.
   - Verify symbol `"vnm"` entered returns `"VNM"` in response.

9. **Admin modules.**
   - Users: toggle `isAdmin`, soft-delete. UI disables self-demote / self-delete (backend is source of truth).
   - Allowed-emails: list, create, delete.
   - Toggle admin flag on self via DB, re-login, confirm admin pages appear.

10. **Polish.**
    - Global TanStack Query error handler → toast on non-401 errors.
    - 404 + 500 boundaries.
    - Loading skeletons on list pages.

11. **Production deploy.**
    - Set `Cors__AllowedOrigins__0` on Northflank to the production Vercel URL.
    - Add production Vercel domain to Google OAuth authorized origins.
    - Push to `master` on both repos → auto-deploy → full smoke test (§below).

## Verification (end-to-end smoke test)

From a browser at `https://<vercel-host>/`:

1. Login page renders.
2. Sign in → redirected to `/transactions`.
3. DevTools: `POST https://<northflank-host>/api/auth/google` returns tokens; access token absent from localStorage; refresh token present under `fiolyn.refresh`. `Access-Control-Allow-Origin` header present on all API responses.
4. Create a broker account → 201, list refreshes.
5. Create a Buy with symbol `vnm` → response has `"VNM"` and `"transactionType": "Buy"` (string, not 0).
6. Create a Dividend → qty/price hidden; server stores 0/0.
7. Edit, delete — list updates.
8. **Deep link:** navigate directly to `https://<vercel-host>/transactions` (browser reload) → page renders (Vercel rewrite rule working).
9. **Refresh coalescing:** clear the in-memory access token, fire 3 parallel queries — Network tab shows exactly one `/api/auth/refresh`, three retried originals.
10. **Admin gate:** non-admin visit `/admin/users` → 403 Alert page.
11. **Reuse detection:** replay a used refresh token via curl → second call 401; in the UI the next query triggers forced logout.
12. **Webhook regression:** `curl -X POST https://<northflank-host>/api/Telegram -d '{}'` → 200.
13. **Swagger regression:** `https://<northflank-host>/swagger` in Development → still renders.

## Critical files

**Backend repo (`C:\Tuan Anh\Fiolyn\`) — modified:**
- `Fiolyn/Program.cs`
- `Fiolyn/ServiceRegisters/WebApiServiceRegister.cs`

**Backend repo — new:**
- `Fiolyn/ServiceRegisters/Settings/CorsSettings.cs`

**Frontend repo (`C:\FiolynWeb\`) — new (anchors):**
- `.claude/CLAUDE.md`
- `.github/workflows/ci.yml`
- `vercel.json`
- `vite.config.ts`
- `src/api/http.ts`
- `src/auth/AuthContext.tsx`
- `src/App.tsx`

## Risks / notes

- **CORS misconfiguration** — the most likely failure mode. `UseCors()` must come before `UseAuthentication()` in `Program.cs`. The `AllowedOrigins` list must exactly match the Vercel origin (scheme + host, no trailing slash). Preflight failures show as network errors in the browser, not 401s.
- **Enum serialization** — resolved by `StringEnumConverter` in step 1. Do **not** skip; the frontend type design assumes string enums throughout.
- **Google OAuth authorized origins** — must include both the Vercel production URL and `http://localhost:5173` for local dev. Missing this causes Google Sign-In to silently fail with `origin_mismatch`.
- **Vercel preview deployments** — each PR gets a unique preview URL, which won't be in the backend's `AllowedOrigins`. For now, only the main branch (production) URL is whitelisted. Add wildcard subdomain support later if preview-branch E2E testing is needed.
- **Telegram webhook + CORS** — the Telegram webhook POSTs from Telegram's servers, not a browser, so CORS headers are irrelevant for that endpoint. No special handling needed.
- **Swagger** — unaffected; registered as endpoints, served by the backend independently of the frontend deployment.
- **Testing scope** — only auth interceptor + guards covered. Form-submission tests (e.g., dividend qty=0 enforcement) intentionally deferred.
