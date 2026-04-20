using System.Text ;
using Fiolyn.Constants ;
using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.ServiceRegisters.Settings ;
using Fiolyn.Services.External.Google ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;
using Microsoft.AspNetCore.Authentication.JwtBearer ;
using Microsoft.IdentityModel.Tokens ;

namespace Fiolyn.ServiceRegisters ;

/// <summary>
///   Registers the JWT bearer authentication scheme, the "Admin" authorization policy,
///   the auth-related settings classes, and the token-plumbing services
///   (<see cref="IGoogleTokenVerifier" />, <see cref="IJwtService" />).
/// </summary>
public class AuthServiceRegister : IServiceRegister
{
  public int Order => 1 ;

  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    var jwtSection = configuration.GetSection( "Jwt" ) ;
    services.Configure<JwtSettings>( jwtSection ) ;
    services.Configure<GoogleAuthSettings>( configuration.GetSection( "GoogleAuth" ) ) ;
    services.Configure<InitialAdminSettings>( configuration.GetSection( "InitialAdmin" ) ) ;

    var jwtSettings = jwtSection.Get<JwtSettings>() ?? new JwtSettings() ;
    if ( string.IsNullOrWhiteSpace( jwtSettings.SigningKey ) || jwtSettings.SigningKey.Length < 32 )
    {
      throw new InvalidOperationException( "Jwt:SigningKey must be configured and at least 32 characters long." ) ;
    }

    var signingKeyBytes = Encoding.UTF8.GetBytes( jwtSettings.SigningKey ) ;

    services
      .AddAuthentication( JwtBearerDefaults.AuthenticationScheme )
      .AddJwtBearer( options =>
      {
        options.TokenValidationParameters = new TokenValidationParameters
        {
          ValidateIssuer = true,
          ValidIssuer = jwtSettings.Issuer,
          ValidateAudience = true,
          ValidAudience = jwtSettings.Audience,
          ValidateLifetime = true,
          ValidateIssuerSigningKey = true,
          IssuerSigningKey = new SymmetricSecurityKey( signingKeyBytes ),
          ClockSkew = TimeSpan.Zero
        } ;
      } ) ;

    services.AddAuthorizationBuilder()
      .AddPolicy( AuthConstants.AdminPolicyName, policy =>
        policy.RequireClaim( AuthConstants.AdminClaimName, AuthConstants.AdminClaimTrueValue ) ) ;

    services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>() ;
    services.AddScoped<IJwtService, JwtService>() ;
  }
}