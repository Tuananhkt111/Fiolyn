namespace Fiolyn.Entities ;

/// <summary>
///   A money-in transaction (e.g. salary, refund). The signed <c>Amount</c> on the base is always
///   positive; the controller applies the sign rule. When the request includes a non-empty
///   <c>shares</c> list, <c>ShareableTransactionServiceBase</c>
///   atomically splits the amount into this row (caller's portion) and one
///   <see cref="BorrowingTransaction" /> per share (counterparty's portion, you owe them),
///   linked back via <see cref="TransactionBase.ParentTransactionId" />.
/// </summary>
public class IncomeTransaction : TransactionBase ;
