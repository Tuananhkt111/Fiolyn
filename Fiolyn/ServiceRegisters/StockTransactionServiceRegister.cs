using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.Internal ;

namespace Fiolyn.ServiceRegisters ;

public class StockTransactionServiceRegister : IServiceRegister
{
  public int Order => 1 ;

  public void RegisterServices( IServiceCollection services, IConfiguration configuration )
    => services.AddScoped<IStockTransactionService, StockTransactionService>() ;
}