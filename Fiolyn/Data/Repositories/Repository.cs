using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data.Repositories ;

/// <summary>
///   Generic repository implementation for data access operations.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public class Repository<T>( AppDbContext dbContext ) : IRepository<T> where T : class
{
  /// <inheritdoc />
  public IQueryable<T> Query() => dbContext.Set<T>() ;

  /// <inheritdoc />
  public async Task<T?> GetByIdAsync( object id ) => await dbContext.Set<T>().FindAsync( id ) ;

  /// <inheritdoc />
  public async Task<T> GetByIdOrThrowAsync( object id )
  {
    var entity = await GetByIdAsync( id ) ;
    return entity is null or ISoftDelete { IsDeleted: true }
      ? throw new KeyNotFoundException( $"{typeof( T ).Name} {id} not found" )
      : entity ;
  }

  /// <inheritdoc />
  public async Task AddAsync( T entity ) => await dbContext.Set<T>().AddAsync( entity ) ;

  /// <inheritdoc />
  public async Task InsertRangeAsync( IEnumerable<T> entities ) => await dbContext.Set<T>().AddRangeAsync( entities ) ;

  /// <inheritdoc />
  public void Attach( T entity ) => dbContext.Set<T>().Attach( entity ) ;

  /// <inheritdoc />
  public void Update( T entity ) => dbContext.Set<T>().Update( entity ) ;

  /// <inheritdoc />
  public void UpdateRange( IEnumerable<T> entities ) => dbContext.Set<T>().UpdateRange( entities ) ;

  /// <inheritdoc />
  public void Delete( T entity ) => dbContext.Set<T>().Remove( entity ) ;

  /// <inheritdoc />
  public void SoftDelete( T entity )
  {
    if ( entity is ISoftDelete softDeleteEntity )
    {
      softDeleteEntity.IsDeleted = true ;
      MarkIsDeletedModified( entity ) ;
    }
  }

  /// <inheritdoc />
  public void Restore( T entity )
  {
    if ( entity is ISoftDelete softDeleteEntity )
    {
      softDeleteEntity.IsDeleted = false ;
      MarkIsDeletedModified( entity ) ;
    }
  }

  private void MarkIsDeletedModified( T entity )
  {
    var entry = dbContext.Entry( entity ) ;
    if ( entry.State == EntityState.Detached )
    {
      dbContext.Set<T>().Attach( entity ) ;
      entry = dbContext.Entry( entity ) ;
    }
    entry.Property( nameof( ISoftDelete.IsDeleted ) ).IsModified = true ;
  }

  /// <inheritdoc />
  public async Task SaveAsync() => await dbContext.SaveChangesAsync() ;
}
