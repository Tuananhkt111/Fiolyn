# Plan: REST API Convention Improvements

## Context

The Fiolyn API recently moved from "always 200" to proper HTTP status codes. The next step is to align with REST best practices more broadly. This plan compares the current state against conventions and proposes concrete, high-impact improvements — scoped to what makes sense for a small personal API.

---

## Current State vs Best Practices

| Area | Current | REST Best Practice | Status |
|------|---------|-------------------|--------|
| Status codes | 201/200/204/404/500 | Correct | Done |
| Error format | RFC 7807 `ProblemDetails` | Industry standard | Done |
| Exception handling | Global `IExceptionHandler` | Centralized middleware | Done |
| JSON casing | camelCase | REST convention | Done |
| Validation errors | `ProblemDetails` shape | Consistent 400 format | Done |
| `Location` header | On POST via `CreatedAtAction` | Correct | Done |
| Response DTOs | Separate from entities | No `IsDeleted` leak | Done |
| Route naming | Kebab-case plurals | Correct | Done |
| HTTP verbs | GET/POST/PUT/DELETE/HEAD | Correct | Done |
| Pagination | None (returns full list) | `?page=1&pageSize=20` | Future |

---

## Changes Made

### 1. Global Exception Handler

Every controller action had identical try-catch blocks. A global `IExceptionHandler` centralizes this, making controllers thin and consistent.

- Created `Infrastructure/GlobalExceptionHandler.cs` implementing `IExceptionHandler` (.NET 8)
- Maps `KeyNotFoundException` to 404, other exceptions to 500
- Logs errors inside the handler
- Registered via `services.AddExceptionHandler<GlobalExceptionHandler>()`
- Added `app.UseExceptionHandler()` to `Program.cs` pipeline
- Removed all try-catch blocks from `BrokerAccountController` and `StockTransactionController`

### 2. ProblemDetails Error Format (RFC 7807)

`ProblemDetails` is the industry standard for HTTP API errors. ASP.NET 8 has built-in support via `services.AddProblemDetails()`. Clients get a predictable error shape.

- Registered `services.AddProblemDetails()` in service registration
- The global exception handler writes `ProblemDetails` responses
- Deleted custom `ErrorResponse.cs` and `ApiResponse.cs` (replaced by framework's `ProblemDetails`)
- Controllers return `NotFound()` without custom body

Example error response:
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "detail": "BrokerAccount 42 not found"
}
```

### 3. camelCase JSON Serialization

REST APIs conventionally use camelCase (`brokerAccountId`), not PascalCase (`BrokerAccountId`).

- Added `CamelCasePropertyNamesContractResolver` to Newtonsoft settings in `WebApiServiceRegister.cs`

### 4. Consistent Validation Error Responses (400)

`services.AddProblemDetails()` makes 400 validation errors return the same `ProblemDetails` shape. No extra configuration needed.

### 5. Response DTOs (separate from entities)

API responses no longer expose the `IsDeleted` property. Each response record has a constructor that accepts the entity for mapping.

- `BrokerAccountResponse` — Id, Name, Created, LastModified
- `StockTransactionResponse` — all fields except IsDeleted and BrokerAccount navigation

### 6. Proper HTTP Status Codes

| Endpoint | Status Codes |
|----------|-------------|
| `POST` create | 201 Created (with `Location` header) |
| `GET` list | 200 OK |
| `GET` by id | 200 OK / 404 Not Found |
| `HEAD` exists | 200 OK / 404 Not Found |
| `PUT` update | 200 OK / 404 Not Found |
| `DELETE` | 204 No Content / 404 Not Found |
| Exceptions | 500 Internal Server Error (via global handler) |
| Telegram webhook | 200 OK always (Telegram requirement) |

---

## Files Changed

| File | Change |
|------|--------|
| `Fiolyn/Infrastructure/GlobalExceptionHandler.cs` | New — centralized exception handling |
| `Fiolyn/Program.cs` | Added `app.UseExceptionHandler()` |
| `Fiolyn/ServiceRegisters/WebApiServiceRegister.cs` | Registered handler, ProblemDetails, camelCase |
| `Fiolyn/Controllers/BrokerAccountController.cs` | Removed try-catch, logger; added HEAD endpoint |
| `Fiolyn/Controllers/StockTransactionController.cs` | Removed try-catch, logger; added HEAD endpoint |
| `Fiolyn/Controllers/DTOs/Responses/BrokerAccount/BrokerAccountResponse.cs` | New — response DTO |
| `Fiolyn/Controllers/DTOs/Responses/StockTransaction/StockTransactionResponse.cs` | New — response DTO |
| `Fiolyn/Services/Internal/IBrokerAccountService.cs` | Added `ExistsAsync` |
| `Fiolyn/Services/Internal/BrokerAccountService.cs` | Implemented `ExistsAsync` |
| `Fiolyn/Services/Internal/IStockTransactionService.cs` | Added `ExistsAsync` |
| `Fiolyn/Services/Internal/StockTransactionService.cs` | Implemented `ExistsAsync` |
| `Fiolyn/Controllers/DTOs/Responses/ApiResponse.cs` | Deleted — replaced by direct responses |
| `Fiolyn/Controllers/DTOs/Responses/ErrorResponse.cs` | Deleted — replaced by ProblemDetails |

---

## Intentionally Skipped (overkill for project size)

- **API versioning** — single consumer (Telegram bot + personal use)
- **HATEOAS / hypermedia links** — adds complexity, no real consumer benefit
- **Rate limiting** — single-user API behind Northflank
- **ETag / conditional requests** — no high-traffic caching needs
- **PATCH** — entities are small; PUT for full replacement is fine
