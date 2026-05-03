using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IIncomeTransactionService" />
public class IncomeTransactionService(
  IRepository<IncomeTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository )
  : StandardTransactionServiceBase<IncomeTransaction>(
    transactionRepository, spendRepository, categoryRepository ), IIncomeTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Income ;
}
