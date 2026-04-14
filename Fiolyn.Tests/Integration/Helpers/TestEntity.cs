using System.ComponentModel.DataAnnotations ;
using Fiolyn.Entities.Base ;

namespace Fiolyn.Tests.Integration.Helpers ;

/// <summary>
///   Minimal entity used to test generic repository and audit behavior
///   without coupling tests to any production entity.
/// </summary>
public class TestEntity : IAuditable, ISoftDelete
{
  [Key]
  public long Id { get ; set ; }

  public DateTime Created { get ; set ; }
  public DateTime LastModified { get ; set ; }
  public bool IsDeleted { get ; set ; }
}