using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="ILendTransactionService" />
public class LendTransactionService(
  IRepository<LendTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository )
  : StandardTransactionServiceBase<LendTransaction>(
    transactionRepository, spendRepository, categoryRepository ), ILendTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Lend ;
}
