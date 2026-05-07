using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IExpenseTransactionService" />
public class ExpenseTransactionService(
  IRepository<ExpenseTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository,
  IRepository<LendTransaction> childRepository )
  : ShareableTransactionServiceBase<ExpenseTransaction, LendTransaction>(
    transactionRepository,
    spendRepository,
    categoryRepository,
    counterpartyRepository,
    childRepository ), IExpenseTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Expense ;
}
