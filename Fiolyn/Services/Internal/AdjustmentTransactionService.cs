using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IAdjustmentTransactionService" />
public class AdjustmentTransactionService(
  IRepository<AdjustmentTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository )
  : StandardTransactionServiceBase<AdjustmentTransaction>(
    transactionRepository, spendRepository, categoryRepository ), IAdjustmentTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Adjustment ;
}
