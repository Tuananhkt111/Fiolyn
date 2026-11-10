using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Infrastructure.Exceptions ;
using Fiolyn.Services.Internal ;
using FluentAssertions ;
using MockQueryable ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class TransactionCategoryServiceTests
{
  private readonly IRepository<TransactionCategory> _repo =
    Substitute.For<IRepository<TransactionCategory>>() ;

  private TransactionCategoryService CreateSut() => new(_repo) ;

  [Fact]
  public async Task CreateAsync_ThrowsConflict_WhenSameKindNameExists()
  {
    _repo.Query().Returns( new List<TransactionCategory>
    {
      new()
      {
        Id = 1,
        Name = "Groceries",
        Kind = TransactionKind.Expense
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var act = async () => await sut.CreateAsync( "Groceries", TransactionKind.Expense ) ;

    await act.Should().ThrowAsync<ConflictException>() ;
  }

  [Fact]
  public async Task CreateAsync_AllowsSameNameDifferentKind()
  {
    _repo.Query().Returns( new List<TransactionCategory>
    {
      new()
      {
        Id = 1,
        Name = "Other",
        Kind = TransactionKind.Expense
      }
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "Other", TransactionKind.Income ) ;

    entry.Name.Should().Be( "Other" ) ;
    entry.Kind.Should().Be( TransactionKind.Income ) ;
    await _repo.Received( 1 ).AddAsync( Arg.Any<TransactionCategory>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_RenamesButKeepsKind()
  {
    var existing = new TransactionCategory
    {
      Id = 5,
      Name = "Old",
      Kind = TransactionKind.Expense
    } ;
    _repo.GetByIdOrThrowAsync( 5L ).Returns( existing ) ;
    var sut = CreateSut() ;

    var updated = await sut.UpdateAsync( 5, "New" ) ;

    updated.Name.Should().Be( "New" ) ;
    updated.Kind.Should().Be( TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetAllAsync_FiltersByKind_WhenSpecified()
  {
    var rows = new List<TransactionCategory>
    {
      new()
      {
        Id = 1,
        Name = "Salary",
        Kind = TransactionKind.Income
      },
      new()
      {
        Id = 2,
        Name = "Groceries",
        Kind = TransactionKind.Expense
      }
    } ;
    _repo.Query().Returns( rows.BuildMock() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery(), TransactionKind.Expense ) ;

    result.Items.Should().ContainSingle().Which.Name.Should().Be( "Groceries" ) ;
  }

  [Fact]
  public async Task SoftDeleteAsync_ProcessesAndSaves()
  {
    var entry = new TransactionCategory
    {
      Id = 3,
      Name = "Tmp",
      Kind = TransactionKind.Expense
    } ;
    _repo.GetByIdOrThrowAsync( 3L ).Returns( entry ) ;
    var sut = CreateSut() ;

    await sut.SoftDeleteAsync( 3 ) ;

    _repo.Received( 1 ).SoftDelete( entry ) ;
    await _repo.Received( 1 ).SaveAsync() ;
  }

  // The (Kind, Name) unique index also covers soft-deleted categories, so re-creating a deleted name
  // must restore the old row instead of inserting one the index refuses.
  [Fact]
  public async Task CreateAsync_NameOfADeletedCategory_RestoresItInsteadOfInserting()
  {
    var deleted = new TransactionCategory
    {
      Id = 3,
      Name = "Groceries",
      Kind = TransactionKind.Expense,
      IsDeleted = true
    } ;
    _repo.Query().Returns( new List<TransactionCategory>
    {
      deleted
    }.BuildMock() ) ;
    var sut = CreateSut() ;

    var entry = await sut.CreateAsync( "Groceries", TransactionKind.Expense ) ;

    entry.Should().BeSameAs( deleted ) ;
    _repo.Received( 1 ).Restore( deleted ) ;
    await _repo.DidNotReceive().AddAsync( Arg.Any<TransactionCategory>() ) ;
  }
}
