using System.Text ;
using Fiolyn.Controllers ;
using Fiolyn.Services.External.Telegram ;
using Fiolyn.Services.External.Telegram.Results ;
using FluentAssertions ;
using Microsoft.AspNetCore.Http ;
using Microsoft.AspNetCore.Mvc ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Controllers ;

public class TelegramControllerTests
{
  private readonly ITelegramMessageService _messageService = Substitute.For<ITelegramMessageService>() ;

  private TelegramController CreateSut( string requestBody = "{}" )
  {
    var controller = new TelegramController( _messageService ) ;
    var context = new DefaultHttpContext
    {
      Request =
      {
        Body = new MemoryStream( Encoding.UTF8.GetBytes( requestBody ) )
      }
    } ;
    controller.ControllerContext = new ControllerContext
    {
      HttpContext = context
    } ;
    return controller ;
  }

  [Fact]
  public async Task PostAsync_ServiceReturnsSuccess_Returns200()
  {
    _messageService.ProcessWebhookAsync( Arg.Any<string>() ).Returns( ProcessResult.Success() ) ;
    var sut = CreateSut() ;

    var result = await sut.PostAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.StatusCode.Should().Be( 200 ) ;
  }

  [Fact]
  public async Task PostAsync_ServiceReturnsSkipped_Returns200()
  {
    _messageService.ProcessWebhookAsync( Arg.Any<string>() ).Returns( ProcessResult.Skipped( "no text" ) ) ;
    var sut = CreateSut() ;

    var result = await sut.PostAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.StatusCode.Should().Be( 200 ) ;
  }

  [Fact]
  public async Task PostAsync_ServiceReturnsError_Returns200()
  {
    _messageService.ProcessWebhookAsync( Arg.Any<string>() ).Returns( ProcessResult.Error( "crash" ) ) ;
    var sut = CreateSut() ;

    var result = await sut.PostAsync() ;

    result.Should().BeOfType<OkObjectResult>()
      .Which.StatusCode.Should().Be( 200 ) ;
  }

  [Fact]
  public async Task PostAsync_PassesRawBodyToService()
  {
    const string body = """{"message":{"from":{"id":1},"text":"raw body test"}}""" ;
    _messageService.ProcessWebhookAsync( Arg.Any<string>() ).Returns( ProcessResult.Success() ) ;
    var sut = CreateSut( body ) ;

    await sut.PostAsync() ;

    await _messageService.Received( 1 ).ProcessWebhookAsync( body ) ;
  }
}
