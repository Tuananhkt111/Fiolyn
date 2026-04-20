using Fiolyn.Entities ;

namespace Fiolyn.Services.Internal.Interfaces ;

/// <summary>
///   Defines operations for managing stock transactions and parsing Telegram messages.
/// </summary>
public interface IStockTransactionService
{
  /// <summary>
  ///   Parses a Telegram message into a StockTransaction. Looks up BrokerAccount by name;
  ///   returns null if the format is invalid or the broker is not found.
  /// </summary>
  /// <param name="text">The raw message text starting with "stock ".</param>
  /// <returns>A populated StockTransaction, or null if the format is invalid or broker not found.</returns>
  Task<StockTransaction?> ParseMessageAsync( string text ) ;

  /// <summary>
  ///   Creates a new stock transaction.
  /// </summary>
  /// <param name="transaction">The transaction to persist.</param>
  /// <returns>The persisted transaction.</returns>
  Task<StockTransaction> CreateAsync( StockTransaction transaction ) ;

  /// <summary>
  ///   Gets all active transactions, optionally filtered by symbol.
  /// </summary>
  /// <param name="symbol">Optional ticker symbol filter.</param>
  /// <returns>List of matching transactions.</returns>
  Task<List<StockTransaction>> GetAllAsync( string? symbol = null ) ;

  /// <summary>
  ///   Gets a single transaction by ID.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  /// <returns>The transaction, or null if not found.</returns>
  Task<StockTransaction?> GetByIdAsync( long id ) ;

  /// <summary>
  ///   Checks whether an active stock transaction exists for the given ID.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  /// <returns>True if an active transaction exists; otherwise false.</returns>
  Task<bool> ExistsAsync( long id ) ;

  /// <summary>
  ///   Updates an existing transaction.
  /// </summary>
  /// <param name="transaction">The transaction with updated values.</param>
  Task UpdateAsync( StockTransaction transaction ) ;

  /// <summary>
  ///   Soft-deletes a transaction by ID.
  /// </summary>
  /// <param name="id">The transaction primary key.</param>
  Task SoftDeleteAsync( long id ) ;
}