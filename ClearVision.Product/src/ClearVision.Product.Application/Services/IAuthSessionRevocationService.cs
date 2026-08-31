namespace ClearVision.Product.Application.Services;

/// <summary>
/// Revokes process-lifetime desktop sessions after a security-relevant user mutation.
/// Implementations must never log or otherwise expose the session token itself.
/// </summary>
public interface IAuthSessionRevocationService
{
    void RevokeSessionsForUser(string userId, string reason);
}
