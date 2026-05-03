using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Responses.Transaction ;

/// <summary>
///   Compact category projection for transaction responses.
/// </summary>
public record TransactionCategoryRefResponse( long Id, string Name, TransactionKind Kind )
{
  public TransactionCategoryRefResponse( Entities.TransactionCategory c )
    : this( c.Id, c.Name, c.Kind )
  {
  }
}
