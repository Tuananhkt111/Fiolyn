using Fiolyn.Entities.Base ;

namespace Fiolyn.Data.Repositories ;

/// <summary>
///   Generic repository implementation for data access operations.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public class Repository<T>( AppDbContext dbContext ) : IRepository<T> where T : class
{
  /// <summary>
  ///   Gets a queryable collection of all entities (including soft-deleted).
  /// </summary>
  public IQueryable<T> Query() => dbContext.Set<T>() ;

  /// <summary>
  ///   Gets a queryable collection of non-deleted entities.
  ///   For entities that don't implement ISoftDelete, returns all entities.
  /// </summary>
  public IQueryable<T> QueryActive()
  {
    var query = dbContext.Set<T>().AsQueryable() ;

    if ( typeof( ISoftDelete ).IsAssignableFrom( typeof( T ) ) )
    {
      query = query.Where( e => ! ((ISoftDelete)e).IsDeleted ) ;
    }

    return query ;
  }

  /// <summary>
  ///   Gets an entity by its primary key.
  /// </summary>
  public async Task<T?> GetByIdAsync( object id ) => await dbContext.Set<T>().FindAsync( id ) ;

  /// <summary>
  ///   Gets a non-deleted entity by its primary key.
  ///   Returns null if the entity is not found or has been soft-deleted.
  /// </summary>
  public async Task<T?> GetActiveByIdAsync( object id )
  {
    var entity = await GetByIdAsync( id ) ;
    return entity is ISoftDelete { IsDeleted: true } ? null : entity ;
  }

  /// <summary>
  ///   Gets a non-deleted entity by its primary key, or throws
  ///   <see cref="KeyNotFoundException" /> if not found or soft-deleted.
  /// </summary>
  public async Task<T> GetActiveByIdOrThrowAsync( object id )
    => await GetActiveByIdAsync( id )
       ?? throw new KeyNotFoundException( $"{typeof( T ).Name} {id} not found" ) ;

  /// <summary>
  ///   Adds a new entity to the database.
  /// </summary>
  public async Task AddAsync( T entity ) => await dbContext.Set<T>().AddAsync( entity ) ;

  /// <summary>
  ///   Adds multiple entities to the database.
  /// </summary>
  public async Task InsertRangeAsync( IEnumerable<T> entities ) => await dbContext.Set<T>().AddRangeAsync( entities ) ;

  /// <summary>
  ///   Attaches an entity to the context for tracking without modifying the database.
  ///   Useful when an entity comes from an external source or detached state.
  /// </summary>
  public void Attach( T entity ) => dbContext.Set<T>().Attach( entity ) ;

  /// <summary>
  ///   Updates an existing entity.
  /// </summary>
  public void Update( T entity ) => dbContext.Set<T>().Update( entity ) ;

  /// <summary>
  ///   Updates multiple entities.
  /// </summary>
  public void UpdateRange( IEnumerable<T> entities ) => dbContext.Set<T>().UpdateRange( entities ) ;

  /// <summary>
  ///   Performs a hard delete (removes entity from database).
  /// </summary>
  public void Delete( T entity ) => dbContext.Set<T>().Remove( entity ) ;

  /// <summary>
  ///   Performs a soft delete (marks entity as deleted without removing from database).
  ///   Only works if the entity implements ISoftDelete.
  /// </summary>
  public void SoftDelete( T entity )
  {
    if ( entity is ISoftDelete softDeleteEntity )
    {
      softDeleteEntity.IsDeleted = true ;
      dbContext.Set<T>().Update( entity ) ;
    }
  }

  /// <summary>
  ///   Restores a soft-deleted entity.
  ///   Only works if the entity implements ISoftDelete.
  /// </summary>
  public void Restore( T entity )
  {
    if ( entity is ISoftDelete softDeleteEntity )
    {
      softDeleteEntity.IsDeleted = false ;
      dbContext.Set<T>().Update( entity ) ;
    }
  }

  /// <summary>
  ///   Saves all pending changes to the database.
  /// </summary>
  public async Task SaveAsync() => await dbContext.SaveChangesAsync() ;
}