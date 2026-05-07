namespace Fiolyn.Entities ;

/// <summary>
///   Marker for <see cref="TransactionBase" /> leaves that reference a per-user
///   <see cref="Counterparty" /> via a required <c>CounterpartyId</c> FK. Lets the shared
///   <c>CounterpartyTransactionServiceBase&lt;T&gt;</c> resolve the counterparty name on writes and
///   eager-load the nav on reads without per-leaf duplication.
/// </summary>
public interface IHasCounterparty
{
  long CounterpartyId { get ; set ; }
  Counterparty Counterparty { get ; set ; }
}
