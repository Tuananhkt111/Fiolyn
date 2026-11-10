using System.Net ;
using System.Net.Http.Headers ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Notion ;
using Fiolyn.Tests.Helpers ;
using FluentAssertions ;
using Microsoft.Extensions.Logging ;
using Microsoft.Extensions.Options ;
using NSubstitute ;

namespace Fiolyn.Tests.Unit.Services ;

public class NotionServiceTests
{
  private readonly NotionSettings _settings = new()
  {
    NotionApiKey = "test-api-key",
    NotionDatabaseId = "test-db-id"
  } ;

  private (NotionService sut, MockHttpMessageHandler handler) CreateSut(
    HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}" )
  {
    var handler = new MockHttpMessageHandler( statusCode, responseBody ) ;
    var httpClient = new HttpClient( handler )
    {
      BaseAddress = new Uri( "https://api.notion.com/v1/" )
    } ;
    httpClient.DefaultRequestHeaders.Authorization =
      new AuthenticationHeaderValue( "Bearer", _settings.NotionApiKey ) ;
    var options = Options.Create( _settings ) ;
    var logger = Substitute.For<ILogger<NotionService>>() ;
    var sut = new NotionService( options, httpClient, logger ) ;
    return ( sut, handler ) ;
  }

  [Fact]
  [Trait( "Story", "US-25" )]
  public async Task AppendAsync_SuccessResponse_DoesNotThrow()
  {
    var (sut, _) = CreateSut() ;

    var act = async () => await sut.AppendAsync( "test message" ) ;

    await act.Should().NotThrowAsync() ;
  }

  [Fact]
  [Trait( "Story", "US-25" )]
  public async Task AppendAsync_ErrorResponse_ThrowsInvalidOperationException()
  {
    var (sut, _) = CreateSut( HttpStatusCode.BadRequest, """{"message":"Bad Request"}""" ) ;

    var act = async () => await sut.AppendAsync( "test message" ) ;

    await act.Should().ThrowAsync<InvalidOperationException>()
      .WithMessage( "*BadRequest*" ) ;
  }

  [Fact]
  [Trait( "Story", "US-25" )]
  public async Task AppendAsync_SendsPayloadWithDatabaseIdAndText()
  {
    var (sut, handler) = CreateSut() ;

    await sut.AppendAsync( "my note" ) ;

    handler.LastRequestContent.Should().NotBeNull() ;
    handler.LastRequestContent!.Should().Contain( "test-db-id" ) ;
    handler.LastRequestContent!.Should().Contain( "my note" ) ;
    handler.LastRequestContent!.Should().Contain( "database_id" ) ;
    handler.LastRequestContent!.Should().Contain( "title" ) ;
  }

  [Fact]
  [Trait( "Story", "US-25" )]
  public async Task AppendAsync_SetsAuthorizationHeader()
  {
    var (sut, handler) = CreateSut() ;

    await sut.AppendAsync( "msg" ) ;

    handler.LastRequest.Should().NotBeNull() ;
    handler.LastRequest!.Headers.Authorization.Should().NotBeNull() ;
    handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be( "Bearer" ) ;
    handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be( "test-api-key" ) ;
  }
}
