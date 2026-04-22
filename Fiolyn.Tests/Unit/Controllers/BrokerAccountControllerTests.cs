using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class BrokerAccountControllerTests
{
  private readonly IBrokerAccountService _service = Substitute.For<IBrokerAccountService>() ;

  private BrokerAccountController CreateSut() => new(_service) ;

  [Fact]
  public async Task CreateAsync_Returns201WithPersistedEntity()
  {
    var created = new BrokerAccount
    {
      Id = 1,
      Name = "VPS"
    } ;
    _service.CreateAsync( Arg.Is<BrokerAccount>( b => b.Name == "VPS" ) ).Returns( created ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new BrokerAccountRequest( "VPS" ) ) ;

    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<BrokerAccountResponse>()
      .Which.Id.Should().Be( 1 ) ;
  }

  [Fact]
  public async Task GetAllAsync_Returns200WithMappedList()
  {
    _service.GetAllAsync().Returns( [
      new BrokerAccount
      {
        Id = 1,
        Name = "VPS"
      },
      new BrokerAccount
      {
        Id = 2,
        Name = "TCBS"
      }
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<BrokerAccountResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Existing_Returns200()
  {
    _service.GetByIdAsync( 1 ).Returns( new BrokerAccount
    {
      Id = 1,
      Name = "VPS"
    } ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 1 ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<BrokerAccountResponse>()
      .Which.Name.Should().Be( "VPS" ) ;
  }

  [Fact]
  public async Task ExistsAsync_True_Returns200()
  {
    _service.ExistsAsync( 1 ).Returns( true ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 1 ) ).Should().BeOfType<OkResult>() ;
  }

  [Fact]
  public async Task ExistsAsync_False_Returns404()
  {
    _service.ExistsAsync( 1 ).Returns( false ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 1 ) ).Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, new BrokerAccountRequest( "X" ) ) ;

    result.Should().BeOfType<NotFoundResult>() ;
    await _service.DidNotReceive().UpdateAsync( Arg.Any<BrokerAccount>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_Existing_AppliesChangesAndReturns200()
  {
    var existing = new BrokerAccount
    {
      Id = 1,
      Name = "OLD"
    } ;
    _service.GetByIdAsync( 1 ).Returns( existing ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 1, new BrokerAccountRequest( "NEW" ) ) ;

    existing.Name.Should().Be( "NEW" ) ;
    result.Should().BeOfType<OkObjectResult>() ;
    await _service.Received( 1 ).UpdateAsync( existing ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 1 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 1 ) ;
  }
}
