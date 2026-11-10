# Telegram ingest

`POST /api/Telegram` receives Telegram Bot API webhook updates. A message either records a stock
transaction or is filed to Notion verbatim.

> **Canonical rules:** [rules/auth.md](../rules/auth.md) (sender resolution),
> [rules/transactions.md](../rules/transactions.md) (stock sign rules)

This is a second authentication path, entirely separate from the web API's JWT. It is the only
route that is not kebab-case — it uses the default `[controller]` token, and renaming it would mean
re-registering the webhook with Telegram. See
[0012](../decisions/0012-rest-conventions-and-non-goals.md).

## The endpoint always returns 200

Telegram retries any non-2xx response, so reporting a processing failure honestly would produce a
retry storm on a message that will never succeed. Every outcome — skipped, parse failure,
authorization failure, internal error — is reported in the 200 body, not the status code.

`TelegramController` is the only controller that behaves this way. Everything else uses real status
codes.

## Processing order

`TelegramMessageService.ProcessWebhookAsync` short-circuits at the first failing step:

1. **Shape check** — the payload must have `message.from`. Otherwise → skipped.
2. **Sender resolution** — `message.from.id` is matched against `AppUser.TelegramId`, restricted to
   active users by the global soft-delete filter. No match → skipped, and nothing is recorded. This
   is the whitelist; there is no separate table.
3. **Text check** — non-text messages (photos, stickers, voice) → skipped.
4. **Routing** — text starting with `stock ` (case-insensitive) goes to the parser; everything else
   is appended to Notion as a page.

The resolved `AppUser.Id` becomes the transaction's `PayerId`, and stock writes go through the same
`StockTransactionService.CreateAsync(transaction, userId)` as the REST API — so the broker
membership check applies identically. Telegram is not a privileged path.

## Stock command grammar

```
stock buy  {symbol} {qty} {price} {total} [named...]
stock sell {symbol} {qty} {price} {total} [named...]
stock div  {symbol} {total}                [named...]     lump sum
stock div  {symbol} {qty} {price}          [named...]     per-share
```

All positional numbers are non-negative integers (whole VND, whole shares); a negative one, or a
trade whose total comes out zero, is rejected as an error — see
[0001](../decisions/0001-money-as-long-not-decimal.md).

**Buy and sell** need all five positional values. `{total}` is the actual cash moved and is *not*
re-derived from `qty × price`, because it may include fees.

**Dividend** picks its form from how many positional numbers follow the symbol. One number is a
lump-sum total, and quantity and price stay zero. Two numbers are shares and per-share rate, and
the total becomes `qty × price`. A third number is malformed and the message is rejected. Named
parameters contain `:` and are not numeric, so they end the positional scan.

### Named parameters

Matched by the regex `(\w+):(.+?)(?=\s+\w+:|\s*$)` — a value runs until the next `key:` or the end
of the message, so values may contain spaces.

| Key | Required | Effect |
|---|---|---|
| `broker` | **yes** | Resolved by case-insensitive name to a `BrokerAccount` |
| `date` | no | Transaction date; defaults to UTC now |
| `notes` | no | Becomes `Description` |

```
stock buy VNM 100 45000 4501500 broker:VPS date:2026-04-14 notes:bought at support
stock div VNM 100 5000 broker:VPS notes:Q2 dividend
```

`broker:` is effectively required: an unresolvable or absent broker name makes the parse return
null, which the service reports as an invalid-format error.

### Broker name resolution is global; authorization is not

`ResolveBrokerAsync` matches the broker name across **all** broker accounts, without filtering by
the sender's membership. Authorization is enforced one step later, when
`StockTransactionService.CreateAsync` re-queries the broker with the membership predicate and
throws if the sender is not a member.

The practical consequence: naming a broker you cannot access is rejected as
`"Not authorized for broker"` rather than `"Invalid stock message format"`, and two brokers sharing
a name resolve to whichever the database returns first. Keep broker names distinct.

## Outcomes

| Body | Cause |
|---|---|
| success | Notion page created, or stock transaction recorded |
| skipped — *Missing message or from field* | Payload shape not a user message |
| skipped — *User not allowed* | `from.id` matches no active `AppUser.TelegramId` |
| skipped — *No text in message* / *Empty text* | Non-text or empty message |
| error — *Invalid stock message format* | Parse failed: too few values, non-numeric, or unresolvable broker |
| error — *Not authorized for broker* | Sender is not a member of the named broker account |
| error — *Invalid JSON* / *Internal error* | Malformed payload or unexpected failure |

All of these are HTTP 200.

## Linking a Telegram account

`AppUser.TelegramId` is set through `PUT /api/users/me/telegram-id` (self) or
`PUT /api/users/{id}/telegram-id` (admin), and cleared with the matching `DELETE`. The column has a
unique index, so linking an id already claimed by another user returns 409.

See [`../guides/local-development.md`](../guides/local-development.md) for testing the webhook
without Telegram.
