namespace Fiolyn.Entities.Base ;

/// <summary>
///   Marks an entity as auditable with creation and modification timestamps.
/// </summary>
public interface IAuditable
{
  /// <summary>
  ///   Gets or sets the timestamp when the entity was created.
  /// </summary>
  DateTime Created { get ; set ; }

  /// <summary>
  ///   Gets or sets the timestamp of the last modification.
  /// </summary>
  DateTime LastModified { get ; set ; }
}
