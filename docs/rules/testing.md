# Testing Rules

Read this when adding or changing tests under `Fiolyn.Tests/`.

> **Which tests to run after a change:** `.claude/CLAUDE.md` → Workflow ·
> **Paged fixtures:** [pagination.md](pagination.md)

No reference-doc twin — this is implementation rules only.

Stack: xUnit · NSubstitute · FluentAssertions · MockQueryable. `Xunit` and `Fiolyn.Common.Pagination` are global usings — don't re-import them.

Layout: `Unit/Services/`, `Unit/Controllers/`, `Unit/Common/`, `Integration/Data/`, `Helpers/`. One test class per production class, named `<ClassName>Tests`.

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

## Simulating concurrency and conflicts

These paths have real branches and are easy to leave untested:

```csharp
_repository.SaveAsync().ThrowsAsync( new DbUpdateConcurrencyException() ) ;   // → 409 / 401
```

For unique-violation mapping, throw a `DbUpdateException` whose inner is a `PostgresException` with `SqlState` `23505`. Needs `using NSubstitute.ExceptionExtensions ;`.

## Assertions

FluentAssertions throughout — `result.Should().BeOfType<OkObjectResult>()`, `act.Should().ThrowAsync<BadRequestException>()`. Assert the paged shape as `result.Items.Should().HaveCount(n)` plus `result.TotalCount.Should().Be(n)`, not just the item count.

## Which tests to run

Map each changed production file to its `*Tests.cs` and filter (see `CLAUDE.md`, Workflow). Run the full suite for cross-cutting changes — `IRepository<T>`, `AppDbContext`, `ServiceRegisters/*`, shared base classes — and always as the pre-commit gate.

## Tests for a logic change

A logic change is a new or changed branch, calculation, validation, state change or error mapping. Its tests are written by a fresh agent that follows `.claude/skills/test-gen/skill.md` and gets the requirement and the file paths, not the conversation. Take expected values from the requirement, never from the production formula. Then the `test-reviewer` agent checks them for tautologies, mocks that return the asserted value, and uncovered acceptance criteria. Fix every high, tautology or mock finding; list the rest in the PR — [0017](../decisions/0017-tests-written-and-reviewed-apart-from-the-code.md).

## Changed lines no test runs

Before a PR, run `python scripts/coverage-changed.py` (base defaults to `master`). Give each line it lists a test, or say in the PR why it has none (startup wiring, EF configuration). It reports and never fails — [0016](../decisions/0016-changed-line-coverage-reported-not-gated.md).
