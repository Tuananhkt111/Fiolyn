namespace Fiolyn.Services.Internal.BaseServices ;

/// <summary>
///   A single counterparty share supplied on a create / update request: free-text name that
///   gets resolved (or created) per-user, and a whole-percent slice.
/// </summary>
public record TransactionShareInput( string CounterpartyName, int Percentage ) ;
