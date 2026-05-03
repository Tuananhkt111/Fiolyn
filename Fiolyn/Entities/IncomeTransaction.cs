namespace Fiolyn.Entities ;

/// <summary>
///   A money-in transaction (e.g. salary, refund). The signed <c>Amount</c> on the base is always
///   positive; the controller applies the sign rule.
/// </summary>
public class IncomeTransaction : TransactionBase ;
