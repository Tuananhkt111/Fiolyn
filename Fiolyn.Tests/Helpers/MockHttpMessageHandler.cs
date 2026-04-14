using System.Net ;

namespace Fiolyn.Tests.Helpers ;

public class MockHttpMessageHandler( HttpStatusCode statusCode = HttpStatusCode.OK, string responseBody = "{}" ) : HttpMessageHandler
{
  public HttpRequestMessage? LastRequest { get ; private set ; }
  public string? LastRequestContent { get ; private set ; }

  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken cancellationToken )
  {
    LastRequest = request ;
    LastRequestContent = request.Content is not null
      ? await request.Content.ReadAsStringAsync( cancellationToken )
      : null ;

    return new HttpResponseMessage( statusCode )
    {
      Content = new StringContent( responseBody )
    } ;
  }
}