using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public class AuthServiceTests
{
    public AuthServiceTests()
    {
        AuthService.ResetInMemoryStateForTests();
    }

    [Fact]
    public async Task Session_ShouldRemainValid_RegardlessOfElapsedTime()
    {
        // Desktop sessions never expire by elapsed time. Advancing the clock far past the legacy
        // 30-minute timeout (and beyond a full day) must not invalidate an otherwise good session;
        // it stays valid for the lifetime of the process until logout / security change.
        var now = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 1, loginFailureLockoutCount: 3);
        var user = CreateUser("tester", "hash");

        repository.GetByUsernameAsync("tester", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService)
        {
            UtcNowProvider = () => now
        };

        var login = await service.LoginAsync("tester", "correct-password");

        login.Success.Should().BeTrue();
        login.Token.Should().NotBeNullOrWhiteSpace();
        (await service.ValidateTokenAsync(login.Token!)).Should().BeTrue();

        // Two hours later: still valid.
        service.UtcNowProvider = () => now.AddHours(2);
        (await service.ValidateTokenAsync(login.Token!)).Should().BeTrue();

        // A full day later: still valid, and the session is still resolvable.
        service.UtcNowProvider = () => now.AddHours(24);
        (await service.ValidateTokenAsync(login.Token!)).Should().BeTrue();

        var session = await service.GetSessionAsync(login.Token!);
        session.Should().NotBeNull();
        session!.ExpiresAt.Should().BeNull("desktop sessions do not carry a time-based expiry");
    }

    [Fact]
    public async Task LoginAsync_ShouldTemporarilyLockUserAfterConfiguredFailureThreshold()
    {
        var now = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("locked-user", "hash");

        repository.GetByUsernameAsync("locked-user", Arg.Any<CancellationToken>()).Returns(user);
        passwordHasher.VerifyPassword("bad-password", user.PasswordHash).Returns(false);
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService)
        {
            UtcNowProvider = () => now
        };

        var firstFailure = await service.LoginAsync("locked-user", "bad-password");
        var secondFailure = await service.LoginAsync("locked-user", "bad-password");
        var lockedAttempt = await service.LoginAsync("locked-user", "correct-password");

        firstFailure.Success.Should().BeFalse();
        firstFailure.ErrorMessage.Should().Be("用户名或密码错误");
        secondFailure.Success.Should().BeFalse();
        secondFailure.ErrorMessage.Should().Contain("临时锁定");
        lockedAttempt.Success.Should().BeFalse();
        lockedAttempt.ErrorMessage.Should().Contain("临时锁定");
        await repository.DidNotReceive().UpdateAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task LoginAsync_ShouldClearFailureCountAfterSuccessfulLogin()
    {
        var now = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("recover-user", "hash");

        repository.GetByUsernameAsync("recover-user", Arg.Any<CancellationToken>()).Returns(user);
        passwordHasher.VerifyPassword("bad-password", user.PasswordHash).Returns(false);
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService)
        {
            UtcNowProvider = () => now
        };

        var firstFailure = await service.LoginAsync("recover-user", "bad-password");
        var success = await service.LoginAsync("recover-user", "correct-password");
        var failureAfterSuccess = await service.LoginAsync("recover-user", "bad-password");
        var secondSuccess = await service.LoginAsync("recover-user", "correct-password");

        firstFailure.Success.Should().BeFalse();
        success.Success.Should().BeTrue();
        failureAfterSuccess.Success.Should().BeFalse();
        failureAfterSuccess.ErrorMessage.Should().Be("用户名或密码错误");
        secondSuccess.Success.Should().BeTrue();
        await repository.Received(2).UpdateAsync(user);
    }

    [Fact]
    public async Task LoginAsync_ShouldIssueHighEntropyBase64UrlToken()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("token-user", "hash");

        repository.GetByUsernameAsync("token-user", Arg.Any<CancellationToken>()).Returns(user);
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 20; i++)
        {
            var result = await service.LoginAsync("token-user", "correct-password");

            result.Success.Should().BeTrue();
            result.Token.Should().NotBeNullOrWhiteSpace();
            var token = result.Token!;
            token.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
            token.Should().NotMatchRegex("^[0-9a-f]{32}$");
            tokens.Add(token).Should().BeTrue();
        }
    }

    [Fact]
    public async Task LogoutAsync_ShouldInvalidateExistingSession()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("logout-user", "hash");

        repository.GetByUsernameAsync("logout-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("logout-user", "correct-password");

        (await service.ValidateTokenAsync(login.Token!)).Should().BeTrue();

        await service.LogoutAsync(login.Token!);

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        (await service.GetSessionAsync(login.Token!)).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionLookup_WhenRevokedDuringRepositoryAwait_ShouldRejectStaleRefresh(
        bool validateTokenSurface)
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(
            sessionTimeoutMinutes: 30,
            loginFailureLockoutCount: 2);
        var user = CreateUser("revoke-race-user", "hash");
        var repositoryEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRepository = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        repository.GetByUsernameAsync("revoke-race-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(async _ =>
        {
            repositoryEntered.TrySetResult(true);
            await releaseRepository.Task;
            return user;
        });
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("revoke-race-user", "correct-password");
        login.Success.Should().BeTrue();

        var lookup = validateTokenSurface
            ? service.ValidateTokenAsync(login.Token!)
            : ResolveSessionPresenceAsync(service, login.Token!);

        try
        {
            await repositoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            service.RevokeSessionsForUser(user.Id.ToString(), "deterministic-race-test");
            AuthService.GetInMemorySessionCountForTests().Should().Be(0);
        }
        finally
        {
            releaseRepository.TrySetResult(true);
        }

        (await lookup.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeFalse();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentSessionLookups_ShouldKeepSameIssuedSessionValid()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(
            sessionTimeoutMinutes: 30,
            loginFailureLockoutCount: 2);
        var user = CreateUser("concurrent-session-user", "hash");
        var bothLookupsEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLookups = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var lookupCount = 0;

        repository.GetByUsernameAsync("concurrent-session-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(async _ =>
        {
            if (Interlocked.Increment(ref lookupCount) == 2)
            {
                bothLookupsEntered.TrySetResult(true);
            }

            await releaseLookups.Task;
            return user;
        });
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("concurrent-session-user", "correct-password");
        login.Success.Should().BeTrue();

        var first = service.GetSessionAsync(login.Token!);
        var second = service.GetSessionAsync(login.Token!);
        try
        {
            await bothLookupsEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            releaseLookups.TrySetResult(true);
        }

        var sessions = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        sessions.Should().OnlyContain(session => session != null);
        (await service.ValidateTokenAsync(login.Token!)).Should().BeTrue();
    }

    [Fact]
    public async Task ExistingSession_ShouldBeRevokedWhenRoleDowngrades()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("downgrade-user", "hash", UserRole.Admin);

        repository.GetByUsernameAsync("downgrade-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("downgrade-user", "correct-password");
        login.Success.Should().BeTrue();

        user.ChangeRole(UserRole.Operator);

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        (await service.GetSessionAsync(login.Token!)).Should().BeNull();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task ExistingSession_ShouldBeInvalid_WhenUserIsDisabled()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("disabled-user", "hash");

        repository.GetByUsernameAsync("disabled-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("disabled-user", "correct-password");
        login.Success.Should().BeTrue();

        user.Deactivate();

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        (await service.GetSessionAsync(login.Token!)).Should().BeNull();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task ExistingSession_ShouldBeInvalid_WhenUserIsDeleted()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("deleted-user", "hash");
        var deleted = false;

        repository.GetByUsernameAsync("deleted-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(deleted ? null : user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("deleted-user", "correct-password");
        login.Success.Should().BeTrue();

        deleted = true;

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        (await service.GetSessionAsync(login.Token!)).Should().BeNull();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task ExistingSession_ShouldBeInvalid_WhenPasswordHashChanges()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2);
        var user = CreateUser("reset-user", "old-hash");

        repository.GetByUsernameAsync("reset-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("reset-user", "correct-password");
        login.Success.Should().BeTrue();

        user.ChangePassword("new-hash");

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        (await service.GetSessionAsync(login.Token!)).Should().BeNull();
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldRevokeExistingSessionsForUser()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var user = CreateUser("change-revoke-user", "old-hash");

        repository.GetByUsernameAsync("change-revoke-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("CurrentPwd1", user.PasswordHash).Returns(true);
        passwordHasher.HashPassword("NewPwd123").Returns("new-hash");

        var service = new AuthService(repository, passwordHasher, configurationService);
        passwordHasher.VerifyPassword("CurrentPwd1", user.PasswordHash).Returns(true);
        var login = await service.LoginAsync("change-revoke-user", "CurrentPwd1");
        login.Success.Should().BeTrue();
        AuthService.GetInMemorySessionCountForTests().Should().Be(1);

        var result = await service.ChangePasswordAsync(user.Id.ToString(), "CurrentPwd1", "NewPwd123");

        result.Success.Should().BeTrue();
        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task LoginAsync_ShouldNotRemovePriorSessionsByElapsedTime()
    {
        // With time-based expiry removed, an earlier session must survive a later login even when
        // the clock has advanced well past the legacy timeout. Sessions end only on explicit
        // logout or a security-relevant change, never by elapsed time.
        var now = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 1, loginFailureLockoutCount: 2);
        var user = CreateUser("cleanup-user", "hash");

        repository.GetByUsernameAsync("cleanup-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService)
        {
            UtcNowProvider = () => now
        };

        var firstLogin = await service.LoginAsync("cleanup-user", "correct-password");
        firstLogin.Success.Should().BeTrue();
        AuthService.GetInMemorySessionCountForTests().Should().Be(1);

        service.UtcNowProvider = () => now.AddMinutes(2);
        var secondLogin = await service.LoginAsync("cleanup-user", "correct-password");

        secondLogin.Success.Should().BeTrue();
        // The first session is NOT purged by elapsed time — both remain valid.
        AuthService.GetInMemorySessionCountForTests().Should().Be(2);
        (await service.ValidateTokenAsync(firstLogin.Token!)).Should().BeTrue();
        (await service.ValidateTokenAsync(secondLogin.Token!)).Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_ShouldOpportunisticallyRemoveOnlyExpiredLegacySessions()
    {
        var now = new DateTime(2026, 8, 31, 4, 0, 0, DateTimeKind.Utc);
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 1, loginFailureLockoutCount: 3);
        var user = CreateUser("legacy-cleanup-user", "hash");
        repository.GetByUsernameAsync("legacy-cleanup-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService)
        {
            UtcNowProvider = () => now
        };
        const string expiredToken = "legacy-expired-token";
        const string activeToken = "legacy-active-token";

        AuthService.AddLegacyExpiringSessionForTests(
            expiredToken,
            CreateLegacySession(user, now.AddMinutes(-1)),
            user.PasswordHash,
            now.AddHours(-1));
        AuthService.AddLegacyExpiringSessionForTests(
            activeToken,
            CreateLegacySession(user, now.AddMinutes(5)),
            user.PasswordHash,
            now.AddMinutes(-1));
        AuthService.GetInMemorySessionCountForTests().Should().Be(2);

        var login = await service.LoginAsync("legacy-cleanup-user", "correct-password");

        login.Success.Should().BeTrue();
        AuthService.GetInMemorySessionCountForTests().Should().Be(2,
            "the expired legacy record is removed while the active legacy record and new desktop session remain");
        (await service.ValidateTokenAsync(expiredToken)).Should().BeFalse();
        (await service.ValidateTokenAsync(activeToken)).Should().BeTrue();
        var desktopSession = await service.GetSessionAsync(login.Token!);
        desktopSession.Should().NotBeNull();
        desktopSession!.ExpiresAt.Should().BeNull("new desktop sessions remain process-lifetime sessions");
    }

    [Fact]
    public async Task AuthenticationLifecycle_ShouldNeverWriteRawSessionTokensToLogs()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 3);
        var logger = new InMemoryLogger<AuthService>();
        var user = CreateUser("token-log-user", "hash");
        repository.GetByUsernameAsync("token-log-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService, logger);

        var logoutLogin = await service.LoginAsync("token-log-user", "correct-password");
        logoutLogin.Success.Should().BeTrue();
        logoutLogin.Token.Should().NotBeNullOrWhiteSpace();
        (await service.ValidateTokenAsync(logoutLogin.Token!)).Should().BeTrue();
        await service.LogoutAsync(logoutLogin.Token!);
        var revokedLogin = await service.LoginAsync("token-log-user", "correct-password");
        revokedLogin.Success.Should().BeTrue();
        revokedLogin.Token.Should().NotBeNullOrWhiteSpace();
        service.RevokeSessionsForUser(user.Id.ToString(), "security-event");
        (await service.ValidateTokenAsync(revokedLogin.Token!)).Should().BeFalse();

        logger.Messages.Should().NotBeEmpty();
        logger.Messages.Should().NotContain(message =>
            message.Contains(logoutLogin.Token!, StringComparison.Ordinal) ||
            message.Contains(revokedLogin.Token!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoginAsync_ShouldEvictOldestTokenAtPerUserCapacity()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 3);
        var user = CreateUser("capacity-user", "hash");
        repository.GetByUsernameAsync("capacity-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", user.PasswordHash).Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService);
        var tokens = new List<string>();

        for (var index = 0; index < AuthService.PerUserSessionCapacity + 2; index++)
        {
            var login = await service.LoginAsync("capacity-user", "correct-password");
            login.Success.Should().BeTrue();
            tokens.Add(login.Token!);
        }

        AuthService.GetInMemorySessionCountForTests().Should().Be(AuthService.PerUserSessionCapacity);
        (await service.ValidateTokenAsync(tokens[0])).Should().BeFalse();
        (await service.ValidateTokenAsync(tokens[1])).Should().BeFalse();
        foreach (var token in tokens.Skip(2))
        {
            (await service.ValidateTokenAsync(token)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task LoginAsync_ShouldEvictOldestTokenAtGlobalCapacity()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 3);
        var users = Enumerable.Range(0, AuthService.GlobalSessionCapacity + 1)
            .Select(index => CreateUser($"global-{index:D3}", "hash"))
            .ToDictionary(user => user.Username, StringComparer.Ordinal);
        var usersById = users.Values.ToDictionary(user => user.Id);
        repository.GetByUsernameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<User?>(users.GetValueOrDefault(call.ArgAt<string>(0))));
        repository.GetByIdAsync(Arg.Any<Guid>())
            .Returns(call => Task.FromResult<User?>(usersById.GetValueOrDefault(call.ArgAt<Guid>(0))));
        passwordHasher.VerifyPassword("correct-password", "hash").Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService);
        var tokens = new List<string>();

        foreach (var user in users.Values.OrderBy(item => item.Username, StringComparer.Ordinal))
        {
            var login = await service.LoginAsync(user.Username, "correct-password");
            login.Success.Should().BeTrue();
            tokens.Add(login.Token!);
        }

        AuthService.GetInMemorySessionCountForTests().Should().Be(AuthService.GlobalSessionCapacity);
        (await service.ValidateTokenAsync(tokens[0])).Should().BeFalse();
        (await service.ValidateTokenAsync(tokens[^1])).Should().BeTrue();
    }

    [Fact]
    public async Task ExistingSession_ShouldBeRevokedWhenRoleChanges()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 3);
        var user = CreateUser("role-user", "hash", UserRole.Operator);
        repository.GetByUsernameAsync("role-user", Arg.Any<CancellationToken>()).Returns(user);
        repository.GetByIdAsync(user.Id).Returns(_ => Task.FromResult<User?>(user));
        passwordHasher.VerifyPassword("correct-password", "hash").Returns(true);
        var service = new AuthService(repository, passwordHasher, configurationService);
        var login = await service.LoginAsync("role-user", "correct-password");

        user.ChangeRole(UserRole.Engineer);

        (await service.ValidateTokenAsync(login.Token!)).Should().BeFalse();
        AuthService.GetInMemorySessionCountForTests().Should().Be(0);
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldRejectPasswordShorterThanConfiguredLength()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var user = CreateUser("short-password-user", "hash");

        repository.GetByIdAsync(user.Id).Returns(user);

        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.ChangePasswordAsync(user.Id.ToString(), "CurrentPwd1", "short1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("新密码长度不能少于 8 位");
        await repository.DidNotReceive().UpdateAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldRejectPasswordReuse()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var user = CreateUser("reuse-user", "hash");

        repository.GetByIdAsync(user.Id).Returns(user);

        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.ChangePasswordAsync(user.Id.ToString(), "CurrentPwd1", "CurrentPwd1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("新密码不能与当前密码相同");
        await repository.DidNotReceive().UpdateAsync(Arg.Any<User>());
    }

    [Fact]
    public async Task ChangePasswordAsync_ShouldUpdatePasswordWhenPolicySatisfied()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var user = CreateUser("change-success-user", "old-hash");

        repository.GetByIdAsync(user.Id).Returns(user);
        passwordHasher.VerifyPassword("CurrentPwd1", user.PasswordHash).Returns(true);
        passwordHasher.HashPassword("plaintext").Returns("new-hash");

        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.ChangePasswordAsync(user.Id.ToString(), "CurrentPwd1", "plaintext");

        result.Success.Should().BeTrue();
        await repository.Received(1).UpdateAsync(user);
        passwordHasher.Received(1).HashPassword("plaintext");
    }

    [Fact]
    public async Task GetInitialAdminSetupStatusAsync_ShouldRequireSetup_WhenSystemHasNoUsers()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 10);
        repository.IsInstallationCompletedAsync(Arg.Any<CancellationToken>()).Returns(false);

        var service = new AuthService(repository, passwordHasher, configurationService);

        var status = await service.GetInitialAdminSetupStatusAsync();

        status.RequiresInitialAdminSetup.Should().BeTrue();
        status.UsernameMinLength.Should().Be(3);
        status.PasswordMinLength.Should().Be(10);
        status.RequiresUppercase.Should().BeFalse();
        status.RequiresLowercase.Should().BeFalse();
        status.RequiresDigit.Should().BeFalse();
    }

    [Fact]
    public async Task SetupInitialAdminAsync_ShouldCreateAdminAndReturnToken_WhenSystemHasNoUsers()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        passwordHasher.HashPassword("password1").Returns("hashed-admin");
        passwordHasher.VerifyPassword("password1", "hashed-admin").Returns(true);
        User? createdAdmin = null;
        repository.TryCreateInitialAdminAsync(Arg.Any<User>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                createdAdmin = call.ArgAt<User>(0);
                return UserAuthorityMutationResult.Succeeded(createdAdmin);
            });
        repository.GetByUsernameAsync("factory-admin", Arg.Any<CancellationToken>())
            .Returns(_ => createdAdmin);

        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "factory-admin",
            Password = "password1",
            ConfirmPassword = "password1"
        });

        result.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.User.Should().NotBeNull();
        result.User!.Role.Should().Be(UserRole.Admin);
        result.User.Username.Should().Be("factory-admin");
        await repository.Received(1).TryCreateInitialAdminAsync(Arg.Is<User>(user =>
            user.Username == "factory-admin" &&
            user.DisplayName == "factory-admin" &&
            user.Role == UserRole.Admin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupInitialAdminAsync_ShouldReject_WhenAnyUserAlreadyExists()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        repository.IsInstallationCompletedAsync(Arg.Any<CancellationToken>()).Returns(true);

        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "factory-admin",
            Password = "password1",
            ConfirmPassword = "password1"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(AuthService.InitialAdminSetupAlreadyCompletedMessage);
        result.ErrorCode.Should().Be(AuthService.InstallationAlreadyCompletedCode);
        await repository.DidNotReceive().TryCreateInitialAdminAsync(
            Arg.Any<User>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupInitialAdminAsync_ShouldReject_WhenPasswordIsShorterThanMinimumLength()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "factory-admin",
            Password = "short1",
            ConfirmPassword = "short1"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("新密码长度不能少于 8 位");
        result.ErrorCode.Should().Be(AuthService.SetupValidationErrorCode);
        await repository.DidNotReceive().TryCreateInitialAdminAsync(
            Arg.Any<User>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetupInitialAdminAsync_ShouldReject_WhenConfirmPasswordDoesNotMatch()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configurationService = CreateConfigurationService(sessionTimeoutMinutes: 30, loginFailureLockoutCount: 2, passwordMinLength: 8);
        var service = new AuthService(repository, passwordHasher, configurationService);

        var result = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "factory-admin",
            Password = "password1",
            ConfirmPassword = "password2"
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("两次输入的密码不一致");
        result.ErrorCode.Should().Be(AuthService.SetupValidationErrorCode);
        await repository.DidNotReceive().TryCreateInitialAdminAsync(
            Arg.Any<User>(),
            Arg.Any<CancellationToken>());
    }

    private static IConfigurationService CreateConfigurationService(int sessionTimeoutMinutes, int loginFailureLockoutCount, int passwordMinLength = 6)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            Security = new SecurityConfig
            {
                PasswordMinLength = passwordMinLength,
                SessionTimeoutMinutes = sessionTimeoutMinutes,
                LoginFailureLockoutCount = loginFailureLockoutCount
            }
        });
        return configurationService;
    }

    private static User CreateUser(string username, string passwordHash, UserRole role = UserRole.Engineer)
    {
        return User.Create(username, passwordHash, username, role);
    }

    private static async Task<bool> ResolveSessionPresenceAsync(AuthService service, string token)
    {
        return await service.GetSessionAsync(token) != null;
    }

    private static UserSession CreateLegacySession(User user, DateTime expiresAt) => new()
    {
        UserId = user.Id.ToString(),
        Username = user.Username,
        Role = user.Role.ToString(),
        ExpiresAt = expiresAt
    };

    private sealed class InMemoryLogger<T> : ILogger<T>
    {
        private readonly object _sync = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_sync)
                {
                    return _messages.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_sync)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }
}
