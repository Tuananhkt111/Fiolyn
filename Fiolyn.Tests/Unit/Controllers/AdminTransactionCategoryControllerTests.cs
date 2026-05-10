using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.TransactionCategory ;
using Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AdminTransactionCategoryControllerTests
{
  private readonly ITransactionCategoryService _service =
    Substitute.For<ITransactionCategoryService>() ;

  private AdminTransactionCategoryController CreateSut() => new(_service) ;

  [Fact]
  public async Task CreateAsync_Returns201_WithBody()
  {
    _service.CreateAsync( "Groceries", TransactionKind.Expense ).Returns( new TransactionCategory
    {
      Id = 1,
      Name = "Groceries",
      Kind = TransactionKind.Expense
    } ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync(
      new TransactionCategoryCreateRequest( "Groceries", TransactionKind.Expense ) ) ;

    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<TransactionCategoryResponse>().Which ;
    body.Name.Should().Be( "Groceries" ) ;
    body.Kind.Should().Be( TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Returns404_WhenMissing()
  {
    _service.GetByIdAsync( 9L ).Returns( ( TransactionCategory? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 9 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetAllAsync_PassesKindFilter()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>(), TransactionKind.Income )
      .Returns( PagedResultHelpers.SinglePage(
        new TransactionCategory
        {
          Id = 1,
          Name = "Salary",
          Kind = TransactionKind.Income
        } ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery(), TransactionKind.Income ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<TransactionCategoryResponse>>()
      .Which.Items.Should().ContainSingle().Which.Name.Should().Be( "Salary" ) ;
  }
}
