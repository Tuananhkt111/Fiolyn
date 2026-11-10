using System.IdentityModel.Tokens.Jwt ;
using System.Net ;
using System.Net.Http.Headers ;
using System.Runtime.CompilerServices ;
using System.Security.Claims ;
using System.Text ;
using System.Text.Json ;
using System.Text.RegularExpressions ;
using Fiolyn.Entities ;
using Fiolyn.Tests.Integration.Helpers ;
using FluentAssertions ;
using Microsoft.AspNetCore.Mvc.Controllers ;
using Microsoft.AspNetCore.Routing ;
using Microsoft.Extensions.DependencyInjection ;
using Microsoft.IdentityModel.Tokens ;

namespace Fiolyn.Tests.Integration.Api ;

/// <summary>
///   Drives the real authentication and authorization pipeline with HTTP requests. The endpoint list is
///   read from the generated openapi.json, so a new endpoint is covered without touching this file.
///   Which endpoints are admin-only is decided here from the requirement (path rules), not from the
///   controllers' attributes.
/// </summary>
public class AuthorizationTests( FiolynApiFactory factory ) : IClassFixture<FiolynApiFactory>
{
  private const string Issuer = "Fiolyn" ;
  private const string Audience = "FiolynWeb" ;
  private const string TestSigningKey = "test-signing-key-at-least-32-characters-long" ;

  private static readonly HashSet<string> _httpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD"] ;

  private static readonly (string Method, string Path)[] _anonymousEndpoints =
  [
    ( "POST", "/api/auth/google" ),
    ( "POST", "/api/auth/refresh" ),
    ( "POST", "/api/telegram" )
  ] ;

  /// <summary>Admin groups from the requirement. Each must match at least one endpoint.</summary>
  private static readonly (string Group, Func<string, string, bool> Matches)[] _adminGroups =
  [
    ( "US-04 allowed-emails", ( _, p ) => p.StartsWith( "/api/allowed-emails" ) ),
    ( "US-08 GET /api/users", ( m, p ) => m == "GET" && p == "/api/users" ),
    ( "US-08 PATCH/DELETE /api/users/1", ( m, p ) => p == "/api/users/1" && ( m == "PATCH" || m == "DELETE" ) ),
    ( "US-08 users/{id}/telegram-id", ( m, p ) => p == "/api/users/1/telegram-id" && ( m == "PUT" || m == "DELETE" ) ),
    ( "US-11 admin spend-accounts", ( _, p ) => p.StartsWith( "/api/admin/spend-accounts" ) ),
    ( "US-14 admin broker-accounts", ( _, p ) => p.StartsWith( "/api/admin/broker-accounts" ) ),
    ( "US-16 admin categories", ( _, p ) => p.StartsWith( "/api/admin/" ) && p.Contains( "categories" ) ),
    ( "US-21 admin *-transactions", ( _, p ) => p.StartsWith( "/api/admin/" ) && p.Contains( "-transactions" ) ),
    ( "US-21 admin unified list", ( _, p ) => p == "/api/admin/transactions" )
  ] ;

  public static IEnumerable<object[]> ProtectedEndpoints() => AllEndpoints()
    .Where( e => ! IsAnonymous( e ) )
    .Select( e => new object[] { e.Method, e.Path } ) ;

  public static IEnumerable<object[]> AdminEndpoints() => AllEndpoints()
    .Where( IsAdminOnly )
    .Select( e => new object[] { e.Method, e.Path } ) ;

  public static IEnumerable<object[]> AnonymousEndpoints() => _anonymousEndpoints
    .Select( e => new object[] { e.Method, e.Path } ) ;

  /// <summary>Protected endpoints that are not admin-only: everything a plain user may reach.</summary>
  public static IEnumerable<object[]> NonAdminProtectedEndpoints() => AllEndpoints()
    .Where( e => ! IsAnonymous( e ) && ! IsAdminOnly( e ) )
    .Select( e => new object[] { e.Method, e.Path } ) ;

