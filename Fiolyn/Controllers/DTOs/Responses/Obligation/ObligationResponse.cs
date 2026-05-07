namespace Fiolyn.Controllers.DTOs.Responses.Obligation ;

/// <summary>
///   Per-counterparty obligation summary for the calling user. <c>Receivable</c> sums the
///   counterparty's <c>LendTransaction</c> magnitudes (what they owe you); <c>Payable</c> sums
///   <c>BorrowingTransaction</c> magnitudes (what you owe them). Auto-paired share children
///   are regular Lend / Borrow rows so they fall into the same buckets.
///   <c>Net = Receivable - Payable</c>; positive means you are net-owed, negative means you owe.
/// </summary>
public record ObligationResponse(
  long CounterpartyId,
  string CounterpartyName,
  long Receivable,
  long Payable,
  long Net ) ;
