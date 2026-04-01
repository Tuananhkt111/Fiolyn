namespace Fiolyn.Entities ;

public class AllowedUser
{
  public int Id { get ; set ; }
  public long TelegramId { get ; set ; }
  public string? Label { get ; set ; }
  public DateTime CreatedAt { get ; set ; } = DateTime.UtcNow ;
}