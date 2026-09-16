# Error responses

What a client receives when a request fails.

> **Canonical rules:** [rules/controllers.md](../rules/controllers.md) (services throw, controllers don't catch) ·
> **Why:** [ADR 0012 — RFC 7807 and the non-goals](../decisions/0012-rest-conventions-and-non-goals.md)

**`openapi.json` does not cover this.** Because no controller declares `[ProducesResponseType]`, the
generated document describes only `200` responses. This file is the only description of the error
contract — see [ADR 0012](../decisions/0012-rest-conventions-and-non-goals.md).

## Shape

`Infrastructure/GlobalExceptionHandler` catches everything a service throws and writes an RFC 7807
`ProblemDetails` body:

```json
{
  "status": 404,
  "title": "Not Found",
  "detail": "Not Found"
}
```

**`detail` always equals `title`.** The handler sets them from the same string, deliberately: an
exception message can carry entity ids, email addresses or SQL fragments, and none of that should
reach a client. The cost is that the body carries no actionable information beyond the status code —
a client distinguishes failures by status, not by reading `detail`.

The reason for a failure is in the server logs, and only `500` is logged — the mapped statuses are
expected outcomes, not faults.

## Status mapping

| Exception | Status | `title` / `detail` | Typical cause |
|---|---|---|---|
| `KeyNotFoundException` | 404 | `Not Found` | Row missing, **or** caller not a member of the resource |
| `UnauthorizedAccessException` | 401 | `Unauthorized` | Refresh token invalid, revoked, or lost a rotation race |
| `ForbiddenAccessException` | 403 | `Forbidden` | Authenticated but not permitted |
| `BadRequestException` | 400 | `Bad Request` | Domain rule violated — last member removal, deleting a referenced row, invalid share percentages |
| `ConflictException` | 409 | `Conflict` | Unique-index violation, or a concurrent balance write lost the `[ConcurrencyCheck]` race |
| anything else | 500 | `Internal Server Error` | Unhandled. Logged with the exception. |

**404 is deliberately ambiguous.** "Not a member" and "does not exist" return the same response, so a
caller can't probe for the existence of resources they can't see. Don't read a 404 as proof a row is
absent.

Only three custom exception types exist — `BadRequestException`, `ConflictException`,
`ForbiddenAccessException` (`Infrastructure/Exceptions/`). The other two branches reuse BCL types.

## Validation errors are a different shape

A request that fails data-annotation validation never reaches a service, so it never reaches
`GlobalExceptionHandler`. `[ApiController]` short-circuits it and ASP.NET Core emits a
`ValidationProblemDetails` — same `application/problem+json` family, but with a per-field `errors`
map:

```json
{
  "status": 400,
  "title": "One or more validation errors occurred.",
  "errors": {
    "email": [ "The Email field is not a valid e-mail address." ]
  }
}
```

So **400 has two possible shapes**: this one for annotation failures, and the flat one above for
`BadRequestException` thrown by a service. A client that parses `errors` must treat it as optional.

Out-of-range `page` / `pageSize` arrive this way — see [rules/pagination.md](../rules/pagination.md).

## The Telegram webhook never does this

`POST /api/Telegram` always returns 200 and reports failures in the body, because Telegram retries
non-2xx responses. It is the one endpoint that does not use this contract — see
[telegram-ingest.md](telegram-ingest.md).

## Serialization

Error bodies are written with `WriteAsJsonAsync`, which uses **System.Text.Json** — not the
Newtonsoft pipeline that serializes successful responses. Both emit camelCase keys, so the
difference is invisible on the wire, but it means the global Newtonsoft settings (enum-as-string,
UTC date handling) do not apply to error payloads. See
[rules/serialization.md](../rules/serialization.md).
