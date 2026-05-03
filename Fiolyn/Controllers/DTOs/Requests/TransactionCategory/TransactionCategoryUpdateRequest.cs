using System.ComponentModel.DataAnnotations ;

namespace Fiolyn.Controllers.DTOs.Requests.TransactionCategory ;

/// <summary>
///   Admin request to rename a <see cref="Entities.TransactionCategory" />. <c>Kind</c> is not
///   accepted here — it is fixed at creation time.
/// </summary>
public record TransactionCategoryUpdateRequest(
  [Required] [MaxLength( 50 )] string Name ) ;
