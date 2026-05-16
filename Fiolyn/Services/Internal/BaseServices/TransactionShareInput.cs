namespace Fiolyn.Services.Internal.BaseServices ;

/// <summary>
///   A single counterparty share supplied on a create / update request: free-text name that
///   gets resolved (or created) per-user, and a percent slice in [0.01, 100] with up to 2
///   decimal places (extra precision is rounded server-side).
/// </summary>
public record TransactionShareInput( string CounterpartyName, double Percentage ) ;
