using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AdminSpendAccountControllerTests
{
  private readonly ISpendAccountService _service = Substitute.For<ISpendAccountService>() ;

  private AdminSpendAccountController CreateSut() => new(_service) ;

  private static SpendAccount BuildEntity( long id = 10, long userId = 7,
    string email = "owner@example.com", string? displayName = "Owner",
    string name = "Wallet", SpendAccountType type = SpendAccountType.Cash,
    long balance = 1_500_000L ) =>
    new()
    {
      Id = id,
      AppUserId = userId,
      AppUser = new AppUser
      {
        Id = userId,
        Email = email,
        GoogleSub = "g-" + userId,
        DisplayName = displayName
      },
      Name = name,
      Type = type,
      Balance = balance
    } ;

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAdminResponseList()
  {
    _service.GetAllAsync().Returns( [BuildEntity( 1 ), BuildEntity( 2, 8 )] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    await _service.Received( 1 ).GetAllAsync() ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<AdminSpendAccountResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
    list[ 0 ].AppUserEmail.Should().Be( "owner@example.com" ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdFilter_PassesFilterAndReturnsAdminResponse()
  {
    _service.GetAllAsync( 42L ).Returns( [BuildEntity( 1, 42 )] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 42 ) ;

    await _service.Received( 1 ).GetAllAsync( 42L ) ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<AdminSpendAccountResponse>>().Which ;
    list.Should().HaveCount( 1 ) ;
    list[ 0 ].AppUserId.Should().Be( 42 ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( SpendAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Existing_Returns200WithAdminResponse()
  {
    _service.GetByIdAsync( 10 ).Returns( BuildEntity() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<AdminSpendAccountResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
    body.AppUserEmail.Should().Be( "owner@example.com" ) ;
    body.AppUserDisplayName.Should().Be( "Owner" ) ;
  }

  [Fact]
  public async Task CreateAsync_UsesAppUserIdFromRequest_AndReturns201()
  {
    SpendAccount? captured = null ;
    _service.CreateAsync( Arg.Do<SpendAccount>( a => captured = a ) )
      .Returns( ci =>
      {
        var a = ci.Arg<SpendAccount>() ;
        a.Id = 55 ;
        return a ;
      } ) ;
    _service.GetByIdAsync( 55 ).Returns( BuildEntity( 55, 42, "bob@x.com", "Bob",
      "Visa", SpendAccountType.Credit, -500_000L ) ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new AdminSpendAccountCreateRequest(
      42, "Visa", SpendAccountType.Credit, -500_000L ) ) ;

    captured.Should().NotBeNull() ;
    captured!.AppUserId.Should().Be( 42 ) ;
    captured.Name.Should().Be( "Visa" ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<AdminSpendAccountResponse>().Which ;
    body.AppUserId.Should().Be( 42 ) ;
    body.AppUserEmail.Should().Be( "bob@x.com" ) ;
    await _service.Received( 1 ).GetByIdAsync( 55 ) ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( SpendAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, new SpendAccountRequest( "x", SpendAccountType.Cash, 0L ) ) ;

    result.Should().BeOfType<NotFoundResult>() ;
    await _service.DidNotReceive().UpdateAsync( Arg.Any<SpendAccount>() ) ;
  }

  [Fact]
  public async Task UpdateAsync_Existing_AppliesChangesAndReturns200()
  {
    var existing = BuildEntity( balance: 1_000_000L ) ;
    _service.GetByIdAsync( 10 ).Returns( existing ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10,
      new SpendAccountRequest( "Renamed", SpendAccountType.Credit, 7_500_000L ) ) ;

    existing.Name.Should().Be( "Renamed" ) ;
    existing.Type.Should().Be( SpendAccountType.Credit ) ;
    existing.Balance.Should().Be( 7_500_000L ) ;
    await _service.Received( 1 ).UpdateAsync( existing ) ;
    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<AdminSpendAccountResponse>() ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204_AndCallsUnscopedSoftDelete()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10 ) ;
    await _service.DidNotReceive().SoftDeleteAsync( Arg.Any<long>(), Arg.Any<long>() ) ;
  }
}
