namespace Fiolyn.Entities.Base ;

/// <summary>
///   Marks an entity as supporting soft deletes (logical deletion without removing from database).
/// </summary>
/// <remarks>
///   Soft deletes allow entities to be marked as deleted while remaining in the database.
///   This preserves referential integrity and historical data while filtering deleted records in queries.
/// </remarks>
public interface ISoftDelete
{
  /// <summary>
  ///   Gets or sets a value indicating whether the entity has been soft-deleted.
  /// </summary>
  bool IsDeleted { get ; set ; }
}
