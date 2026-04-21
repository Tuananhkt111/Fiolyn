namespace Fiolyn.Controllers.DTOs.Responses.AllowedEmail ;

/// <summary>
///   Response body for allowed-email endpoints.
/// </summary>
public record AllowedEmailResponse(
  long Id,
  string Email,
  string? Note,
  long? InvitedByUserId,
  DateTime Created )
{
  public AllowedEmailResponse( Entities.AllowedEmail a )
    : this( a.Id, a.Email, a.Note, a.InvitedByUserId, a.Created )
  {
  }
}
