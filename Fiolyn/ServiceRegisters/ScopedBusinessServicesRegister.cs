using Fiolyn.ServiceRegisters.Base ;
using Fiolyn.Services.External.Telegram ;
using Fiolyn.Services.Internal ;
using Fiolyn.Services.Internal.Interfaces ;

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
    services.AddScoped<ISpendAccountService, SpendAccountService>() ;
    services.AddScoped<IAuthService, AuthService>() ;
    services.AddScoped<IAllowedEmailService, AllowedEmailService>() ;
    services.AddScoped<IUserService, UserService>() ;
    services.AddScoped<ITransactionCategoryService, TransactionCategoryService>() ;
    services.AddScoped<ITransactionReadService, TransactionReadService>() ;
    services.AddScoped<IIncomeTransactionService, IncomeTransactionService>() ;
    services.AddScoped<IExpenseTransactionService, ExpenseTransactionService>() ;
    services.AddScoped<ILendTransactionService, LendTransactionService>() ;
    services.AddScoped<IBorrowingTransactionService, BorrowingTransactionService>() ;
    services.AddScoped<ITransferTransactionService, TransferTransactionService>() ;
    services.AddScoped<IAdjustmentTransactionService, AdjustmentTransactionService>() ;
    services.AddScoped<ICounterpartyService, CounterpartyService>() ;
    services.AddScoped<IObligationService, ObligationService>() ;
  }
}
