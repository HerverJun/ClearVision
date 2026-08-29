namespace ClearVision.Product.Application.DTOs;

/// <summary>
/// Stable authenticated context returned by <c>/api/auth/me</c>.
/// </summary>
public sealed class AuthenticatedContextResponse
{
    public string UserId { get; init; } = string.Empty;

    public string Username { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public AuthenticatedPasswordPolicyResponse PasswordPolicy { get; init; } = new();
}

/// <summary>
/// Effective password policy shared by create, reset, and change-password workflows.
/// </summary>
public sealed class AuthenticatedPasswordPolicyResponse
{
    public int MinimumLength { get; init; }
}
