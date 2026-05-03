using System.ComponentModel.DataAnnotations ;
using Fiolyn.Enums ;

namespace Fiolyn.Controllers.DTOs.Requests.TransactionCategory ;

/// <summary>
///   Admin request to create a new <see cref="Entities.TransactionCategory" />. <c>Kind</c> is
///   immutable post-create.
/// </summary>
public record TransactionCategoryCreateRequest(
  [Required] [MaxLength( 50 )] string Name,
  [Required] TransactionKind Kind ) ;
