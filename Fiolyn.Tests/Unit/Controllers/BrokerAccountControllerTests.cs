using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.BrokerAccount ;
using Fiolyn.Controllers.DTOs.Responses.BrokerAccount ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class BrokerAccountControllerTests
{
  private readonly IBrokerAccountService _service = Substitute.For<IBrokerAccountService>() ;

  private BrokerAccountController CreateSut( long callerId = 7 )
  {
    var controller = new BrokerAccountController( _service ) ;
    var identity = new ClaimsIdentity( [
      new Claim( JwtRegisteredClaimNames.Sub, callerId.ToString() )
    ], "TestAuth" ) ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = new DefaultHttpContext
      {
        User = new ClaimsPrincipal( identity )
      }
    } ;
    return controller ;
  }

  private static BrokerAccount BuildEntity( long id = 10, string name = "VPS", long spendAccountId = 1 ) =>
    new()
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

  private static AppUser BuildUser( long id, string email = "", string? displayName = null ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      DisplayName = displayName
    } ;

  private static BrokerAccountMember BuildMember( long accountId, long userId, string email = "",
    string? displayName = null ) =>
    new()
    {
      BrokerAccountId = accountId,
      AppUserId = userId,
      AppUser = BuildUser( userId, email, displayName ),
      JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc )
    } ;

  [Fact]
  public async Task CreateAsync_PassesCallerIdToService_AndReturns201()
  {
    BrokerAccount? captured = null ;
    long capturedCallerId = 0 ;
    _service.CreateAsync( Arg.Do<BrokerAccount>( a => captured = a ),
        Arg.Do<long>( id => capturedCallerId = id ) )
      .Returns( ci =>
      {
        var a = ci.Arg<BrokerAccount>() ;
        a.Id = 55 ;
        return a ;
      } ) ;
    _service.GetByIdForUserAsync( 55, 42 ).Returns( BuildEntity( 55, "VPS", 7 ) ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.CreateAsync( new BrokerAccountRequest( "VPS", 7 ) ) ;

    captured.Should().NotBeNull() ;
    capturedCallerId.Should().Be( 42 ) ;
    captured!.Name.Should().Be( "VPS" ) ;
    captured.SpendAccountId.Should().Be( 7 ) ;
    var body = result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<BrokerAccountResponse>().Which ;
    body.Name.Should().Be( "VPS" ) ;
    body.SpendAccountId.Should().Be( 7 ) ;
  }

  [Fact]
  public async Task GetAllAsync_PassesUserIdToService()
  {
    _service.GetAllForUserAsync( 42, Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage( BuildEntity( 1 ), BuildEntity( 2 ) ) ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    await _service.Received( 1 ).GetAllForUserAsync( 42, Arg.Any<PaginationQuery>() ) ;
    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<BrokerAccountResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.TotalCount.Should().Be( 2L ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 99 ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task GetByIdAsync_Existing_Returns200()
  {
    _service.GetByIdForUserAsync( 10, 7 ).Returns( BuildEntity() ) ;
    var sut = CreateSut() ;

    var result = await sut.GetByIdAsync( 10 ) ;

    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<BrokerAccountResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
  }

  [Fact]
  public async Task ExistsAsync_True_Returns200()
  {
    _service.ExistsForUserAsync( 10, 7 ).Returns( true ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 10 ) ).Should().BeOfType<OkResult>() ;
  }

  [Fact]
  public async Task ExistsAsync_False_Returns404()
  {
    _service.ExistsForUserAsync( 10, 7 ).Returns( false ) ;
    var sut = CreateSut() ;

    ( await sut.ExistsAsync( 10 ) ).Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.UpdateAsync( 99, 7, Arg.Any<string>(), Arg.Any<long>() )
      .Returns( ( BrokerAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, new BrokerAccountRequest( "X", 1 ) ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_PassesScalarsToService_AndReturns200()
  {
    var updated = BuildEntity( 10, "Renamed", 7 ) ;
    _service.UpdateAsync( 10, 7, "Renamed", 7 ).Returns( updated ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10, new BrokerAccountRequest( "Renamed", 7 ) ) ;

    await _service.Received( 1 ).UpdateAsync( 10, 7, "Renamed", 7 ) ;
    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<BrokerAccountResponse>().Which ;
    body.Name.Should().Be( "Renamed" ) ;
    body.SpendAccountId.Should().Be( 7 ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut( 42 ) ;

    var result = await sut.DeleteAsync( 10 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 10, 42 ) ;
  }

  [Fact]
  public async Task GetMembersAsync_Returns200WithMemberList()
  {
    _service.GetMembersAsync( 10, 7, Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage(
        BuildMember( 10, 7, "me@example.com", "Me" ),
        BuildMember( 10, 8, "other@example.com", "Other" ) ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetMembersAsync( 10, new PaginationQuery() ) ;

    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<BrokerAccountMemberResponse>>().Which ;
    page.Items.Select( m => m.Email ).Should().ContainInOrder( "me@example.com", "other@example.com" ) ;
  }

  [Fact]
  public async Task AddMemberAsync_PassesEmailToService_AndReturnsMemberList()
  {
    _service.AddMemberAsync( 10, 7, "target@example.com" ).Returns( [
      BuildMember( 10, 7 ),
      BuildMember( 10, 8, "target@example.com" )
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.AddMemberAsync( 10, new AddMemberRequest( "target@example.com" ) ) ;

    await _service.Received( 1 ).AddMemberAsync( 10, 7, "target@example.com" ) ;
    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<BrokerAccountMemberResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
  }

  [Fact]
  public async Task RemoveMemberAsync_CallsServiceAndReturns204()
  {
    var sut = CreateSut( 42 ) ;

    var result = await sut.RemoveMemberAsync( 10, 8 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).RemoveMemberAsync( 10, 42, 8 ) ;
  }
}
