using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.User ;
using Fiolyn.Controllers.DTOs.Responses.User ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class UserControllerTests
{
  private readonly IUserService _userService = Substitute.For<IUserService>() ;

  private UserController CreateSut( long? callerId = null )
  {
    var controller = new UserController( _userService ) ;
    if ( callerId is not null )
    {
      var identity = new ClaimsIdentity( [
        new Claim( JwtRegisteredClaimNames.Sub, callerId.Value.ToString() )
      ], "TestAuth" ) ;
      controller.ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          User = new ClaimsPrincipal( identity )
        }
      } ;
    }

    return controller ;
  }

  [Fact]
  public async Task GetMeAsync_Returns200WithCallerProfile()
  {
    var user = new AppUser
    {
      Id = 7,
      Email = "me@example.com",
      GoogleSub = "sub",
      DisplayName = "Me"
    } ;
    _userService.GetByIdAsync( 7 ).Returns( user ) ;
    var sut = CreateSut( 7 ) ;

    var result = await sut.GetMeAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.Id.Should().Be( 7 ) ;
  }

  [Fact]
  public async Task UpdateMeAsync_DelegatesCallerIdAndReturns200()
  {
    var user = new AppUser
    {
      Id = 7,
      Email = "me@example.com",
      GoogleSub = "sub",
      DisplayName = "New"
    } ;
    _userService.UpdateDisplayNameAsync( 7, "New" ).Returns( user ) ;
    var sut = CreateSut( 7 ) ;

    var result = await sut.UpdateMeAsync( new UpdateMeRequest( "New" ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.DisplayName.Should().Be( "New" ) ;
    await _userService.Received( 1 ).UpdateDisplayNameAsync( 7, "New" ) ;
  }

  [Fact]
  public async Task GetAllAsync_Returns200WithMappedList()
  {
    _userService.GetAllAsync().Returns( [
      new AppUser
      {
        Id = 1,
        Email = "a@x.com",
        GoogleSub = "s1"
      },
      new AppUser
      {
        Id = 2,
        Email = "b@x.com",
        GoogleSub = "s2"
      }
    ] ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync() ;

    var list = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<List<UserResponse>>().Which ;
    list.Should().HaveCount( 2 ) ;
    list.Select( u => u.Id ).Should().ContainInOrder( 1, 2 ) ;
  }

  [Fact]
  public async Task SetAdminAsync_DelegatesAndReturns200()
  {
    var user = new AppUser
    {
      Id = 9,
      Email = "c@x.com",
      GoogleSub = "s",
      IsAdmin = true
    } ;
    _userService.SetAdminAsync( 9, true ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.SetAdminAsync( 9, new UpdateRoleRequest( true ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.IsAdmin.Should().BeTrue() ;
    await _userService.Received( 1 ).SetAdminAsync( 9, true ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 42 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _userService.Received( 1 ).SoftDeleteAsync( 42 ) ;
  }

  [Fact]
  public async Task SetMyTelegramIdAsync_DelegatesCallerIdAndReturns200()
  {
    var user = new AppUser
    {
      Id = 7,
      Email = "me@example.com",
      GoogleSub = "sub",
      TelegramId = 12345L
    } ;
    _userService.UpdateTelegramIdAsync( 7, 12345L ).Returns( user ) ;
    var sut = CreateSut( 7 ) ;

    var result = await sut.SetMyTelegramIdAsync( new SetTelegramIdRequest( 12345L ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.TelegramId.Should().Be( 12345L ) ;
    await _userService.Received( 1 ).UpdateTelegramIdAsync( 7, 12345L ) ;
  }

  [Fact]
  public async Task ClearMyTelegramIdAsync_DelegatesCallerIdWithNullAndReturns200()
  {
    var user = new AppUser
    {
      Id = 7,
      Email = "me@example.com",
      GoogleSub = "sub",
      TelegramId = null
    } ;
    _userService.UpdateTelegramIdAsync( 7, null ).Returns( user ) ;
    var sut = CreateSut( 7 ) ;

    var result = await sut.ClearMyTelegramIdAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.TelegramId.Should().BeNull() ;
    await _userService.Received( 1 ).UpdateTelegramIdAsync( 7, null ) ;
  }

  [Fact]
  public async Task SetTelegramIdAsync_DelegatesToRouteIdAndReturns200()
  {
    var user = new AppUser
    {
      Id = 9,
      Email = "other@x.com",
      GoogleSub = "s",
      TelegramId = 99999L
    } ;
    _userService.UpdateTelegramIdAsync( 9, 99999L ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.SetTelegramIdAsync( 9, new SetTelegramIdRequest( 99999L ) ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.TelegramId.Should().Be( 99999L ) ;
    await _userService.Received( 1 ).UpdateTelegramIdAsync( 9, 99999L ) ;
  }

  [Fact]
  public async Task ClearTelegramIdAsync_DelegatesToRouteIdWithNullAndReturns200()
  {
    var user = new AppUser
    {
      Id = 9,
      Email = "other@x.com",
      GoogleSub = "s",
      TelegramId = null
    } ;
    _userService.UpdateTelegramIdAsync( 9, null ).Returns( user ) ;
    var sut = CreateSut() ;

    var result = await sut.ClearTelegramIdAsync( 9 ) ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<UserResponse>()
      .Which.TelegramId.Should().BeNull() ;
    await _userService.Received( 1 ).UpdateTelegramIdAsync( 9, null ) ;
  }
}
