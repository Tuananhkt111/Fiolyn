# Serialization Rules

Read this when adding or changing a request/response DTO, an enum that crosses the wire, any `DateTime` property, or when parsing an external payload.

> **Configured in:** `ServiceRegisters/WebApiServiceRegister.cs` ·
> **Wire contract:** [docs/reference/openapi.json](../reference/openapi.json) ·
> **Why:** [ADR 0012 — REST conventions and non-goals](../decisions/0012-rest-conventions-and-non-goals.md)

No reference-doc twin — this is implementation rules only.

## Two JSON stacks — know which one you're in

| Where | Stack |
|---|---|
| MVC controllers (all request/response DTOs) | **Newtonsoft.Json** via `AddNewtonsoftJson` |
| `NotionService`, `TelegramMessageService` (external payloads) | **System.Text.Json** |
| Error bodies (`GlobalExceptionHandler` → `WriteAsJsonAsync`) | **System.Text.Json** |

That third row is easy to miss: the settings configured below — enum-as-string, UTC date handling,
the camelCase resolver — apply to success responses but **not** to error payloads, which go through
a different serializer. It happens not to matter today because `ProblemDetails` has no enums or
dates and STJ camelCases by default. Adding a field to the error contract would change that.

This trips people up constantly. On a **DTO**, `[JsonPropertyName]` (System.Text.Json) is silently ignored — the attribute Newtonsoft honours is `[JsonProperty]`. You rarely need either, because the contract resolver handles casing.

Don't "standardise" one service onto the other stack without checking: the external parsers use `JsonDocument`/`TryGetProperty` against payloads that Fiolyn doesn't control, which is a different job from DTO binding.

## camelCase is automatic

`CamelCasePropertyNamesContractResolver` is registered globally. Write DTO properties in PascalCase and let the resolver emit `brokerAccountId`. Never hand-name a JSON property to force casing.

## `DateTime` must be UTC — this is a database constraint

`DateTimeZoneHandling = DateTimeZoneHandling.Utc` is set deliberately. Postgres columns are `timestamptz`, and **Npgsql rejects a `DateTime` write whose `Kind` is not `Utc`** with an `InvalidCastException` at save time, not at bind time.

The setting treats an incoming date without an offset as UTC and converts one with an offset to UTC. Consequences for code you write:

- When defaulting a date server-side use `DateTime.UtcNow`, never `DateTime.Now`.
- Don't call `.ToLocalTime()` on anything heading for the database.
- A `DateTime` built in a test with `new DateTime(...)` has `Kind.Unspecified`; use `DateTime.SpecifyKind(..., DateTimeKind.Utc)` or `DateTime.UtcNow` if it will be persisted.

## Enums cross the wire as strings

`StringEnumConverter` is registered, so `StockTransactionType.Buy` serializes as `"Buy"`, not `0`. Clients depend on this. Don't add an `int`-backed DTO field to "simplify" an enum, and don't reorder enum members assuming ordinals are private — they aren't visible on the wire, but they *are* stored in the database.

## Reference loops are ignored, not an excuse

`ReferenceLoopHandling.Ignore` stops a nav cycle from throwing during serialization. It is a safety net, not permission to return entities — response DTOs are still mandatory (see `CLAUDE.md`, "Never return raw entities").

## OpenAPI schema ids

Schema ids are **short type names**, so a DTO record name must be unique across the entire DTO tree — the folder it sits in is not part of the id.

Swashbuckle throws on a duplicate and **500s the entire `/swagger` document**, not just the affected types. It has happened once: `AddMemberRequest` existed under both `Requests/SpendAccount/` and `Requests/BrokerAccount/` as byte-identical copies. The fix was to keep one copy in `Requests/Shared/`, not to configure a custom naming scheme — a shared shape wants one record, and a custom scheme would have hidden the duplication behind generated prefixes.

If two resources need the same request shape, put it in `Requests/Shared/`. If they need *different* shapes, give them different names.

Controllers return `IActionResult` with no `[ProducesResponseType]`, so `docs/reference/openapi.json` documents routes, parameters and request bodies but **not** response schemas. Don't assume a response shape is covered by the generated document — see [ADR 0012](../decisions/0012-rest-conventions-and-non-goals.md).
