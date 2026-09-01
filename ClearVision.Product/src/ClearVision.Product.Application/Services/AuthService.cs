using System.Security.Cryptography;
using System.Text;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Application.Services;

/// <summary>
/// Auth service backed by in-memory sessions.
/// </summary>
public class AuthService : IAuthService, IAuthSessionRevocationService
{
    private const int UsernameMinLength = 3;
    private const int SessionTokenByteLength = 32;
    public const int PerUserSessionCapacity = 8;
    public const int GlobalSessionCapacity = 256;
    public const string InitialAdminSetupAlreadyCompletedMessage = "系统已完成初始化，请直接登录";
    public const string InstallationAlreadyCompletedCode = "INSTALLATION_ALREADY_COMPLETED";
    public const string SetupValidationErrorCode = "SETUP_VALIDATION_ERROR";
    private const string InitialAdminPasswordMismatchMessage = "两次输入的密码不一致";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<AuthService> _logger;

    private static readonly Dictionary<string, SessionRecord> _sessions = new();
    private static readonly Dictionary<string, LoginFailureState> _loginFailures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();
    private static readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);
    private static long _sessionSequence;

    public Func<DateTime> UtcNowProvider { get; set; } = static () => DateTime.UtcNow;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfigurationService configurationService,
        ILogger<AuthService> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _configurationService = configurationService;
        _logger = logger;
    }

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IConfigurationService configurationService)
        : this(userRepository, passwordHasher, configurationService, NullLogger<AuthService>.Instance)
    {
    }

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return AuthResult.Fail("用户名和密码不能为空");
        }

        var normalizedUsername = username.Trim();
        var utcNow = UtcNowProvider();

        var user = await _userRepository.GetByUsernameAsync(normalizedUsername);
        if (user == null)
        {
            return AuthResult.Fail("用户名或密码错误");
        }

        if (!user.IsActive)
        {
            return AuthResult.Fail("账户已被禁用");
        }

        if (TryGetActiveLockout(normalizedUsername, utcNow, out var lockedUntilUtc))
        {
            _logger.LogWarning("[AuthService] Login rejected because account is locked: {Username}", normalizedUsername);
            return AuthResult.Fail(BuildLockoutMessage(lockedUntilUtc));
        }

        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
        {
            var threshold = ResolveLoginFailureLockoutCount();
            var failureState = RegisterFailedAttempt(normalizedUsername, utcNow, threshold);
            if (failureState.IsLockedAt(utcNow))
            {
                RevokeSessionsForUser(user.Id.ToString(), "account-lockout");
                _logger.LogWarning("[AuthService] Login locked after failures: {Username}", normalizedUsername);
                return AuthResult.Fail(BuildLockoutMessage(failureState.LockedUntilUtc));
            }

            _logger.LogInformation("[AuthService] Invalid login credentials: {Username}", normalizedUsername);
            return AuthResult.Fail("用户名或密码错误");
        }

        ClearFailedAttempts(normalizedUsername);

        user.UpdateLastLogin();
        await _userRepository.UpdateAsync(user);

        var token = GenerateToken();
        var session = new UserSession
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
            Role = user.Role.ToString(),
            // Desktop sessions never expire by elapsed time. They remain valid for the lifetime of
            // the process and are invalidated only by explicit logout, server-side session clearing,
            // or a security-relevant change (password change / user disabled), each enforced in
            // GetSessionAsync. Security.SessionTimeoutMinutes is retained purely for config
            // backward-compatibility and no longer drives session expiry.
            ExpiresAt = null
        };

        var passwordFingerprint = ComputePasswordFingerprint(user.PasswordHash);

        lock (_lock)
        {
            CleanExpiredSessionsLocked(utcNow);
            _sessions[token] = new SessionRecord(
                session,
                passwordFingerprint,
                Interlocked.Increment(ref _sessionSequence),
                utcNow);
            EnforceSessionCapacitiesLocked(session.UserId);
        }

        _logger.LogInformation("[AuthService] Login success: {Username}", normalizedUsername);
        return AuthResult.Ok(token, MapToDto(user));
    }

    public Task LogoutAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            _logger.LogInformation("[AuthService] Logout requested without token.");
            return Task.CompletedTask;
        }

        var removed = false;
        lock (_lock)
        {
            removed = _sessions.Remove(token);
        }

        _logger.LogInformation("[AuthService] Logout complete: {Result}", removed ? "removed" : "not-found");
        return Task.CompletedTask;
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        return await GetSessionAsync(token) != null;
    }

    public async Task<UserSession?> GetSessionAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var utcNow = UtcNowProvider();
        SessionRecord record;

        lock (_lock)
        {
            CleanExpiredSessionsLocked(utcNow);
            if (!_sessions.TryGetValue(token, out record!))
            {
                return null;
            }
        }

        if (!Guid.TryParse(record.Session.UserId, out var userId))
        {
            RemoveSession(token);
            return null;
        }

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            RemoveSession(token);
            return null;
        }

        var currentFingerprint = ComputePasswordFingerprint(user.PasswordHash);
        if (!string.Equals(record.PasswordFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            RemoveSession(token);
            return null;
        }

        if (!string.Equals(record.Session.Role, user.Role.ToString(), StringComparison.Ordinal) ||
            !string.Equals(record.Session.Username, user.Username, StringComparison.Ordinal))
        {
            // Do not silently upgrade or otherwise change the authority of an
            // already-issued token. The user must authenticate again.
            RemoveSession(token);
            return null;
        }

        var refreshed = new UserSession
        {
            UserId = user.Id.ToString(),
            Username = user.Username,
            Role = user.Role.ToString(),
            ExpiresAt = record.Session.ExpiresAt
        };

        lock (_lock)
        {
            if (!_sessions.TryGetValue(token, out var current) ||
                current.CreatedSequence != record.CreatedSequence)
            {
                return null;
            }

            _sessions[token] = current with { Session = refreshed, LastValidatedAtUtc = utcNow };
        }

        return refreshed;
    }

    public async Task<AuthResult> ChangePasswordAsync(string userId, string oldPassword, string newPassword)
    {
        if (!Guid.TryParse(userId, out var id))
        {
            return AuthResult.Fail("无效的用户ID");
        }

        if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            return AuthResult.Fail("密码不能为空");
        }

        var passwordPolicyError = ValidatePasswordPolicy(newPassword, oldPassword);
        if (!string.IsNullOrEmpty(passwordPolicyError))
        {
            return AuthResult.Fail(passwordPolicyError);
        }

        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
        {
            return AuthResult.Fail("用户不存在");
        }

        if (!_passwordHasher.VerifyPassword(oldPassword, user.PasswordHash))
        {
            _logger.LogInformation("[AuthService] Change password rejected due to invalid current password: {UserId}", userId);
            return AuthResult.Fail("当前密码错误");
        }

        var newHash = _passwordHasher.HashPassword(newPassword);
        user.ChangePassword(newHash);
        await _userRepository.UpdateAsync(user);
        RevokeSessionsForUser(user.Id.ToString(), "password-changed");
        _logger.LogInformation("[AuthService] Password changed: {UserId}", userId);

        return AuthResult.Ok(string.Empty, MapToDto(user));
    }

    public async Task<InitialAdminSetupStatusResponse> GetInitialAdminSetupStatusAsync()
    {
        return new InitialAdminSetupStatusResponse
        {
            RequiresInitialAdminSetup = !await _userRepository.IsInstallationCompletedAsync(),
            UsernameMinLength = UsernameMinLength,
            PasswordMinLength = ResolvePasswordMinLength(),
            RequiresUppercase = false,
            RequiresLowercase = false,
            RequiresDigit = false
        };
    }

    public async Task<AuthResult> SetupInitialAdminAsync(InitialAdminSetupRequest request)
    {
        if (request == null)
        {
            return AuthResult.Fail("初始化请求不能为空", SetupValidationErrorCode);
        }

        var usernameError = ValidateUsername(request.Username);
        if (!string.IsNullOrEmpty(usernameError))
        {
            return AuthResult.Fail(usernameError, SetupValidationErrorCode);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return AuthResult.Fail("密码不能为空", SetupValidationErrorCode);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return AuthResult.Fail(InitialAdminPasswordMismatchMessage, SetupValidationErrorCode);
        }

        var passwordPolicyError = ValidatePasswordLength(request.Password);
        if (!string.IsNullOrEmpty(passwordPolicyError))
        {
            return AuthResult.Fail(passwordPolicyError, SetupValidationErrorCode);
        }

        var normalizedUsername = request.Username.Trim();

        // Fast durable-latch check. TryCreateInitialAdminAsync still performs the authoritative
        // transactional compare-and-set, so a concurrent setup race remains protected.
        if (await _userRepository.IsInstallationCompletedAsync())
        {
            return AuthResult.Fail(
                InitialAdminSetupAlreadyCompletedMessage,
                InstallationAlreadyCompletedCode);
        }

        var adminUser = User.Create(
            normalizedUsername,
            _passwordHasher.HashPassword(request.Password),
            normalizedUsername,
            UserRole.Admin);
        var creation = await _userRepository.TryCreateInitialAdminAsync(adminUser);
        if (creation.Status != UserAuthorityMutationStatus.Success)
        {
            return AuthResult.Fail(
                InitialAdminSetupAlreadyCompletedMessage,
                InstallationAlreadyCompletedCode);
        }

        _logger.LogInformation("[AuthService] Initial admin created: {Username}", normalizedUsername);
        return await LoginAsync(normalizedUsername, request.Password);
    }

    public static void ResetInMemoryStateForTests()
    {
        lock (_lock)
        {
            _sessions.Clear();
            _loginFailures.Clear();
            _sessionSequence = 0;
        }
    }

    public static int GetInMemorySessionCountForTests()
    {
        lock (_lock)
        {
            return _sessions.Count;
        }
    }

    internal static void AddLegacyExpiringSessionForTests(
        string token,
        UserSession session,
        string passwordHash,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.ExpiresAt.HasValue)
        {
            throw new ArgumentException("A legacy test session must have an absolute expiry.", nameof(session));
        }

        lock (_lock)
        {
            _sessions[token] = new SessionRecord(
                session,
                ComputePasswordFingerprint(passwordHash),
                Interlocked.Increment(ref _sessionSequence),
                createdAtUtc);
        }
    }

    private static string GenerateToken()
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(SessionTokenByteLength);
        return Convert.ToBase64String(tokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ComputePasswordFingerprint(string passwordHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? string.Empty));
        return Convert.ToHexString(bytes);
    }

    private static void CleanExpiredSessionsLocked(DateTime utcNow)
    {
        foreach (var expiredToken in _sessions
                     .Where(item => item.Value.Session.IsExpiredAt(utcNow))
                     .Select(item => item.Key)
                     .ToList())
        {
            _sessions.Remove(expiredToken);
        }
    }

    private static void RemoveSession(string token)
    {
        lock (_lock)
        {
            _sessions.Remove(token);
        }
    }

    public void RevokeSessionsForUser(string userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var removed = 0;
        lock (_lock)
        {
            foreach (var token in _sessions
                         .Where(item => string.Equals(item.Value.Session.UserId, userId, StringComparison.Ordinal))
                         .Select(item => item.Key)
                         .ToList())
            {
                if (_sessions.Remove(token))
                {
                    removed++;
                }
            }
        }

        _logger.LogInformation(
            "[AuthService] Revoked {SessionCount} session(s) for user {UserId}. Reason={Reason}",
            removed,
            userId,
            string.IsNullOrWhiteSpace(reason) ? "security-event" : reason.Trim());
    }

    private static void EnforceSessionCapacitiesLocked(string userId)
    {
        var perUserOverflow = _sessions.Count(item =>
            string.Equals(item.Value.Session.UserId, userId, StringComparison.Ordinal)) - PerUserSessionCapacity;
        if (perUserOverflow > 0)
        {
            foreach (var token in _sessions
                         .Where(item => string.Equals(item.Value.Session.UserId, userId, StringComparison.Ordinal))
                         .OrderBy(item => item.Value.CreatedSequence)
                         .ThenBy(item => item.Key, StringComparer.Ordinal)
                         .Take(perUserOverflow)
                         .Select(item => item.Key)
                         .ToList())
            {
                _sessions.Remove(token);
            }
        }

        var globalOverflow = _sessions.Count - GlobalSessionCapacity;
        if (globalOverflow <= 0)
        {
            return;
        }

        foreach (var token in _sessions
                     .OrderBy(item => item.Value.CreatedSequence)
                     .ThenBy(item => item.Key, StringComparer.Ordinal)
                     .Take(globalOverflow)
                     .Select(item => item.Key)
                     .ToList())
        {
            _sessions.Remove(token);
        }
    }

    private int ResolvePasswordMinLength()
    {
        return Math.Max(6, _configurationService.GetCurrent()?.Security?.PasswordMinLength ?? 6);
    }

    private int ResolveLoginFailureLockoutCount()
    {
        return Math.Max(1, _configurationService.GetCurrent()?.Security?.LoginFailureLockoutCount ?? 5);
    }

    private string? ValidatePasswordPolicy(string newPassword, string oldPassword)
    {
        var lengthError = ValidatePasswordLength(newPassword);
        if (!string.IsNullOrEmpty(lengthError))
        {
            return lengthError;
        }

        if (string.Equals(newPassword, oldPassword, StringComparison.Ordinal))
        {
            return "新密码不能与当前密码相同";
        }

        return null;
    }

    private string? ValidatePasswordLength(string password)
    {
        var minPasswordLength = ResolvePasswordMinLength();
        if (password.Trim().Length < minPasswordLength)
        {
            return $"新密码长度不能少于 {minPasswordLength} 位";
        }

        return null;
    }

    private static string? ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return "用户名不能为空";
        }

        if (username.Trim().Length < UsernameMinLength)
        {
            return $"用户名长度至少为 {UsernameMinLength} 位";
        }

        return null;
    }

    private static LoginFailureState RegisterFailedAttempt(string username, DateTime utcNow, int threshold)
    {
        lock (_lock)
        {
            if (!_loginFailures.TryGetValue(username, out var state))
            {
                state = new LoginFailureState();
            }

            if (state.LockedUntilUtc > utcNow)
            {
                _loginFailures[username] = state;
                return state;
            }

            if (state.LockedUntilUtc != DateTime.MinValue && state.LockedUntilUtc <= utcNow)
            {
                state = new LoginFailureState();
            }

            state.FailureCount += 1;
            if (state.FailureCount >= threshold)
            {
                state.LockedUntilUtc = utcNow.Add(_lockoutDuration);
                state.FailureCount = 0;
            }

            _loginFailures[username] = state;
            return state;
        }
    }

    private static void ClearFailedAttempts(string username)
    {
        lock (_lock)
        {
            _loginFailures.Remove(username);
        }
    }

    private static bool TryGetActiveLockout(string username, DateTime utcNow, out DateTime lockedUntilUtc)
    {
        lock (_lock)
        {
            if (_loginFailures.TryGetValue(username, out var state))
            {
                if (state.IsLockedAt(utcNow))
                {
                    lockedUntilUtc = state.LockedUntilUtc;
                    return true;
                }

                if (state.LockedUntilUtc != DateTime.MinValue && state.LockedUntilUtc <= utcNow)
                {
                    _loginFailures.Remove(username);
                }
            }
        }

        lockedUntilUtc = DateTime.MinValue;
        return false;
    }

    private static string BuildLockoutMessage(DateTime lockedUntilUtc)
    {
        return $"账号已被临时锁定，请在 {lockedUntilUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} 后重试";
    }

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

    private sealed class LoginFailureState
    {
        public int FailureCount { get; set; }

        public DateTime LockedUntilUtc { get; set; }

        public bool IsLockedAt(DateTime utcNow) => LockedUntilUtc > utcNow;
    }

    private sealed record SessionRecord(
        UserSession Session,
        string PasswordFingerprint,
        long CreatedSequence,
        DateTime LastValidatedAtUtc);
}
