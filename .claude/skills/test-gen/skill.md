---
name: test-gen
description: Generate xUnit test cases for a service or controller. Reads the target file, identifies all methods and dependencies, and writes a test class following project conventions. Use when asked to generate tests, write tests for, or add tests to a file.
argument-hint: "<path-to-source-file>"
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, mcp__ide__getDiagnostics
model: sonnet
effort: high
---

# Test Generator

## Instructions

Generate a complete xUnit test class for a given source file, following the project's test conventions.

---

## Step 0: Parse Input

Accept one argument: a path to the source file to test. It may be:
- A relative path from repo root (e.g., `Fiolyn/Services/External/Telegram/TelegramMessageService.cs`)
- A file name alone (e.g., `TelegramMessageService.cs`) — search the repo to resolve it

If no argument is provided, ask the user which file to generate tests for.

---

## Step 1: Read the Target File

Read the source file and identify:

1. **Class name** and **namespace**
2. **Constructor parameters** (dependencies to mock)
3. **Public methods** — name, parameters, return type, `async`/`await`
4. **Interfaces implemented** — informs what the class contracts to do
5. **Layer** — determines test type:

| File location | Test type | Placement |
|---|---|---|
| `Services/External/` or `Services/Internal/` | Unit | `Fiolyn.Tests/Unit/Services/` |
| `Controllers/` | Unit | `Fiolyn.Tests/Unit/Controllers/` |
| `Data/Repositories/` | Integration | `Fiolyn.Tests/Integration/Data/` |
| `Data/AppDbContext.cs` | Integration | `Fiolyn.Tests/Integration/Data/` |

---

## Step 2: Inspect Existing Tests (for context and patterns)

Before writing, **read the most similar existing test file** to match style exactly:

- New service → read `Fiolyn.Tests/Unit/Services/TelegramMessageServiceTests.cs`
- New controller → read `Fiolyn.Tests/Unit/Controllers/TelegramControllerTests.cs`
- Repository/DbContext → read `Fiolyn.Tests/Integration/Data/RepositoryTests.cs`

Also grep for any existing test file for this class:
```
Grep "{ClassName}Tests" path:Fiolyn.Tests/
```

If a test file already exists, **extend it** (add missing test cases) rather than overwrite.

---

## Step 3: Identify Test Cases

For each public method, generate test scenarios covering:

### Service methods
| Scenario type | Test name pattern |
|---|---|
| Happy path | `MethodName_ValidInput_Returns{Expected}` |
| Missing / null input | `MethodName_Missing{Field}_Returns{Skipped\|Null}` |
| Unauthorized / not found | `MethodName_{Condition}_Returns{Skipped}` |
| Dependency throws | `MethodName_{Dependency}Throws_Returns{Error\|Throws}` |
| Dependency called with correct args | `MethodName_Valid_Calls{Dependency}Once` |
| Dependency NOT called | `MethodName_{Condition}_Does Not Call{Dependency}` |

### Controller methods
| Scenario | Expected |
|---|---|
| Service returns `Success` | HTTP 200 |
| Service returns `Skipped` | HTTP 200 |
| Service returns `Error` | HTTP 200 |
| Controller always returns 200 | Verify regardless of outcome |

### HTTP client services (e.g., NotionService)
| Scenario | Expected |
|---|---|
| HTTP 200 response | Completes without throwing |
| HTTP 4xx/5xx response | Throws `InvalidOperationException` with status name in message |
| Request payload | Contains expected fields (database_id, body content) |
| Auth header | Contains `Bearer {token}` in `Authorization` header |

---

## Step 4: Write the Test File

**File path:** `Fiolyn.Tests/{Unit|Integration}/{Layer}/{ClassName}Tests.cs`

### Code style rules (must follow exactly)
- 2-space indentation, no tabs
- Spaces inside all parentheses: `new Foo( bar )`
- Opening brace `{` on its own line for types and methods
- Primary constructors for test classes where applicable
- `var` when type is obvious
- `async Task` for all async tests

### Unit test template

```csharp
using {Namespace.Of.ClassUnderTest} ;
using {Other.Required.Usings} ;
using FluentAssertions ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.{Layer} ;

public class {ClassName}Tests
{
  private readonly I{Dep1} _{dep1} = Substitute.For<I{Dep1}>() ;
  private readonly I{Dep2} _{dep2} = Substitute.For<I{Dep2}>() ;

  private {ClassName} CreateSut() => new( _{dep1}, _{dep2} ) ;

  [Fact]
  public async Task {MethodName}_{Condition}_{Expected}()
  {
    // Arrange
    var sut = CreateSut() ;
    // setup mocks

    // Act
    var result = await sut.{MethodName}( ... ) ;

    // Assert
    result.Should().Be( ... ) ;
  }
}
```

### Integration test template (for repository or DbContext tests)
```csharp
using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Tests.Integration.Data ;

public class {ClassName}Tests : IDisposable
{
  private readonly TestDbContext _dbContext = TestDbContextFactory.Create() ;

  public void Dispose() => _dbContext.Dispose() ;

  [Fact]
  public async Task {MethodName}_{Condition}_{Expected}()
  {
    // Arrange
    // ...

    // Act
    // ...

    // Assert
    // ...
  }
}
```

### Key mocking patterns

**Mock `IRepository<T>` with async LINQ support (Query):**
```csharp
using MockQueryable ;

var items = new List<MyEntity> { ... }.BuildMock() ;
_repo.Query().Returns( items ) ;
```

**Mock `HttpClient` (for HTTP services):**
```csharp
var handler = new MockHttpMessageHandler( HttpStatusCode.OK, "{}" ) ;
var httpClient = new HttpClient( handler ) { BaseAddress = new Uri( "https://api.example.com/" ) } ;
var sut = new MyService( Options.Create( _settings ), httpClient, _logger ) ;
```

**Verify a dependency was called:**
```csharp
await _dep.Received( 1 ).MethodAsync( Arg.Is<string>( s => s.Contains( "expected" ) ) ) ;
```

**Verify a dependency was NOT called:**
```csharp
await _dep.DidNotReceive().MethodAsync( Arg.Any<string>() ) ;
```

**Test that a method throws:**
```csharp
var act = () => sut.MethodAsync( input ) ;
await act.Should().ThrowAsync<InvalidOperationException>()
  .WithMessage( "*expected fragment*" ) ;
```

---

## Step 5: Validate

After writing the test file:

1. Call `mcp__ide__getDiagnostics` — fix any errors before reporting done
2. Run `dotnet test Fiolyn.Tests/Fiolyn.Tests.csproj --verbosity normal` from repo root
3. Report:
   - Test file path
   - Number of test cases generated
   - Test run result (pass/fail count)
   - Any tests that failed and why

---

## Constraints

- Never modify production source files
- Never overwrite an existing test file — extend it with missing cases only
- Never use `Moq` — always `NSubstitute`
- Never use `.Result` or `.Wait()` — always `await`
- Never use `Assert.*` — always FluentAssertions (`.Should()`)
- Use `TestEntity` for integration tests — never use production entities like `TelegramUserInfo`
- Controller tests must assert HTTP 200 for ALL outcome types (Success, Skipped, Error)
- HTTP error message assertions: use the enum name (e.g., `"*BadRequest*"`), not the status code number (`"*400*"`)