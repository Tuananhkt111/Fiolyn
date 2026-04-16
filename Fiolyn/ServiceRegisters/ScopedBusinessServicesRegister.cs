using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.External.Telegram ;
using Fiolyn.Services.Internal ;

namespace Fiolyn.ServiceRegisters ;

/// <summary>
///   Registers all scoped business logic services.
/// </summary>
public class ScopedBusinessServicesRegister : IServiceRegister
{
  /// <inheritdoc />
  public int Order => 1 ;

  /// <inheritdoc />
  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
  {
    services.AddScoped<ITelegramMessageService, TelegramMessageService>() ;
    services.AddScoped<IStockTransactionService, StockTransactionService>() ;
    services.AddScoped<IBrokerAccountService, BrokerAccountService>() ;
  }
}