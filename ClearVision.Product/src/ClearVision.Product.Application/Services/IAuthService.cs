using ClearVision.Product.Application.DTOs;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// Authentication service contract.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password);

    Task LogoutAsync(string token);

    Task<bool> ValidateTokenAsync(string token);

    Task<UserSession?> GetSessionAsync(string token);

    Task<AuthResult> ChangePasswordAsync(string userId, string oldPassword, string newPassword);

    Task<InitialAdminSetupStatusResponse> GetInitialAdminSetupStatusAsync();

    Task<AuthResult> SetupInitialAdminAsync(InitialAdminSetupRequest request);
}

/// <summary>
/// Authentication result.
/// </summary>
public class AuthResult
{
    public bool Success { get; set; }

    public string? Token { get; set; }

    public UserDto? User { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorCode { get; set; }

    public static AuthResult Ok(string token, UserDto user) => new()
    {
        Success = true,
        Token = token,
        User = user
    };

    public static AuthResult Fail(string error, string? code = null) => new()
    {
        Success = false,
        ErrorMessage = error,
        ErrorCode = code
    };
}

/// <summary>
/// User session information.
/// </summary>
public class UserSession
{
    public string UserId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Absolute expiry instant of the session, or <see langword="null"/> when the session never
    /// expires by elapsed time. Desktop logins are non-expiring (<see langword="null"/>): the
    /// session stays valid for the lifetime of the ClearVision process until the user logs out,
    /// the server session is cleared, or a security-relevant change (password/user status) occurs.
    /// A non-null value is still honoured for any caller that opts into a time-bounded session.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => IsExpiredAt(DateTime.UtcNow);

    public bool IsExpiredAt(DateTime utcNow) => ExpiresAt.HasValue && utcNow > ExpiresAt.Value;
}
