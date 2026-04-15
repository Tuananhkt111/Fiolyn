using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.Internal ;

namespace Fiolyn.ServiceRegisters ;

/// <summary>
///   Registers broker account services.
/// </summary>
public class BrokerAccountServiceRegister : IServiceRegister
{
  /// <inheritdoc />
  public int Order => 1 ;

  /// <inheritdoc />
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
    => services.AddScoped<IBrokerAccountService, BrokerAccountService>() ;
}