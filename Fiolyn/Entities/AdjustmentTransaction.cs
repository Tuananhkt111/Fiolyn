namespace Fiolyn.Entities ;

/// <summary>
///   Manual signed balance correction. Replaces the legacy
///   <c>POST /spend-accounts/{id}/balance-adjustments</c> endpoint by attaching the same
///   audit trail that every other transaction carries (date, description, payer, optional category).
/// </summary>
public class AdjustmentTransaction : TransactionBase ;
