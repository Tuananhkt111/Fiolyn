namespace Fiolyn.ServiceRegisters.Settings ;

public class JwtSettings
{
  public string Issuer { get ; set ; } = "Fiolyn" ;
  public string Audience { get ; set ; } = "FiolynMobile" ;
  public string SigningKey { get ; set ; } = string.Empty ;
  public int AccessTokenExpiryMinutes { get ; set ; } = 15 ;
  public int RefreshTokenExpiryDays { get ; set ; } = 30 ;
}