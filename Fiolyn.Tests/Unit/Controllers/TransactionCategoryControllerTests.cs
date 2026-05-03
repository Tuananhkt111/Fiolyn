using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
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
    _service.GetAllAsync( null ).Returns( [
      new TransactionCategory { Id = 1, Name = "Salary",    Kind = TransactionKind.Income },
      new TransactionCategory { Id = 2, Name = "Groceries", Kind = TransactionKind.Expense }
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeAssignableTo<IEnumerable<TransactionCategoryResponse>>()
      .Which.ToList() ;
    body.Should().HaveCount( 2 ) ;
    body[0].Name.Should().Be( "Salary" ) ;
    body[0].Kind.Should().Be( TransactionKind.Income ) ;
    body[1].Name.Should().Be( "Groceries" ) ;
    body[1].Kind.Should().Be( TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithKindFilter_PassesKindToService()
  {
    _service.GetAllAsync( TransactionKind.Expense ).Returns( [
      new TransactionCategory { Id = 2, Name = "Groceries", Kind = TransactionKind.Expense }
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( TransactionKind.Expense ) ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeAssignableTo<IEnumerable<TransactionCategoryResponse>>()
      .Which.ToList() ;
    body.Should().ContainSingle().Which.Kind.Should().Be( TransactionKind.Expense ) ;
    await _service.Received( 1 ).GetAllAsync( TransactionKind.Expense ) ;
  }

  [Fact]
  public async Task GetAllAsync_EmptyList_ReturnsEmptyArray()
  {
    _service.GetAllAsync( null ).Returns( [] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeAssignableTo<IEnumerable<TransactionCategoryResponse>>()
      .Which.Should().BeEmpty() ;
  }
}
