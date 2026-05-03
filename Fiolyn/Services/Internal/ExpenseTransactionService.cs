using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.Helpers ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="IExpenseTransactionService" />
public class ExpenseTransactionService(
  IRepository<ExpenseTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository )
  : StandardTransactionServiceBase<ExpenseTransaction>(
    transactionRepository, spendRepository, categoryRepository ), IExpenseTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Expense ;
}
