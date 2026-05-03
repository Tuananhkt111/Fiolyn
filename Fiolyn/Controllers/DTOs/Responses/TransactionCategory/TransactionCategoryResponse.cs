using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.TransactionCategory ;

/// <summary>
///   Response body for a <see cref="Entities.TransactionCategory" />.
/// </summary>
public record TransactionCategoryResponse(
  long Id,
  string Name,
  TransactionKind Kind,
  DateTime Created,
  DateTime LastModified )
{
  public TransactionCategoryResponse( Entities.TransactionCategory c )
    : this( c.Id, c.Name, c.Kind, c.Created, c.LastModified )
  {
  }
}
