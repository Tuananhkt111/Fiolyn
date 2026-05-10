using System.IdentityModel.Tokens.Jwt ;
using System.Security.Claims ;
using Fiolyn.Controllers ;
using Fiolyn.Controllers.DTOs.Requests.AllowedEmail ;
using Fiolyn.Controllers.DTOs.Responses.AllowedEmail ;
using Fiolyn.Entities ;
using Fiolyn.Services.Internal.Interfaces ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class AllowedEmailControllerTests
{
  private readonly IAllowedEmailService _service = Substitute.For<IAllowedEmailService>() ;

  private AllowedEmailController CreateSut( long? callerId = null )
  {
    var controller = new AllowedEmailController( _service ) ;
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
  public async Task GetAllAsync_Returns200WithMappedList()
  {
    _service.GetAllAsync( Arg.Any<PaginationQuery>() )
      .Returns( PagedResultHelpers.SinglePage(
        new AllowedEmail
        {
          Id = 1,
          Email = "a@x.com"
        },
        new AllowedEmail
        {
          Id = 2,
          Email = "b@x.com"
        } ) ) ;
    var sut = CreateSut() ;

    var result = await sut.GetAllAsync( new PaginationQuery() ) ;

    var page = result.Should().BeOfType<OkObjectResult>()
      .Which.Value.Should().BeOfType<PagedResult<AllowedEmailResponse>>().Which ;
    page.Items.Should().HaveCount( 2 ) ;
    page.Items.Select( a => a.Email ).Should().ContainInOrder( "a@x.com", "b@x.com" ) ;
  }

  [Fact]
  public async Task CreateAsync_Returns201WithInviterAttributed()
  {
    var entry = new AllowedEmail
    {
      Id = 5,
      Email = "new@x.com",
      Note = "hello",
      InvitedByUserId = 7
    } ;
    _service.CreateAsync( "new@x.com", "hello", 7L ).Returns( entry ) ;
    var sut = CreateSut( 7 ) ;

    var result = await sut.CreateAsync( new AllowedEmailRequest( "new@x.com", "hello" ) ) ;

    result.Should().BeOfType<CreatedAtRouteResult>()
      .Which.Value.Should().BeOfType<AllowedEmailResponse>()
      .Which.InvitedByUserId.Should().Be( 7 ) ;
    await _service.Received( 1 ).CreateAsync( "new@x.com", "hello", 7L ) ;
  }

  [Fact]
  public async Task DeleteAsync_Returns204()
  {
    var sut = CreateSut() ;

    var result = await sut.DeleteAsync( 42 ) ;

    result.Should().BeOfType<NoContentResult>() ;
    await _service.Received( 1 ).SoftDeleteAsync( 42 ) ;
  }
}
