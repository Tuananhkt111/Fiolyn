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

  private static AppUser BuildUser( long id, string email = "", string? displayName = null ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      DisplayName = displayName
    } ;

  private static SpendAccountMember BuildMember( long accountId, AppUser user ) =>
    new()
    {
      SpendAccountId = accountId,
      AppUserId = user.Id,
      AppUser = user,
      JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc )
    } ;

  private static SpendAccount BuildEntity( long id = 10, long[] memberIds = null!,
    string name = "Wallet", SpendAccountType type = SpendAccountType.Cash, long balance = 1_500_000L )
  {
    memberIds ??= [7] ;
    var account = new SpendAccount
    {
      Id = id,
      Name = name,
      Type = type,
      Balance = balance
    } ;
    foreach ( var memberId in memberIds )
    {
      account.Members.Add( BuildMember( id, BuildUser( memberId ) ) ) ;
    }

    return account ;
  }

  [Fact]
  public async Task GetAllAsync_NoFilter_ReturnsAdminResponseListWithMembers()
  {
    _service.GetAllAsync().Returns( [
      BuildEntity( 1, [7, 8] ),
      BuildEntity( 2, [9] )
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    await _service.Received( 1 ).GetAllAsync() ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<AdminSpendAccountResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
    list[ 0 ].Members.Should().HaveCount( 2 ) ;
    list[ 0 ].Members.Select( m => m.AppUserId ).Should().BeEquivalentTo( [7L, 8L] ) ;
  }

  [Fact]
  public async Task GetAllAsync_WithUserIdFilter_PassesFilter()
  {
    _service.GetAllAsync( 42L ).Returns( [BuildEntity( 1, [42] )] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( 42 ) ;

    await _service.Received( 1 ).GetAllAsync( 42L ) ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<AdminSpendAccountResponse>>().Which ;
    list.Should().ContainSingle()
      .Which.Members.Should().ContainSingle()
      .Which.AppUserId.Should().Be( 42 ) ;
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
    var account = BuildEntity( 10, [7, 8] ) ;
    account.Members.First( m => m.AppUserId == 7 ).AppUser.Email = "owner@example.com" ;
    account.Members.First( m => m.AppUserId == 7 ).AppUser.DisplayName = "Owner" ;
    _service.GetByIdAsync( 10 ).Returns( account ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<AdminSpendAccountResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
    body.Members.Should().HaveCount( 2 ) ;
    body.Members.Select( m => m.Email ).Should().Contain( "owner@example.com" ) ;
  }

  [Fact]
  public async Task CreateAsync_UsesAppUserIdFromRequest_AndReturns201()
  {
    SpendAccount? captured = null ;
    long capturedUserId = 0 ;
    _service.CreateAsync( Arg.Do<SpendAccount>( a => captured = a ), Arg.Do<long>( id => capturedUserId = id ) )
      .Returns( ci =>
      {
        var a = ci.Arg<SpendAccount>() ;
        a.Id = 55 ;
        return a ;
      } ) ;
    var enriched = BuildEntity( 55, [42], "Visa", SpendAccountType.Credit, -500_000L ) ;
    enriched.Members.First().AppUser.Email = "bob@x.com" ;
    enriched.Members.First().AppUser.DisplayName = "Bob" ;
    _service.GetByIdAsync( 55 ).Returns( enriched ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new AdminSpendAccountCreateRequest(
      42, "Visa", SpendAccountType.Credit, -500_000L ) ) ;

    captured.Should().NotBeNull() ;
    capturedUserId.Should().Be( 42 ) ;
    captured!.Name.Should().Be( "Visa" ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<AdminSpendAccountResponse>().Which ;
    body.Members.Should().ContainSingle()
      .Which.Email.Should().Be( "bob@x.com" ) ;
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
    var existing = BuildEntity( 10, [7] ) ;
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
