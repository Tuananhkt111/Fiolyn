using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IBorrowingTransactionService" />
public class BorrowingTransactionService(
  IRepository<BorrowingTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository )
  : StandardTransactionServiceBase<BorrowingTransaction>(
    transactionRepository, spendRepository, categoryRepository ), IBorrowingTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Borrowing ;
}
