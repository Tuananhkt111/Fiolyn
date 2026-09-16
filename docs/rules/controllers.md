# Controller Rules

Read this when adding or changing anything under `Fiolyn/Controllers/`, including request and response DTOs.

> **What it does** (routes, verbs, status codes, request shapes): [reference/openapi.json](../reference/openapi.json) ·
> **Why:** [ADR 0012 — REST conventions and non-goals](../decisions/0012-rest-conventions-and-non-goals.md) ·
> **Serialization:** [serialization.md](serialization.md)

## Controllers stay thin

Controller → service → `IRepository<T>`. A controller binds the request, pipes `User.GetUserId()` into the service call, and maps the result to a response DTO. Business logic, membership checks and validation beyond data annotations belong in the service.

Services throw; controllers don't catch. `Infrastructure/GlobalExceptionHandler` maps the exception to a status code — see [ADR 0012](../decisions/0012-rest-conventions-and-non-goals.md). A `try`/`catch` in a controller action is almost always wrong.

## Never return raw entities

Every response goes through a `record` DTO in `Controllers/DTOs/Responses/{Resource}/`. Returning an entity leaks `IsDeleted`, audit columns and whatever navs happen to be loaded, and couples the wire contract to the schema.

Request records live in `Controllers/DTOs/Requests/{Resource}/` with `[Required]` / `[MaxLength]` annotations — `[ApiController]` turns a validation failure into a 400 automatically, so don't hand-check required fields.

One record per file, file name matching the record. **Record names must be unique across the whole DTO tree**, not just within their resource folder — OpenAPI schema ids are short type names, and a duplicate 500s the entire `/swagger` document. When two resources genuinely need the same request shape, put one copy in `Requests/Shared/` rather than duplicating it per folder. See [serialization.md](serialization.md#openapi-schema-ids).

## Refetch through the service's read method when a write response reads through a nav

`IRepository<T>.GetByIdAsync` does **not** eager-load; service-layer reads do, via `.Include(...)`. So a write response that reads `x.SomeNav.Name` needs a refetch through the read method. Two cases:

- **Create.** `AddAsync` + `SaveAsync` populates only the FK. The nav is still null, and the response builder will throw.
- **Update where an FK actually changed.** EF does not refresh the in-memory nav when you reassign the scalar FK, so the old nav is stale and the response would show the *previous* related row.

Capture the old FK before mutating, compare, and refetch **only when it changed** — an unchanged update must not pay for the extra read. See `BrokerAccountController.UpdateAsync` and `StockTransactionController.UpdateAsync`.

## `TelegramController` always returns 200

Telegram retries any non-2xx response, so an honest error status produces a retry storm on a message that will never succeed. Every outcome is reported in the 200 body instead.

This applies to `TelegramController` only. **Every other controller uses real status codes** — 201 with `Location` on create, 204 on delete, and the mapped error codes. Don't generalise the always-200 pattern.

## List endpoints

Every list GET takes `[FromQuery] PaginationQuery pagination` and returns `PagedResult<TResponse>` via `page.Map(...)`. Single-item GETs and HEAD endpoints stay unpaged. See [pagination.md](pagination.md).
