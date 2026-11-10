using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AdminBrokerAccountControllerTests
{
  private readonly IBrokerAccountService _service = Substitute.For<IBrokerAccountService>() ;

  private AdminBrokerAccountController CreateSut() => new(_service) ;

  private static AppUser BuildUser( long id, string email = "", string? displayName = null ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      DisplayName = displayName
    } ;

  private static BrokerAccountMember BuildMember( long accountId, AppUser user ) =>
    new()
    {
      BrokerAccountId = accountId,
      AppUserId = user.Id,
      AppUser = user,
      JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc )
    } ;

  private static BrokerAccount BuildEntity( long id = 10, long[] memberIds = null!,
    string name = "VPS", long spendAccountId = 1 )
  {
    memberIds ??= [7] ;
    var account = new BrokerAccount
    {
      Id = id,
      Name = name,
      SpendAccountId = spendAccountId,
      SpendAccount = new SpendAccount
      {
        Id = spendAccountId,
        Name = $"Spend{spendAccountId}"
      }
    } ;
    foreach ( var memberId in memberIds )
    {
      account.Members.Add( BuildMember( id, BuildUser( memberId ) ) ) ;
    }

    return account ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task GetAllAsync_NoFilter_ReturnsAdminResponseListWithMembers()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage(
        BuildEntity( 1, [7, 8] ),
        BuildEntity( 2, [9] ) ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    await _service.Received( 1 ).GetAllAsync( Arg.Any<PaginationQuery>() ) ;
    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<AdminBrokerAccountResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.Items[ 0 ].Members.Should().HaveCount( 2 ) ;
    page.Items[ 0 ].Members.Select( m => m.AppUserId ).Should().BeEquivalentTo( [7L, 8L] ) ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task GetAllAsync_WithUserIdFilter_PassesFilter()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>(), 42L )
      .Returns( PagedResultHelpers.SinglePage( BuildEntity( 1, [42] ) ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery(), 42 ) ;

    await _service.Received( 1 ).GetAllAsync( Arg.Any<PaginationQuery>(), 42L ) ;
    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<AdminBrokerAccountResponse>>().Which ;
    page.Items.Should().ContainSingle()
      .Which.Members.Should().ContainSingle()
      .Which.AppUserId.Should().Be( 42 ) ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdAsync( 99 ).Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task GetByIdAsync_Existing_Returns200WithAdminResponse()
  {
    var account = BuildEntity( 10, [7, 8] ) ;
    account.Members.First( m => m.AppUserId == 7 ).AppUser.Email = "owner@example.com" ;
    account.Members.First( m => m.AppUserId == 7 ).AppUser.DisplayName = "Owner" ;
    _service.GetByIdAsync( 10 ).Returns( account ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<AdminBrokerAccountResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
    body.Members.Should().HaveCount( 2 ) ;
    body.Members.Select( m => m.Email ).Should().Contain( "owner@example.com" ) ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task CreateAsync_UsesAppUserIdFromRequest_AndReturns201()
  {
    BrokerAccount? captured = null ;
    long capturedUserId = 0 ;
    _service.CreateAsync( Arg.Do<BrokerAccount>( a => captured = a ),
        Arg.Do<long>( id => capturedUserId = id ) )
      .Returns( ci =>
      {
        var a = ci.Arg<BrokerAccount>() ;
        a.Id = 55 ;
        return a ;
      } ) ;
    var enriched = BuildEntity( 55, [42], "TCBS" ) ;
    enriched.Members.First().AppUser.Email = "bob@x.com" ;
    enriched.Members.First().AppUser.DisplayName = "Bob" ;
    _service.GetByIdAsync( 55 ).Returns( enriched ) ;
    var sut = CreateSut() ;

    var result = await sut.CreateAsync( new AdminBrokerAccountCreateRequest( 42, "TCBS", 9 ) ) ;

    captured.Should().NotBeNull() ;
    capturedUserId.Should().Be( 42 ) ;
    captured!.Name.Should().Be( "TCBS" ) ;
    captured.SpendAccountId.Should().Be( 9 ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<AdminBrokerAccountResponse>().Which ;
    body.Members.Should().ContainSingle()
      .Which.Email.Should().Be( "bob@x.com" ) ;
    await _service.Received( 1 ).GetByIdAsync( 55 ) ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.UpdateAsync( 99, Arg.Any<string>(), Arg.Any<long>() )
      .Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, new BrokerAccountRequest( "x", 1 ) ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task UpdateAsync_PassesScalarsToService_AndReturns200()
  {
    var updated = BuildEntity( 10, [7], "Renamed", 9 ) ;
    _service.UpdateAsync( 10, "Renamed", 9 ).Returns( updated ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10, new BrokerAccountRequest( "Renamed", 9 ) ) ;

    await _service.Received( 1 ).UpdateAsync( 10, "Renamed", 9 ) ;
    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<AdminBrokerAccountResponse>().Which ;
    body.Name.Should().Be( "Renamed" ) ;
    body.SpendAccountId.Should().Be( 9 ) ;
  }

  [Fact]
  [Trait( "Story", "US-14" )]
  public async Task DeleteAsync_Returns204_AndCallsUnscopedSoftDelete()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10 ) ;
    await _service.DidNotReceive().SoftDeleteAsync( Arg.Any<long>(), Arg.Any<long>() ) ;
  }
}
