using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Data;
using ClearVision.Product.Infrastructure.Repositories;
using ClearVision.Product.Infrastructure.Security;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSubstitute;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.Database, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class UserAuthorityRepositoryTests
{
    [Fact]
    public async Task InitialSetup_ShouldPersistIrreversibleLatch_IndependentOfUserCount()
    {
        await using var database = await AuthorityDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new UserRepository(context);
        var service = CreateAuthService(repository);

        (await service.GetInitialAdminSetupStatusAsync())
            .RequiresInitialAdminSetup.Should().BeTrue();

        var setup = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "factory-admin",
            Password = "Password123",
            ConfirmPassword = "Password123"
        });

        setup.Success.Should().BeTrue();
        (await service.GetInitialAdminSetupStatusAsync())
            .RequiresInitialAdminSetup.Should().BeFalse();

        // Simulate out-of-band data loss. Normal repository paths cannot remove the last Admin.
        await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Users\";");

        (await service.GetInitialAdminSetupStatusAsync())
            .RequiresInitialAdminSetup.Should().BeFalse();
        var repeatedSetup = await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
        {
            Username = "replacement-admin",
            Password = "Password123",
            ConfirmPassword = "Password123"
        });
        repeatedSetup.Success.Should().BeFalse();
        repeatedSetup.ErrorCode.Should().Be(AuthService.InstallationAlreadyCompletedCode);

        var reopen = () => context.Database.ExecuteSqlRawAsync(
            "UPDATE \"InstallationStates\" SET \"IsCompleted\" = 0 WHERE \"Id\" = 1;");
        await reopen.Should().ThrowAsync<SqliteException>()
            .WithMessage("*cannot be reopened*");
    }

    [Theory]
    [InlineData(UserRole.Admin, false)]
    [InlineData(UserRole.Engineer, true)]
    public async Task Update_ShouldRejectDisablingOrDowngradingOnlyActiveAdmin(
        UserRole requestedRole,
        bool requestedActive)
    {
        await using var database = await AuthorityDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new UserRepository(context);
        var admin = await CreateInitialAdminAsync(repository, "sole-admin");
        var service = new UserManagementService(repository, new PasswordHasher(), CreateConfigurationService());

        var result = await service.UpdateUserAsync(admin.Id.ToString(), new UpdateUserRequest
        {
            DisplayName = "Changed",
            Role = requestedRole,
            IsActive = requestedActive
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(UserManagementErrorCodes.LastActiveAdmin);
        var persisted = await context.Users.AsNoTracking().SingleAsync(user => user.Id == admin.Id);
        persisted.Role.Should().Be(UserRole.Admin);
        persisted.IsActive.Should().BeTrue();
        persisted.DisplayName.Should().Be("sole-admin");
    }

    [Fact]
    public async Task Delete_ShouldRejectOnlyActiveAdmin_RegardlessOfUsername()
    {
        await using var database = await AuthorityDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var repository = new UserRepository(context);
        var admin = await CreateInitialAdminAsync(repository, "line-supervisor");
        var service = new UserManagementService(repository, new PasswordHasher(), CreateConfigurationService());

        var result = await service.DeleteUserAsync(admin.Id.ToString());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(UserManagementErrorCodes.LastActiveAdmin);
        (await context.Users.CountAsync(user =>
            user.Role == UserRole.Admin && user.IsActive && !user.IsDeleted)).Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentDowngradeOfFinalTwoAdmins_ShouldAllowAtMostOneAndPreserveOneAdmin()
    {
        await using var database = await AuthorityDatabase.CreateAsync();
        Guid firstId;
        Guid secondId;
        await using (var seedContext = database.CreateContext())
        {
            var repository = new UserRepository(seedContext);
            var first = await CreateInitialAdminAsync(repository, "admin-a");
            var second = User.Create("admin-b", "hash-b", "admin-b", UserRole.Admin);
            await repository.AddAsync(second);
            firstId = first.Id;
            secondId = second.Id;
        }

        var rendezvous = new AsyncRendezvous(participants: 2);
        var firstTask = DowngradeAfterRendezvousAsync(firstId);
        var secondTask = DowngradeAfterRendezvousAsync(secondId);

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Count(result => result.Status == UserAuthorityMutationStatus.Success).Should().Be(1);
        results.Count(result => result.Status == UserAuthorityMutationStatus.LastActiveAdmin).Should().Be(1);
        await using var verifyContext = database.CreateContext();
        (await verifyContext.Users.AsNoTracking().CountAsync(user =>
            user.Role == UserRole.Admin && user.IsActive && !user.IsDeleted)).Should().Be(1);

        async Task<UserAuthorityMutationResult> DowngradeAfterRendezvousAsync(Guid userId)
        {
            await using var context = database.CreateContext();
            var repository = new UserRepository(context);
            await rendezvous.ArriveAsync();
            return await repository.TryUpdatePreservingActiveAdminAsync(
                userId,
                displayName: null,
                UserRole.Engineer,
                isActive: true);
        }
    }

    [Fact]
    public async Task ConcurrentSetupAdmin_ShouldSucceedAtMostOnce_WithSeparateSqliteContexts()
    {
        AuthService.ResetInMemoryStateForTests();
        await using var database = await AuthorityDatabase.CreateAsync();
        var rendezvous = new AsyncRendezvous(participants: 2);
        var firstTask = SetupAfterRendezvousAsync("setup-admin-a");
        var secondTask = SetupAfterRendezvousAsync("setup-admin-b");

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Count(result => result.Success).Should().Be(1);
        results.Count(result => result.ErrorCode == AuthService.InstallationAlreadyCompletedCode)
            .Should().Be(1);
        await using var verifyContext = database.CreateContext();
        (await verifyContext.Users.AsNoTracking().CountAsync(user =>
            user.Role == UserRole.Admin && user.IsActive && !user.IsDeleted)).Should().Be(1);
        (await verifyContext.InstallationStates.AsNoTracking().SingleAsync()).IsCompleted.Should().BeTrue();

        async Task<AuthResult> SetupAfterRendezvousAsync(string username)
        {
            await using var context = database.CreateContext();
            var service = CreateAuthService(new UserRepository(context));
            await rendezvous.ArriveAsync();
            return await service.SetupInitialAdminAsync(new InitialAdminSetupRequest
            {
                Username = username,
                Password = "Password123",
                ConfirmPassword = "Password123"
            });
        }
    }

    [Fact]
    public async Task Migration_ShouldBackfillCompletedLatch_WhenLegacyDatabaseAlreadyHasUser()
    {
        await using var database = await AuthorityDatabase.CreateEmptyAsync();
        await using (var oldContext = database.CreateContext())
        {
            var migrator = oldContext.GetService<IMigrator>();
            await migrator.MigrateAsync("20260714000000_AddStationExecutionIdentity");
            oldContext.Users.Add(User.Create("legacy-user", "hash", "legacy-user", UserRole.Operator));
            await oldContext.SaveChangesAsync();
        }

        await using (var upgradedContext = database.CreateContext())
        {
            await upgradedContext.Database.MigrateAsync();
            var state = await upgradedContext.InstallationStates.AsNoTracking().SingleAsync();
            state.IsCompleted.Should().BeTrue();
            state.CompletedAtUtc.Should().NotBeNull();
            state.Revision.Should().Be(1);
        }
    }

    [Fact]
    public async Task LocalRecovery_ShouldRestoreAdminWithoutReopeningLatch()
    {
        await using var database = await AuthorityDatabase.CreateAsync();
        Guid userId;
        await using (var context = database.CreateContext())
        {
            var repository = new UserRepository(context);
            var admin = await CreateInitialAdminAsync(repository, "recovery-user");
            userId = admin.Id;
            await context.Database.ExecuteSqlRawAsync("DELETE FROM \"Users\";");
        }

        await using (var recoveryContext = database.CreateContext())
        {
            var recovery = new LocalAdminRecoveryService(recoveryContext);
            var result = await recovery.RecoverAsync("recovery-user", "new-password-hash");
            result.WasCreated.Should().BeTrue();
        }

        await using var verifyContext = database.CreateContext();
        var recovered = await verifyContext.Users.AsNoTracking().SingleAsync();
        recovered.Id.Should().NotBe(userId);
        recovered.Username.Should().Be("recovery-user");
        recovered.Role.Should().Be(UserRole.Admin);
        recovered.IsActive.Should().BeTrue();
        (await verifyContext.InstallationStates.AsNoTracking().SingleAsync()).IsCompleted.Should().BeTrue();
    }

    private static AuthService CreateAuthService(IUserRepository repository)
    {
        return new AuthService(repository, new PasswordHasher(), CreateConfigurationService());
    }

    private static IConfigurationService CreateConfigurationService()
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig
        {
            Security = new SecurityConfig
            {
                PasswordMinLength = 8,
                LoginFailureLockoutCount = 5
            }
        });
        return configuration;
    }

    private static async Task<User> CreateInitialAdminAsync(UserRepository repository, string username)
    {
        var admin = User.Create(username, "hash", username, UserRole.Admin);
        var result = await repository.TryCreateInitialAdminAsync(admin);
        result.Status.Should().Be(UserAuthorityMutationStatus.Success);
        return admin;
    }

    private sealed class AsyncRendezvous
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public AsyncRendezvous(int participants)
        {
            _participants = participants;
        }

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == _participants)
            {
                _completion.TrySetResult();
            }

            await _completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class AuthorityDatabase : IAsyncDisposable
    {
        private readonly string _root;

        private AuthorityDatabase(string root, DbContextOptions<VisionDbContext> options)
        {
            _root = root;
            Options = options;
        }

        public DbContextOptions<VisionDbContext> Options { get; }

        public static async Task<AuthorityDatabase> CreateAsync()
        {
            var database = await CreateEmptyAsync();
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }

        public static Task<AuthorityDatabase> CreateEmptyAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "ClearVision.UserAuthority.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "authority.db");
            var options = new DbContextOptionsBuilder<VisionDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=10")
                .Options;
            return Task.FromResult(new AuthorityDatabase(root, options));
        }

        public VisionDbContext CreateContext() => new(Options);

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