  [Fact]
  [Trait( "Story", "US-03" )]
  [Trait( "Story", "US-05" )]
  [Trait( "Story", "US-15" )]
  public void Enumeration_FindsEndpointsAndEveryAdminGroup()
  {
    var all = AllEndpoints() ;

    all.Should().HaveCountGreaterThan( 60, "openapi.json lists well over sixty operations" ) ;
    foreach ( var endpoint in _anonymousEndpoints )
    {
      all.Should().Contain( e => e.Method == endpoint.Method && e.Path.Equals( endpoint.Path, StringComparison.OrdinalIgnoreCase ),
        $"anonymous endpoint {endpoint.Method} {endpoint.Path} must exist" ) ;
    }

    foreach ( var ( group, matches ) in _adminGroups )
    {
      all.Count( e => matches( e.Method, e.Path ) ).Should().BeGreaterThan( 0, $"group '{group}' must match an endpoint" ) ;
    }

    all.Should().Contain( e => e.Method == "POST" && e.Path == "/api/auth/logout" ) ;
    all.Should().Contain( e => e.Method == "GET" && e.Path == "/api/users/me" ) ;
    all.Should().Contain( e => e.Method == "GET" && e.Path == "/api/transaction-categories" ) ;
    AdminEndpoints().Count().Should().BeGreaterThan( 30 ) ;
  }

