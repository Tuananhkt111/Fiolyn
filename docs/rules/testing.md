# Testing Rules

Read this when adding or changing tests under `Fiolyn.Tests/`.

> **Which tests to run after a change:** `.claude/CLAUDE.md` → Workflow ·
> **Paged fixtures:** [pagination.md](pagination.md)

No reference-doc twin — this is implementation rules only.

Stack: xUnit · NSubstitute · FluentAssertions · MockQueryable. `Xunit` and `Fiolyn.Common.Pagination` are global usings — don't re-import them.

Layout: `Unit/Services/`, `Unit/Controllers/`, `Unit/Common/`, `Integration/Data/`, `Integration/Api/`, `Helpers/`. One test class per production class, named `<ClassName>Tests`.

## Stub `IRepository<T>.Query()` with `.BuildMock()`

`Query()` returns `IQueryable<T>`, and the service chains EF async operators (`FirstOrDefaultAsync`, `AnyAsync`, `ToPagedResultAsync`) onto it. A plain `List<T>.AsQueryable()` has no async provider and throws at the first `await`.

```csharp
_repository.Query().Returns( rows.ToList().BuildMock() ) ;   // MockQueryable
```

**Seed every injected repository in the constructor**, including ones the test under test doesn't exercise — an unstubbed `Query()` returns null and fails with a `NullReferenceException` far from the cause:

```csharp
public CounterpartyServiceTests()
{
  _lendRepo.Query().Returns( new List<LendTransaction>().BuildMock() ) ;
  _borrowingRepo.Query().Returns( new List<BorrowingTransaction>().BuildMock() ) ;
}
```

## `CreateSut()`, not a field

Build the system under test in a private `CreateSut()` method so each test can vary the setup before construction. Add `SetupXxx( params T[] )` helpers for seeding — the pattern keeps `[Fact]` bodies about behaviour rather than fixture assembly.

## Controller tests need a `ClaimsPrincipal`

Controllers read the caller through `User.GetUserId()`, so a bare `new SomeController(service)` throws. Give `CreateSut` a `callerId` parameter and attach the identity:

```csharp
private SomeController CreateSut( long callerId = 7 )
{
  var controller = new SomeController( _service ) ;
  var identity = new ClaimsIdentity(
    [new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )], "TestAuth" ) ;
  controller.ControllerContext = new ControllerContext
  {
    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal( identity ) }
  } ;
  return controller ;
}
```

## Paged fixtures

Controller tests stub the service, not the query — use `Arg.Any<PaginationQuery>()` for the argument and `PagedResultHelpers.SinglePage(...)` / `.AsSinglePage()` / `.EmptyPage<T>()` for the return. Don't re-test paging mechanics per service; `PagedQueryExtensionsTests` owns that. See [pagination.md](pagination.md).

## Typed-HttpClient services

Inject a real `HttpClient` wrapping `MockHttpMessageHandler` (`Helpers/`) rather than substituting `HttpClient` — it is not mockable. The handler records `LastRequest` and `LastRequestContent`, so assert the outgoing payload there.

## API-level tests: when the pipeline decides

Some outcomes are decided before a service runs: 401 and 403 from `[Authorize]` and the Admin policy, 400 from a request's `[Range]`, and the sign a controller gives an amount. Controller unit tests can't see any of these. Test them through `Integration/Helpers/FiolynApiFactory`, which hosts the real app on EF's in-memory provider: `As( user )` sends a token the app minted itself, `SeedAsync` and `QueryAsync` reach the same store. Read results back from the store or the API, never from the object you seeded. Keep service logic in unit tests: these boot the app ([0025](../decisions/0025-api-level-tests-on-the-real-pipeline.md)).

- `Integration/Api/AuthorizationTests` lists the endpoints from `openapi.json` and decides which are admin-only from their paths. A new admin area outside `/api/admin/`, `/api/allowed-emails` and the admin `/api/users` routes needs its path added to that rule.
- A real race: give each request its own context over one shared in-memory store, and make one finish while the other is between its read and its save (a `SaveChangesInterceptor` works). See `Integration/Data/ConcurrencyRaceTests`. The in-memory store has no rollback and doesn't enforce unique indexes, so what a loser leaves behind, and unique-violation races, still need a mocked exception or a relational provider.

## Simulating concurrency and conflicts

These paths have real branches and are easy to leave untested:

```csharp
_repository.SaveAsync().ThrowsAsync( new DbUpdateConcurrencyException() ) ;   // → 409 / 401
```

For unique-violation mapping, throw a `DbUpdateException` whose inner is a `PostgresException` with `SqlState` `23505`. Needs `using NSubstitute.ExceptionExtensions ;`.

## Soft-delete and query filters need an integration test

MockQueryable does not apply EF's global query filters: a soft-deleted row in a mock is visible to every query. A unit test therefore cannot tell whether a lookup uses `.IgnoreQueryFilters()` — removing it leaves every unit test green. When a behaviour depends on seeing (or not seeing) deleted rows, add a test in `Integration/Data/` against `TestDbContextFactory.Create()` and the real `Repository<T>`; the in-memory provider applies the filters. See `SoftDeleteRestoreTests` ([0024](../decisions/0024-recreating-a-deleted-row-restores-it.md)).

## Assertions

FluentAssertions throughout — `result.Should().BeOfType<OkObjectResult>()`, `act.Should().ThrowAsync<BadRequestException>()`. Assert the paged shape as `result.Items.Should().HaveCount(n)` plus `result.TotalCount.Should().Be(n)`, not just the item count.

## Which tests to run

Map each changed production file to its `*Tests.cs` and filter (see `CLAUDE.md`, Workflow). Run the full suite for cross-cutting changes — `IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes — and always as the pre-commit gate.

## Tests for a logic change

A logic change is a new or changed branch, calculation, validation, state change or error mapping. Its tests are written by a fresh agent that follows `.claude/skills/test-gen/skill.md` and gets the requirement and the file paths, not the conversation. Take expected values from the requirement, never from the production formula. Then the `test-reviewer` agent checks them for tautologies, mocks that return the asserted value, and uncovered acceptance criteria. Fix every high, tautology or mock finding; list the rest in the PR — [0017](../decisions/0017-tests-written-and-reviewed-apart-from-the-code.md).

## Tag tests with the FRS story they cover

A test that exercises an FRS v1.0 story carries `[Trait( "Story", "US-nn" )]` under its `[Fact]` or `[Theory]`, one per story, and is listed on that story's `**Tests:**` line in `FRS/module-*.md`. Run one story's tests with `--filter "Story=US-01"`. docs-lint fails when the line and the tags disagree, in either direction, or when a class on the story's `**Code:**` line is gone. A test that covers no story gets no trait — [0026](../decisions/0026-frs-stories-linked-to-code-and-tests.md).

## Changed lines no test runs

Before a PR, run `python scripts/coverage-changed.py` (base defaults to `master`). Give each line it lists a test, or say in the PR why it has none (startup wiring, EF configuration). It reports and never fails — [0016](../decisions/0016-changed-line-coverage-reported-not-gated.md).
