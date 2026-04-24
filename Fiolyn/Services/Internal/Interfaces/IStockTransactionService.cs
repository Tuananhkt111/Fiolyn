using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Defines operations for managing stock transactions and parsing Telegram messages.
///   Access flows transitively through <see cref="BrokerAccount.Members" /> — user-scoped methods
///   treat "caller is not a member of the transaction's broker" as 404.
/// </summary>
public interface IStockTransactionService
{
  /// <summary>
  ///   Parses a Telegram message into a StockTransaction. Looks up BrokerAccount by name globally
  ///   (no user scoping); returns null if the format is invalid or the broker is not found.
  /// </summary>
  /// <param name="text">The raw message text starting with "stock ".</param>
  /// <returns>A populated StockTransaction, or null if the format is invalid or broker not found.</returns>
  Task<StockTransaction?> ParseMessageAsync( string text ) ;

  /// <summary>
  ///   Creates a new stock transaction. Verifies the caller is a member of the referenced
  ///   <see cref="BrokerAccount" />; throws <see cref="KeyNotFoundException" /> if not.
  /// </summary>
  /// <param name="transaction">The transaction to persist (with <c>BrokerAccountId</c> already set).</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <returns>The persisted transaction.</returns>
  Task<StockTransaction> CreateAsync( StockTransaction transaction, long userId ) ;

  /// <summary>
  ///   Creates a new stock transaction on behalf of the Telegram ingest pipeline — bypasses the
  ///   member-of-broker check. For trusted internal callers only.
  /// </summary>
  /// <param name="transaction">The transaction to persist.</param>
  /// <returns>The persisted transaction.</returns>
  Task<StockTransaction> CreateFromTelegramAsync( StockTransaction transaction ) ;

  /// <summary>
  ///   Gets active transactions on brokers the caller is a member of, optionally filtered by symbol.
  /// </summary>
  /// <param name="userId">The caller's primary key.</param>
  /// <param name="symbol">Optional ticker symbol filter.</param>
  /// <returns>List of matching transactions.</returns>
  Task<List<StockTransaction>> GetAllForUserAsync( long userId, string? symbol = null ) ;

  /// <summary>
  ///   Gets a single transaction by ID, requiring the caller to be a member of its broker.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  /// <returns>The transaction, or null if not found or caller is not a member.</returns>
  Task<StockTransaction?> GetByIdForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Checks whether an active transaction exists and the caller is a member of its broker.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  Task<bool> ExistsForUserAsync( long id, long userId ) ;

  /// <summary>
  ///   Updates an existing transaction. Verifies the caller is a member of the broker the
  ///   transaction currently references (after any reassignment).
  /// </summary>
  /// <param name="transaction">The transaction with updated values.</param>
  /// <param name="userId">The caller's primary key.</param>
  Task UpdateAsync( StockTransaction transaction, long userId ) ;

  /// <summary>
  ///   Soft-deletes a transaction by ID. Caller must be a member of its broker.
  ///   Throws <see cref="KeyNotFoundException" /> if not found or caller is not a member.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  /// <param name="userId">The caller's primary key.</param>
  Task SoftDeleteAsync( long id, long userId ) ;

  /// <summary>
  ///   (Admin) Gets active transactions across all users. Optional filters by user membership and symbol.
  /// </summary>
  /// <param name="userId">Optional — when supplied, narrows to transactions on brokers that user is a member of.</param>
  /// <param name="symbol">Optional ticker symbol filter.</param>
  Task<List<StockTransaction>> GetAllAsync( long? userId = null, string? symbol = null ) ;

  /// <summary>
  ///   (Admin) Gets any active transaction by ID without user-scoping.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  Task<StockTransaction?> GetByIdAsync( long id ) ;

  /// <summary>
  ///   (Admin) Soft-deletes any transaction by ID without user-scoping.
  ///   Throws <see cref="KeyNotFoundException" /> if not found.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  Task SoftDeleteAsync( long id ) ;
}