  [Theory]
  [MemberData( nameof( ProtectedEndpoints ) )]
  [Trait( "Story", "US-03" )]
  [Trait( "Story", "US-05" )]
  [Trait( "Story", "US-15" )]
  public async Task Protected_WithoutToken_Returns401( string method, string path )
  {
    using var client = factory.Anonymous() ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Unauthorized ) ;
    // The bearer challenge proves the auth pipeline refused it. A 401 without it comes from a service
    // throwing UnauthorizedAccessException after the request was let in.
    response.Headers.WwwAuthenticate.Should().Contain( h => h.Scheme == "Bearer" ) ;
  }

  [Theory]
  [MemberData( nameof( AnonymousEndpoints ) )]
  [Trait( "Story", "US-01" )]
  [Trait( "Story", "US-03" )]
  public async Task Anonymous_WithoutToken_IsNotRejectedWith401( string method, string path )
  {
    using var client = factory.Anonymous() ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().NotBe( HttpStatusCode.Unauthorized ) ;
    response.Headers.WwwAuthenticate.Should().NotContain( h => h.Scheme == "Bearer", "no authentication challenge may be issued" ) ;
  }

  [Fact]
  [Trait( "Story", "US-03" )]
  [Trait( "Story", "US-05" )]
  [Trait( "Story", "US-15" )]
  public void AppRoutes_MatchOpenApiOperations()
  {
    var endpointSource = factory.Services.GetRequiredService<EndpointDataSource>() ;
    var live = endpointSource.Endpoints
      .OfType<RouteEndpoint>()
      .Where( e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null )
      .SelectMany( e => ( e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [] )
        .Select( m => $"{m.ToUpperInvariant()} {NormalisePath( e.RoutePattern.RawText ?? "" )}" ) )
      .Where( o => ! o.StartsWith( "HEAD " ) )
      .ToHashSet() ;
    var documented = AllEndpoints()
      .Where( e => e.Method != "HEAD" )
      .Select( e => $"{e.Method} {e.TemplatePath}" )
      .ToHashSet() ;

    var missingFromOpenApi = live.Except( documented ).Order().ToList() ;
    var notInApp = documented.Except( live ).Order().ToList() ;

    ( missingFromOpenApi.Count + notInApp.Count ).Should().Be( 0,
      "regenerate docs/reference/openapi.json so the tests cover every real route. "
      + $"In the app but not in openapi.json: [{string.Join( ", ", missingFromOpenApi )}]. "
      + $"In openapi.json but not in the app: [{string.Join( ", ", notInApp )}]" ) ;
  }

  private static string NormalisePath( string path ) =>
    Regex.Replace( "/" + path.Trim( '/' ), @"\{[^}]+\}", "{}" ).ToLowerInvariant() ;

  [Theory]
  [MemberData( nameof( ProtectedEndpoints ) )]
  [Trait( "Story", "US-03" )]
  public async Task Protected_TokenSignedWithWrongKey_Returns401( string method, string path )
  {
    var token = BuildToken( signingKey: "another-signing-key-at-least-32-characters-long" ) ;
    await AssertUnauthorized( method, path, token ) ;
  }

  [Theory]
  [MemberData( nameof( ProtectedEndpoints ) )]
  [Trait( "Story", "US-03" )]
  public async Task Protected_ExpiredToken_Returns401( string method, string path )
  {
    var token = BuildToken( notBefore: DateTime.UtcNow.AddHours( -2 ), expires: DateTime.UtcNow.AddHours( -1 ) ) ;
    await AssertUnauthorized( method, path, token ) ;
  }

  [Theory]
  [MemberData( nameof( ProtectedEndpoints ) )]
  [Trait( "Story", "US-03" )]
  public async Task Protected_WrongIssuerToken_Returns401( string method, string path )
  {
    var token = BuildToken( issuer: "SomeoneElse" ) ;
    await AssertUnauthorized( method, path, token ) ;
  }

  [Theory]
  [MemberData( nameof( ProtectedEndpoints ) )]
  [Trait( "Story", "US-03" )]
  public async Task Protected_WrongAudienceToken_Returns401( string method, string path )
  {
    var token = BuildToken( audience: "SomeoneElse" ) ;
    await AssertUnauthorized( method, path, token ) ;
  }

  [Fact]
  [Trait( "Story", "US-03" )]
  public async Task Protected_ValidHandBuiltToken_IsAccepted()
  {
    // Control for the bad-token tests: the same builder with correct parameters must pass, so a 401 above
    // is caused by the one bad parameter and not by the builder.
    using var client = ClientWithToken( BuildToken( admin: true ) ) ;

    var response = await Send( client, "GET", "/api/users/me" ) ;

    response.StatusCode.Should().NotBe( HttpStatusCode.Unauthorized ) ;
  }

  [Theory]
  [MemberData( nameof( AdminEndpoints ) )]
  [Trait( "Story", "US-04" )]
  [Trait( "Story", "US-08" )]
  [Trait( "Story", "US-11" )]
  [Trait( "Story", "US-14" )]
  [Trait( "Story", "US-16" )]
  [Trait( "Story", "US-21" )]
  public async Task AdminOnly_NonAdminToken_Returns403( string method, string path )
  {
    using var client = factory.As( new AppUser { Id = 2, Email = "member@example.com", IsAdmin = false } ) ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Forbidden ) ;
  }

  [Theory]
  [MemberData( nameof( AdminEndpoints ) )]
  [Trait( "Story", "US-04" )]
  [Trait( "Story", "US-08" )]
  [Trait( "Story", "US-11" )]
  [Trait( "Story", "US-14" )]
  [Trait( "Story", "US-16" )]
  [Trait( "Story", "US-21" )]
  public async Task AdminOnly_AdminToken_IsNeitherUnauthorizedNorForbidden( string method, string path )
  {
    using var client = factory.As( new AppUser { Id = 1, Email = "admin@example.com", IsAdmin = true } ) ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().NotBe( HttpStatusCode.Unauthorized ) ;
    response.StatusCode.Should().NotBe( HttpStatusCode.Forbidden ) ;
  }

  [Theory]
  [MemberData( nameof( NonAdminProtectedEndpoints ) )]
  [Trait( "Story", "US-05" )]
  [Trait( "Story", "US-08" )]
  public async Task NonAdminEndpoint_NonAdminToken_IsNeitherUnauthorizedNorForbidden( string method, string path )
  {
    // Includes the caller's own /api/users/me... endpoints, which are not admin-only.
    using var client = factory.As( new AppUser { Id = 2, Email = "member@example.com", IsAdmin = false } ) ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().NotBe( HttpStatusCode.Unauthorized ) ;
    response.StatusCode.Should().NotBe( HttpStatusCode.Forbidden ) ;
  }

  private async Task AssertUnauthorized( string method, string path, string token )
  {
    using var client = ClientWithToken( token ) ;

    var response = await Send( client, method, path ) ;

    response.StatusCode.Should().Be( HttpStatusCode.Unauthorized ) ;
    // The bearer challenge proves the auth pipeline refused it. A 401 without it comes from a service
    // throwing UnauthorizedAccessException after the request was let in.
    response.Headers.WwwAuthenticate.Should().Contain( h => h.Scheme == "Bearer" ) ;
  }

  private HttpClient ClientWithToken( string token )
  {
    var client = factory.Anonymous() ;
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue( "Bearer", token ) ;
    return client ;
  }

  private static Task<HttpResponseMessage> Send( HttpClient client, string method, string path )
  {
    var request = new HttpRequestMessage( new HttpMethod( method ), path ) ;
    if ( method is "POST" or "PUT" or "PATCH" )
    {
      request.Content = new StringContent( "{}", Encoding.UTF8, "application/json" ) ;
    }

    return client.SendAsync( request ) ;
  }

  private static string BuildToken(
    string signingKey = TestSigningKey,
    string issuer = Issuer,
    string audience = Audience,
    DateTime? notBefore = null,
    DateTime? expires = null,
    bool admin = false )
  {
    var credentials = new SigningCredentials(
      new SymmetricSecurityKey( Encoding.UTF8.GetBytes( signingKey ) ), SecurityAlgorithms.HmacSha256 ) ;
    var token = new JwtSecurityToken(
      issuer,
      audience,
      [
        new Claim( JwtRegisteredClaimNames.Sub, "1" ),
        new Claim( JwtRegisteredClaimNames.Email, "admin@example.com" ),
        new Claim( "isAdmin", admin ? "true" : "false" )
      ],
      notBefore ?? DateTime.UtcNow.AddMinutes( -1 ),
      expires ?? DateTime.UtcNow.AddHours( 1 ),
      credentials ) ;
    return new JwtSecurityTokenHandler().WriteToken( token ) ;
  }

  private static bool IsAnonymous( ( string Method, string Path, string TemplatePath ) e ) =>
    _anonymousEndpoints.Any( a => a.Method == e.Method && a.Path.Equals( e.Path, StringComparison.OrdinalIgnoreCase ) ) ;

  private static bool IsAdminOnly( ( string Method, string Path, string TemplatePath ) e ) =>
    _adminGroups.Any( g => g.Matches( e.Method, e.Path ) ) ;

  private static List<( string Method, string Path, string TemplatePath )> AllEndpoints( [CallerFilePath] string thisFile = "" )
  {
    var dir = new DirectoryInfo( Path.GetDirectoryName( thisFile )! ) ;
    while ( dir is not null && ! File.Exists( Path.Combine( dir.FullName, "docs", "reference", "openapi.json" ) ) )
    {
      dir = dir.Parent ;
    }

    dir.Should().NotBeNull( "docs/reference/openapi.json must be reachable from the test sources" ) ;
    using var doc = JsonDocument.Parse( File.ReadAllText( Path.Combine( dir!.FullName, "docs", "reference", "openapi.json" ) ) ) ;

    var endpoints = new List<( string, string, string )>() ;
    foreach ( var path in doc.RootElement.GetProperty( "paths" ).EnumerateObject() )
    {
      var concrete = Regex.Replace( path.Name, @"\{[^}]+\}", "1" ) ;
      foreach ( var op in path.Value.EnumerateObject() )
      {
        var method = op.Name.ToUpperInvariant() ;
        if ( _httpMethods.Contains( method ) )
        {
          endpoints.Add( ( method, concrete, NormalisePath( path.Name ) ) ) ;
        }
      }
    }

    return endpoints ;
  }
}
