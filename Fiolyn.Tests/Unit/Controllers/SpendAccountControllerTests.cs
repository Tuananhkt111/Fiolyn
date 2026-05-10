using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.SpendAccount ;
using Fiolyn.Controllers.DTOs.Responses.SpendAccount ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class SpendAccountControllerTests
{
  private readonly ISpendAccountService _service = Substitute.For<ISpendAccountService>() ;

  private SpendAccountController CreateSut( long callerId = 7 )
  {
    var controller = new SpendAccountController( _service ) ;
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

  private static SpendAccountRequest BuildRequest( string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash, long balance = 1_500_000L ) =>
    new(name, type, balance) ;

  private static SpendAccountUpdateRequest BuildUpdateRequest( string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash ) =>
    new(name, type) ;

  private static SpendAccount BuildEntity( long id = 10, string name = "Wallet",
    SpendAccountType type = SpendAccountType.Cash, long balance = 1_500_000L ) =>
    new()
    {
      Id = id,
      Name = name,
      Type = type,
      Balance = balance
    } ;

  private static AppUser BuildUser( long id, string email = "", string? displayName = null ) =>
    new()
    {
      Id = id,
      Email = string.IsNullOrEmpty( email ) ? $"user{id}@example.com" : email,
      GoogleSub = $"g-{id}",
      DisplayName = displayName
    } ;

  private static SpendAccountMember BuildMember( long accountId, long userId, string email = "",
    string? displayName = null ) =>
    new()
    {
      SpendAccountId = accountId,
      AppUserId = userId,
      AppUser = BuildUser( userId, email, displayName ),
      JoinedAt = new DateTime( 2024, 1, 1, 0, 0, 0, DateTimeKind.Utc )
    } ;

  [Fact]
  public async Task CreateAsync_PassesCallerIdToService_AndReturns201()
  {
    SpendAccount? captured = null ;
    long capturedCallerId = 0 ;
    _service.CreateAsync( Arg.Do<SpendAccount>( a => captured = a ), Arg.Do<long>( id => capturedCallerId = id ) )
      .Returns( ci => ci.Arg<SpendAccount>() ) ;
    var sut = CreateSut( 42 ) ;

    var result = await sut.CreateAsync( BuildRequest( "Wallet", SpendAccountType.Cash, 2_000_000L ) ) ;

    captured.Should().NotBeNull() ;
    capturedCallerId.Should().Be( 42 ) ;
    captured!.Name.Should().Be( "Wallet" ) ;
    captured.Balance.Should().Be( 2_000_000L ) ;
    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<SpendAccountResponse>() ;
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
      .Which.Value.Should().BeOfType<PagedResult<SpendAccountResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.TotalCount.Should().Be( 2L ) ;
  }

  [Fact]
  public async Task GetByIdAsync_Missing_Returns404()
  {
    _service.GetByIdForUserAsync( 99, 7 ).Returns( ( SpendAccount? )null ) ;
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
      .Which.Value.Should().BeOfType<SpendAccountResponse>().Which ;
    body.Id.Should().Be( 10 ) ;
  }

  [Fact]
  public async Task UpdateAsync_Missing_Returns404()
  {
    _service.UpdateAsync( 99, 7, Arg.Any<string>(), Arg.Any<SpendAccountType>() )
      .Returns( ( SpendAccount? )null ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 99, BuildUpdateRequest() ) ;

    result.Should().BeOfType<NotFoundResult>() ;
  }

  [Fact]
  public async Task UpdateAsync_Existing_PassesScalarsToService_AndReturns200()
  {
    var updated = BuildEntity( name: "Renamed", type: SpendAccountType.Credit, balance: 1_000_000L ) ;
    _service.UpdateAsync( 10, 7, "Renamed", SpendAccountType.Credit ).Returns( updated ) ;
    var sut = CreateSut() ;

    var result = await sut.UpdateAsync( 10, BuildUpdateRequest( "Renamed", SpendAccountType.Credit ) ) ;

    await _service.Received( 1 ).UpdateAsync( 10, 7, "Renamed", SpendAccountType.Credit ) ;
    var body = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<SpendAccountResponse>().Which ;
    body.Name.Should().Be( "Renamed" ) ;
    body.Type.Should().Be( SpendAccountType.Credit ) ;
    body.Balance.Should().Be( 1_000_000L ) ;
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
      .Which.Value.Should().BeOfType<PagedResult<SpendAccountMemberResponse>>().Which ;
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
      .Which.Value.Should().BeOfType<List<SpendAccountMemberResponse>>().Which ;
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
