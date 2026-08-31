// UserManagementService.cs
// 更新用户请求
// 作者：蘅芜君

using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// 用户管理服务 - 仅Admin可调用
/// </summary>
public class UserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfigurationService? _configurationService;
    private readonly IAuthSessionRevocationService? _sessionRevocation;

    public UserManagementService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfigurationService configurationService,
        IAuthSessionRevocationService sessionRevocation)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _configurationService = configurationService;
        _sessionRevocation = sessionRevocation;
    }

    public UserManagementService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfigurationService configurationService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _configurationService = configurationService;
    }

    public UserManagementService(IUserRepository userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    /// <summary>
    /// 获取所有用户（包括已禁用）
    /// </summary>
    public async Task<IEnumerable<UserDto>> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetAllAsync();
        return users.Select(MapToDto);
    }

    /// <summary>
    /// 获取所有启用的用户
    /// </summary>
    public async Task<IEnumerable<UserDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetAllActiveUsersAsync(cancellationToken);
        return users.Select(MapToDto);
    }

    /// <summary>
    /// 根据ID获取用户
    /// </summary>
    public async Task<UserDto?> GetUserByIdAsync(string id)
    {
        if (!Guid.TryParse(id, out var userId))
            return null;

        var user = await _userRepository.GetByIdAsync(userId);
        return user == null ? null : MapToDto(user);
    }

    /// <summary>
    /// 创建用户
    /// </summary>
    public async Task<UserResult> CreateUserAsync(CreateUserRequest request)
    {
        // 验证请求
        if (string.IsNullOrWhiteSpace(request.Username))
            return UserResult.Fail("用户名不能为空", UserManagementErrorCodes.ValidationError);

        if (request.Username.Trim().Length < 3)
            return UserResult.Fail("用户名长度至少为3位", UserManagementErrorCodes.ValidationError);

        if (string.IsNullOrWhiteSpace(request.Password))
            return UserResult.Fail("密码不能为空", UserManagementErrorCodes.ValidationError);

        var passwordMinLength = ResolvePasswordMinLength();
        if (request.Password.Trim().Length < passwordMinLength)
            return UserResult.Fail(
                $"初始密码长度不能少于 {passwordMinLength} 位",
                UserManagementErrorCodes.ValidationError);

        if (!Enum.IsDefined(request.Role))
            return UserResult.Fail("用户角色无效", UserManagementErrorCodes.ValidationError);

        // 检查用户名是否已存在
        if (await _userRepository.IsUsernameExistsAsync(request.Username))
            return UserResult.Fail(
                $"用户名 '{request.Username}' 已存在",
                UserManagementErrorCodes.UsernameConflict);

        // 哈希密码
        var passwordHash = _passwordHasher.HashPassword(request.Password);

        // 创建用户
        var user = User.Create(
            request.Username.Trim(),
            passwordHash,
            request.DisplayName?.Trim() ?? request.Username.Trim(),
            request.Role
        );

        await _userRepository.AddAsync(user);

        return UserResult.Ok(MapToDto(user));
    }

    /// <summary>
    /// 更新用户
    /// </summary>
    public async Task<UserResult> UpdateUserAsync(string id, UpdateUserRequest request)
    {
        if (!Guid.TryParse(id, out var userId))
            return UserResult.Fail("无效的用户ID", UserManagementErrorCodes.ValidationError);

        if (!Enum.IsDefined(request.Role))
            return UserResult.Fail("用户角色无效", UserManagementErrorCodes.ValidationError);

        var mutation = await _userRepository.TryUpdatePreservingActiveAdminAsync(
            userId,
            request.DisplayName,
            request.Role,
            request.IsActive);
        if (mutation.Status == UserAuthorityMutationStatus.Success)
        {
            _sessionRevocation?.RevokeSessionsForUser(userId.ToString(), "user-authority-updated");
        }

        return MapAuthorityMutation(mutation);
    }

    /// <summary>
    /// 删除用户
    /// </summary>
    public async Task<UserResult> DeleteUserAsync(string id)
    {
        if (!Guid.TryParse(id, out var userId))
            return UserResult.Fail("无效的用户ID", UserManagementErrorCodes.ValidationError);

        var mutation = await _userRepository.TryDeletePreservingActiveAdminAsync(userId);
        if (mutation.Status == UserAuthorityMutationStatus.Success)
        {
            _sessionRevocation?.RevokeSessionsForUser(userId.ToString(), "user-deleted");
        }

        return MapAuthorityMutation(mutation);
    }

    /// <summary>
    /// 重置密码
    /// </summary>
    public async Task<UserResult> ResetPasswordAsync(string id, string newPassword)
    {
        if (!Guid.TryParse(id, out var userId))
            return UserResult.Fail("无效的用户ID", UserManagementErrorCodes.ValidationError);

        var passwordMinLength = ResolvePasswordMinLength();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Trim().Length < passwordMinLength)
            return UserResult.Fail(
                $"重置密码长度不能少于 {passwordMinLength} 位",
                UserManagementErrorCodes.ValidationError);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            return UserResult.Fail("用户不存在", UserManagementErrorCodes.UserNotFound);

        var newHash = _passwordHasher.HashPassword(newPassword);
        user.ChangePassword(newHash);
        await _userRepository.UpdateAsync(user);
        _sessionRevocation?.RevokeSessionsForUser(userId.ToString(), "password-reset");

        return UserResult.Ok(MapToDto(user));
    }

    /// <summary>
    /// 检查用户名是否可用
    /// </summary>
    public async Task<bool> IsUsernameAvailableAsync(string username)
    {
        return !await _userRepository.IsUsernameExistsAsync(username);
    }

    /// <summary>
    /// 映射到DTO
    /// </summary>
    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id.ToString(),
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            LastLoginAt = user.LastLoginAt
        };
    }

    private int ResolvePasswordMinLength()
    {
        return Math.Max(6, _configurationService?.GetCurrent()?.Security?.PasswordMinLength ?? 6);
    }

    private static UserResult MapAuthorityMutation(UserAuthorityMutationResult mutation)
    {
        return mutation.Status switch
        {
            UserAuthorityMutationStatus.Success =>
                UserResult.Ok(mutation.User == null ? null : MapToDto(mutation.User)),
            UserAuthorityMutationStatus.NotFound =>
                UserResult.Fail("用户不存在", UserManagementErrorCodes.UserNotFound),
            UserAuthorityMutationStatus.LastActiveAdmin =>
                UserResult.Fail(
                    "必须保留至少一个已启用的管理员账户",
                    UserManagementErrorCodes.LastActiveAdmin),
            _ => UserResult.Fail("用户操作冲突", UserManagementErrorCodes.RevisionConflict)
        };
    }
}

/// <summary>
/// 用户操作结果
/// </summary>
public class UserResult
{
    public bool Success { get; set; }
    public UserDto? User { get; set; }
    public string? ErrorMessage { get; set; }

    public string? ErrorCode { get; set; }

    public static UserResult Ok(UserDto? user = null) => new() { Success = true, User = user };
    public static UserResult Fail(string error, string code) => new()
    {
        Success = false,
        ErrorMessage = error,
        ErrorCode = code
    };
}

public static class UserManagementErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UsernameConflict = "USERNAME_ALREADY_EXISTS";
    public const string LastActiveAdmin = "LAST_ACTIVE_ADMIN";
    public const string RevisionConflict = "REVISION_CONFLICT";
}

/// <summary>
/// 创建用户请求
/// </summary>
public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public UserRole Role { get; set; } = UserRole.Operator;
}

/// <summary>
/// 更新用户请求
/// </summary>
public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
}
