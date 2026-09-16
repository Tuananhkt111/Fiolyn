# 0012 — RFC 7807 errors, and the REST features deliberately skipped

**Date:** 2026-04-16 · **Status:** Accepted

## Context

The API originally returned `200 OK` for everything with a success flag in the body, and every
controller action wrapped its work in an identical `try`/`catch` that translated exceptions into
that envelope. Adding an endpoint meant copying the block again.

Separately, "align with REST best practice" is an unbounded task. Versioning, HATEOAS, rate
limiting, conditional requests and partial updates are all defensible, and all cost something. For
a service with one household's traffic and a first-party client, most of them buy nothing.

## Decision

**Adopted:**

- Real HTTP status codes — 201 with `Location` on create, 204 on delete, 404, 400, 401, 403, 409.
- **RFC 7807 `ProblemDetails`** as the single error shape, via `services.AddProblemDetails()`.
  Validation failures get the same shape for free through `[ApiController]`.
- A global `IExceptionHandler` (`GlobalExceptionHandler`) maps exception types to statuses:
  `KeyNotFoundException` → 404, `UnauthorizedAccessException` → 401, `ForbiddenAccessException`
  → 403, `BadRequestException` → 400, `ConflictException` → 409, anything else → 500. Controllers
  carry no `try`/`catch`. Exception messages are never leaked to the client.
- camelCase JSON, kebab-case plural routes, response DTOs that never expose entities.

**The one deliberate exception:** `TelegramController` always returns 200. Telegram retries any
non-2xx response, so reporting a processing failure honestly would produce a retry storm.

## Consequences — what was skipped, and why

These are *decisions*, not omissions. Reopen them when the stated premise stops being true:

| Skipped | Reason | Revisit when |
|---|---|---|
| **API versioning** | One first-party client, deployed with the backend | A third-party or unversioned mobile client appears |
| **HATEOAS / hypermedia** | No consumer would follow the links | Never, realistically |
| **Rate limiting** | Single-tenant behind Northflank | The API is exposed to untrusted callers |
| **ETag / conditional requests** | No caching pressure at this traffic | List responses get large or hot |
| **PATCH** | Entities are small; full-replacement PUT is unambiguous | An entity grows enough that clients cannot send it whole |

Two small inconsistencies that follow from the history and are knowingly tolerated:

- `/api/Telegram` is PascalCase, unlike every other route, because it uses the default
  `[controller]` token. Changing it means re-registering the webhook with Telegram.
- Controllers return `IActionResult` without `[ProducesResponseType]`, so the generated
  [`../reference/openapi.json`](../reference/openapi.json) documents routes, parameters and request
  bodies, but not response schemas.
