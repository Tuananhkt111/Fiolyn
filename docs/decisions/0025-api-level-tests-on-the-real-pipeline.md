# 0025 — API-level tests run the real pipeline on an in-memory database

**Date:** 2026-11-10 · **Status:** Accepted

## Context

The FRS v1.0 conformance check found acceptance criteria that no test could reach. Every 401 and
403 in the API comes from an `[Authorize]` attribute or the Admin policy, and controller unit tests
construct the controller directly, so authentication and authorization never run. The same holds
for anything decided before the service: `[Range]` on a request, the sign an Expense controller
gives the amount, and a stock symbol's case on its way in and out. The concurrency tests threw a
mocked `DbUpdateConcurrencyException`, so no test showed that a concurrency token actually stops
a second writer.

## Decision

- `Fiolyn.Tests/Integration/Helpers/FiolynApiFactory` hosts the real app through
  `WebApplicationFactory<Program>` (`Microsoft.AspNetCore.Mvc.Testing`). It keeps routing, JWT
  bearer validation, the Admin policy, model validation and the exception handler. It swaps only
  the database, for EF's in-memory provider, and runs in a `Testing` environment so a local
  `appsettings.Development.json` never loads. Tokens are minted by the app's own `IJwtService`.
- The authorization tests list the endpoints from `docs/reference/openapi.json` and decide which are
  admin-only from the paths the FRS names. They don't read the attributes, so a missing or wrong
  attribute fails a test, and a new endpoint is covered without a code change.
- Races are forced deterministically. Two contexts share one in-memory store, and one request is
  made to finish while the other is still between its read and its save. EF's own
  concurrency-token check then picks the loser.

Rejected:
- Checking the attributes by reflection. It restates the code, and it can't catch a policy that
  is registered wrong or a token that validates when it shouldn't.
- Postgres in a container (Testcontainers). It is the only way to test unique-index races and
  `ExecuteUpdateAsync`, but it needs Docker on every machine and in CI. It is still open for those
  cases.

## Consequences

- The in-memory provider doesn't enforce unique indexes and can't run `ExecuteUpdateAsync`, so
  unique-violation races and bulk revocation stay on mocked exceptions.
- These tests boot the app, so they are slower than unit tests. They are kept to the criteria that
  only the pipeline decides; service logic stays in unit tests.
- A new admin area outside `/api/admin/`, `/api/allowed-emails` or the admin `/api/users` routes
  needs its path added to the authorization tests' rule.
