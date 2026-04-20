namespace Fiolyn.Constants ;

/// <summary>
///   Shared string constants for the auth stack — claim names, claim values, policy names.
///   Centralized to keep <see cref="Services.Internal.JwtService" />,
///   <see cref="ServiceRegisters.AuthServiceRegister" />, and admin controllers in sync.
/// </summary>
public static class AuthConstants
{
  /// <summary>Claim type that carries the "is admin" flag on Fiolyn access tokens.</summary>
  public const string AdminClaimName = "isAdmin" ;

  /// <summary>Claim value indicating the user has admin privileges.</summary>
  public const string AdminClaimTrueValue = "true" ;

  /// <summary>Authorization policy that requires the admin claim.</summary>
  public const string AdminPolicyName = "Admin" ;
}