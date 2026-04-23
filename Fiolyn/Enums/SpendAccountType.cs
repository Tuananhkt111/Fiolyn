namespace Fiolyn.Enums ;

/// <summary>
///   Categorizes a spend account as cash-based (wallet, bank account) or credit-based (credit card).
/// </summary>
public enum SpendAccountType
{
  /// <summary>
  ///   Cash-based account. Balance represents owned funds and cannot normally go negative.
  /// </summary>
  Cash,

  /// <summary>
  ///   Credit-based account. Balance represents outstanding debt and may be negative when carrying a balance.
  /// </summary>
  Credit
}
