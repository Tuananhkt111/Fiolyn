using Fiolyn.Data.Repositories ;
using Fiolyn.Entities ;
using Fiolyn.Enums ;
using Fiolyn.Services.Internal.BaseServices ;
using Fiolyn.Services.Internal.Interfaces ;

namespace Fiolyn.Services.Internal ;

/// <inheritdoc cref="ILendTransactionService" />
public class LendTransactionService(
  IRepository<LendTransaction> transactionRepository,
  IRepository<SpendAccount> spendRepository,
  IRepository<TransactionCategory> categoryRepository,
  IRepository<Counterparty> counterpartyRepository )
  : CounterpartyTransactionServiceBase<LendTransaction>(
      transactionRepository, spendRepository, categoryRepository, counterpartyRepository ),
    ILendTransactionService
{
  protected override TransactionKind Kind => TransactionKind.Lend ;
}
