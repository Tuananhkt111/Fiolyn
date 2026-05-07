using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IBorrowingTransactionService" />
public class BorrowingTransactionService(
  IRepository<BorrowingTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository )
  : CounterpartyTransactionServiceBase<BorrowingTransaction>(
      transactionRepository, spendRepository, categoryRepository, counterpartyRepository ),
    IBorrowingTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Borrowing ;
}
