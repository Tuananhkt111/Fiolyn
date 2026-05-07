namespace Fiolyn.Entities ;

/// <summary>
///   A money-out transaction (e.g. groceries, rent). The signed <c>Amount</c> on the base is always
///   negative; the controller applies the sign rule. When the request includes a non-empty
///   <c>shares</c> list, <c>ShareableTransactionServiceBase</c>
///   atomically splits the amount into this row (caller's portion) and one
///   <see cref="LendTransaction" /> per share (counterparty's portion, owed to you), linked
///   back via <see cref="TransactionBase.ParentTransactionId" />.
/// </summary>
public class ExpenseTransaction : TransactionBase ;
