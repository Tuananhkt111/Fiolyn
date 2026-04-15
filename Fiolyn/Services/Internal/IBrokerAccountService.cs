using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal ;

/// <summary>
///   Defines operations for managing broker accounts.
/// </summary>
public interface IBrokerAccountService
{
  /// <summary>
  ///   Creates a new broker account.
  /// </summary>
  /// <param name="brokerAccount">The broker account to persist.</param>
  /// <returns>The persisted broker account.</returns>
  Task<BrokerAccount> CreateAsync( BrokerAccount brokerAccount ) ;

  /// <summary>
  ///   Gets all active broker accounts.
  /// </summary>
  /// <returns>List of active broker accounts.</returns>
  Task<List<BrokerAccount>> GetAllAsync() ;

  /// <summary>
  ///   Gets a single broker account by ID.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  /// <returns>The broker account, or null if not found.</returns>
  Task<BrokerAccount?> GetByIdAsync( long id ) ;

  /// <summary>
  ///   Updates an existing broker account.
  /// </summary>
  /// <param name="brokerAccount">The broker account with updated values.</param>
  Task UpdateAsync( BrokerAccount brokerAccount ) ;

  /// <summary>
  ///   Soft-deletes a broker account by ID.
  /// </summary>
  /// <param name="id">The broker account primary key.</param>
  Task SoftDeleteAsync( long id ) ;
}