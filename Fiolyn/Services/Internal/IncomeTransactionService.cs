using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IIncomeTransactionService" />
public class IncomeTransactionService(
  IRepository<IncomeTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository,
  IRepository<BorrowingTransaction> childRepository )
  : ShareableTransactionServiceBase<IncomeTransaction, BorrowingTransaction>(
    transactionRepository,
    spendRepository,
    categoryRepository,
    counterpartyRepository,
    childRepository ), IIncomeTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Income ;
}
