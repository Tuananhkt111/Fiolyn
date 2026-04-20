namespace Fiolyn.Data.Repositories ;

/// <summary>
///   Generic repository interface for data access operations.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public interface IRepository<T> where T : class
{
  /// <summary>
  ///   Gets a queryable collection of all entities (including soft-deleted).
  /// </summary>
  /// <returns>An IQueryable of all entities for composing queries.</returns>
  IQueryable<T> Query() ;

  /// <summary>
  ///   Gets a queryable collection of non-deleted entities.
  ///   For entities that don't implement ISoftDelete, returns all entities.
  /// </summary>
  /// <returns>An IQueryable of non-deleted entities.</returns>
  IQueryable<T> QueryActive() ;

  /// <summary>
  ///   Gets an entity by its primary key.
  /// </summary>
  /// <param name="id">The primary key value.</param>
  /// <returns>The entity if found; otherwise null.</returns>
  Task<T?> GetByIdAsync( object id ) ;

  /// <summary>
  ///   Gets a non-deleted entity by its primary key.
  ///   Returns null if the entity is not found or has been soft-deleted.
  /// </summary>
  /// <param name="id">The primary key value.</param>
  /// <returns>The entity if found and active; otherwise null.</returns>
  Task<T?> GetActiveByIdAsync( object id ) ;

  /// <summary>
  ///   Gets a non-deleted entity by its primary key, or throws
  ///   <see cref="KeyNotFoundException" /> (→ HTTP 404) if not found or soft-deleted.
  /// </summary>
  /// <param name="id">The primary key value.</param>
  Task<T> GetActiveByIdOrThrowAsync( object id ) ;

  /// <summary>
  ///   Adds a new entity to the database.
  /// </summary>
  /// <param name="entity">The entity to add.</param>
  Task AddAsync( T entity ) ;

  /// <summary>
  ///   Adds multiple entities to the database.
  /// </summary>
  /// <param name="entities">The entities to add.</param>
  Task InsertRangeAsync( IEnumerable<T> entities ) ;

  /// <summary>
  ///   Attaches an entity to the context for tracking without modifying the database.
  ///   Useful when an entity comes from an external source or detached state.
  /// </summary>
  /// <param name="entity">The entity to attach.</param>
  void Attach( T entity ) ;

  /// <summary>
  ///   Updates an existing entity.
  /// </summary>
  /// <param name="entity">The entity to update.</param>
  void Update( T entity ) ;

  /// <summary>
  ///   Updates multiple entities.
  /// </summary>
  /// <param name="entities">The entities to update.</param>
  void UpdateRange( IEnumerable<T> entities ) ;

  /// <summary>
  ///   Performs a hard delete (removes entity from database).
  /// </summary>
  /// <param name="entity">The entity to delete.</param>
  void Delete( T entity ) ;

  /// <summary>
  ///   Performs a soft delete (marks entity as deleted without removing from database).
  ///   Only works if the entity implements ISoftDelete.
  /// </summary>
  /// <param name="entity">The entity to soft-delete.</param>
  void SoftDelete( T entity ) ;

  /// <summary>
  ///   Restores a soft-deleted entity.
  ///   Only works if the entity implements ISoftDelete.
  /// </summary>
  /// <param name="entity">The entity to restore.</param>
  void Restore( T entity ) ;

  /// <summary>
  ///   Saves all pending changes to the database.
  /// </summary>
  Task SaveAsync() ;
}