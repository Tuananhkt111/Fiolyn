namespace Fiolyn.Entities ;

/// <summary>
///   A money-out transaction (e.g. groceries, rent). The signed <c>Amount</c> on the base is always
///   negative; the controller applies the sign rule.
/// </summary>
public class ExpenseTransaction : TransactionBase ;
