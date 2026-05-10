using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.Counterparty ;
using Fiolyn.Controllers.DTOs.Responses.Counterparty ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class CounterpartyControllerTests
{
  private readonly ICounterpartyService _service = Substitute.For<ICounterpartyService>() ;

  private CounterpartyController CreateSut( long callerId = 7 )
  {
    var controller = new CounterpartyController( _service ) ;
    var identity = new ClaimsIdentity(
      [new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )], "TestAuth" ) ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal( identity )
      }
    } ;
    return controller ;
  }

  [Fact]
  public async Task GetAllAsync_Returns200WithMappedList()
  {
    _service.GetAllForUserAsync( 7L, Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage(
        new Counterparty
        {
          Id = 1,
          AppUserId = 7,
          Name = "Alice"
        },
        new Counterparty
        {
          Id = 2,
          AppUserId = 7,
          Name = "Bob"
        } ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<CounterpartyResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.Items.Select( c => c.Name ).Should().ContainInOrder( "Alice", "Bob" ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Returns404_WhenMissing()
  {
    _service.GetByIdForUserAsync( 99L, 7L ).Returns( ( Counterparty? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task CreateAsync_Returns201WithMappedBody()
  {
    var entry = new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "Bob"
    } ;
    _service.CreateAsync( "Bob", 7L ).Returns( entry ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new CounterpartyRequest( "Bob" ) ) ;

    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<CounterpartyResponse>()
      .Which.Name.Should().Be( "Bob" ) ;
  }

  [Fact]
  public async Task UpdateAsync_Returns404_WhenMissing()
  {
    _service.UpdateAsync( 5L, 7L, "Renamed" ).Returns( ( Counterparty? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, new CounterpartyRequest( "Renamed" ) ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Returns200_OnSuccess()
  {
    _service.UpdateAsync( 5L, 7L, "Renamed" ).Returns( new Counterparty
    {
      Id = 5,
      AppUserId = 7,
      Name = "Renamed"
    } ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 5, new CounterpartyRequest( "Renamed" ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<CounterpartyResponse>()
      .Which.Name.Should().Be( "Renamed" ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 5 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 5L, 7L ) ;
  }
}
