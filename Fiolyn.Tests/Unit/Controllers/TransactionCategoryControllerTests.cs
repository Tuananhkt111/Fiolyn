using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class TransactionCategoryControllerTests
{
  private readonly ITransactionCategoryService _service =
    Substitute.For<ITransactionCategoryService>() ;

  private TransactionCategoryController CreateSut() => new(_service) ;

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAllCategories()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage(
        new TransactionCategory
        {
          Id = 1,
          Name = "Salary",
          Kind = TransactionKind.Income
        },
        new TransactionCategory
        {
          Id = 2,
          Name = "Groceries",
          Kind = TransactionKind.Expense
        } ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<TransactionCategoryResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.Items[ 0 ].Name.Should().Be( "Salary" ) ;
    page.Items[ 0 ].Kind.Should().Be( TransactionKind.Income ) ;
    page.Items[ 1 ].Name.Should().Be( "Groceries" ) ;
    page.Items[ 1 ].Kind.Should().Be( TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithKindFilter_PassesKindToService()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>(), TransactionKind.Expense )
      .Returns( PagedResultHelpers.SinglePage(
        new TransactionCategory
        {
          Id = 2,
          Name = "Groceries",
          Kind = TransactionKind.Expense
        } ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery(), TransactionKind.Expense ) ;

    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<TransactionCategoryResponse>>().Which ;
    page.Items.Should().ContainSingle().Which.Kind.Should().Be( TransactionKind.Expense ) ;
    await _service.Received( 1 ).GetAllAsync( Arg.Any<PaginationQuery>(), TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetAllAsync_EmptyList_ReturnsEmptyArray()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.EmptyPage<TransactionCategory>() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<TransactionCategoryResponse>>()
      .Which.Items.Should().BeEmpty() ;
  }
}
