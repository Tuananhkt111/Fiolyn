using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using Microsoft.EntityFrameworkCore ;
using MockQueryable ;
using Npgsql ;
using NSubstitute ;
using NSubstitute.ExceptionExtensions ;

namespace Fiolyn.Tests.Unit.Services ;

public class CounterpartyServiceTests
{
  private readonly IRepository<BorrowingTransaction> _borrowingRepo =
    Substitute.For<IRepository<BorrowingTransaction>>() ;

  private readonly IRepository<LendTransaction> _lendRepo =
    Substitute.For<IRepository<LendTransaction>>() ;

  private readonly IRepository<Counterparty> _repository = Substitute.For<IRepository<Counterparty>>() ;

  public CounterpartyServiceTests()
  {
    _lendRepo.Query().Returns( new List<LendTransaction>().BuildMock() ) ;
    _borrowingRepo.Query().Returns( new List<BorrowingTransaction>().BuildMock() ) ;
  }

  private CounterpartyService CreateSut() => new(_repository, _lendRepo, _borrowingRepo) ;

  private void SetupExisting( params Counterparty[] rows ) => _repository.Query().Returns( rows.ToList().BuildMock() ) ;

  [Fact]
  public async Task GetAllForUserAsync_FiltersByOwner_AndOrdersByName()
  {
    SetupExisting(
      new Counterparty
      {
        Id = 1,
        AppUserId = 7,
        Name = "Bob"
      },
      new Counterparty
      {
        Id = 2,
        AppUserId = 7,
        Name = "Alice"
      },
      new Counterparty
      {
        Id = 3,
        AppUserId = 9,
        Name = "Charlie"
      } ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllForUserAsync( 7, new PaginationQuery() ) ;

    result.Items.Should().HaveCount( 2 ) ;
    result.TotalCount.Should().Be( 2L ) ;
    result.Items.Select( c => c.Name ).Should().ContainInOrder( "Alice", "Bob" ) ;
  }

  [Fact]
  public async Task GetByIdForUserAsync_ReturnsNull_WhenNotOwned()
  {
    SetupExisting( new Counterparty
    {
      Id = 5,
      AppUserId = 9,
      Name = "Charlie"
    } ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdForUserAsync( 5, 7 ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task CreateAsync_TrimmedAndPersisted()
  {
    SetupExisting() ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "  Bob  ", 7 ) ;

    entry.Name.Should().Be( "Bob" ) ;
    entry.AppUserId.Should().Be( 7 ) ;
    await _repository.Received( 1 ).AddAsync( Arg.Any<Counterparty>() ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task CreateAsync_DuplicateCaseInsensitive_Throws()
  {
    SetupExisting( new Counterparty
    {
      Id = 1,
      AppUserId = 7,
      Name = "Bob"
    } ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync( "BOB", 7 ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task CreateAsync_AllowsSameName_ForDifferentOwner()
  {
    SetupExisting( new Counterparty
    {
      Id = 1,
      AppUserId = 9,
      Name = "Bob"
    } ) ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "Bob", 7 ) ;

    entry.AppUserId.Should().Be( 7 ) ;
  }

  [Fact]
  public async Task CreateAsync_EmptyName_Throws()
  {
    SetupExisting() ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync( "   ", 7 ) )
      .Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task CreateAsync_RaceLost_UniqueViolationMappedToConflict()
  {
    SetupExisting() ;
    var pgException = new PostgresException(
      "duplicate key value violates unique constraint",
      "ERROR",
      "ERROR",
      PostgresErrorCodes.UniqueViolation ) ;
    _repository.SaveAsync()
      .ThrowsAsync( new DbUpdateException( "unique violation", pgException ) ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.CreateAsync( "Bob", 7 ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task UpdateAsync_ReturnsNull_WhenNotOwned()
  {
    SetupExisting( new Counterparty
    {
      Id = 5,
      AppUserId = 9,
      Name = "Charlie"
    } ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, 7, "Renamed" ) ;

    result.Should().BeNull() ;
  }

  [Fact]
  public async Task UpdateAsync_RenameCollision_Throws()
  {
    SetupExisting(
      new Counterparty
      {
        Id = 5,
        AppUserId = 7,
        Name = "Bob"
      },
      new Counterparty
      {
        Id = 6,
        AppUserId = 7,
        Name = "Alice"
      } ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.UpdateAsync( 5, 7, "alice" ) )
      .Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task UpdateAsync_RenameToSelfCaseChange_Allowed()
  {
    var entry = new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "bob"
    } ;
    SetupExisting( entry ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, 7, "Bob" ) ;

    result!.Name.Should().Be( "Bob" ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Blocked_WhenReferencedByLend()
  {
    var entry = new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "Bob"
    } ;
    SetupExisting( entry ) ;
    _lendRepo.Query().Returns( new List<LendTransaction>
    {
      new()
      {
        Id = 1,
        CounterpartyId = 5
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.SoftDeleteAsync( 5, 7 ) )
      .Should().ThrowAsync<BadRequestException>() ;
    _repository.DidNotReceive().SoftDelete( Arg.Any<Counterparty>() ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Blocked_WhenReferencedByBorrowing()
  {
    var entry = new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "Bob"
    } ;
    SetupExisting( entry ) ;
    _borrowingRepo.Query().Returns( new List<BorrowingTransaction>
    {
      new()
      {
        Id = 1,
        CounterpartyId = 5
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.SoftDeleteAsync( 5, 7 ) )
      .Should().ThrowAsync<BadRequestException>() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_Unreferenced_SoftDeletes()
  {
    var entry = new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "Bob"
    } ;
    SetupExisting( entry ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 5, 7 ) ;

    _repository.Received( 1 ).SoftDelete( entry ) ;
    await _repository.Received( 1 ).SaveAsync() ;
  }

  [Fact]
  public async Task SoftDeleteAsync_NotOwned_Throws()
  {
    SetupExisting( new Counterparty
    {
      Id = 5,
      AppUserId = 9,
      Name = "Charlie"
    } ) ;
    var sut = CreateSut() ;

    await sut.Invoking( s => s.SoftDeleteAsync( 5, 7 ) )
      .Should().ThrowAsync<KeyNotFoundException>() ;
  }

  // docs/reference/counterparties.md: uniqueness is per user and case-insensitive, enforced by an
  // index that also covers soft-deleted rows. Re-creating a deleted contact restores it.
  [Fact]
  public async Task CreateAsync_NameOfADeletedContact_RestoresItCaseInsensitively()
  {
    var deleted = new Counterparty
    {
      Id = 4,
      AppUserId = 7,
      Name = "bob",
      IsDeleted = true
    } ;
    SetupExisting( deleted ) ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "  Bob ", 7 ) ;

    entry.Should().BeSameAs( deleted ) ;
    entry.Name.Should().Be( "Bob" ) ;
    _repository.Received( 1 ).Restore( deleted ) ;
    await _repository.DidNotReceive().AddAsync( Arg.Any<Counterparty>() ) ;
  }
}
