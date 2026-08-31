using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class UserManagementSessionRevocationTests
{
    [Fact]
    public async Task SuccessfulAuthorityUpdate_ShouldRevokeExistingSessions()
    {
        var (service, repository, revocation, user) = CreateService();
        repository.TryUpdatePreservingActiveAdminAsync(
                user.Id,
                Arg.Any<string?>(),
                UserRole.Engineer,
                true,
                Arg.Any<CancellationToken>())
            .Returns(UserAuthorityMutationResult.Succeeded(user));

        var result = await service.UpdateUserAsync(user.Id.ToString(), new UpdateUserRequest
        {
            DisplayName = "updated",
            Role = UserRole.Engineer,
            IsActive = true
        });

        result.Success.Should().BeTrue();
        revocation.Received(1).RevokeSessionsForUser(user.Id.ToString(), "user-authority-updated");
    }

    [Fact]
    public async Task FailedAuthorityUpdate_ShouldNotRevokeSessions()
    {
        var (service, repository, revocation, user) = CreateService();
        repository.TryUpdatePreservingActiveAdminAsync(
                user.Id,
                Arg.Any<string?>(),
                UserRole.Operator,
                false,
                Arg.Any<CancellationToken>())
            .Returns(UserAuthorityMutationResult.Failed(UserAuthorityMutationStatus.LastActiveAdmin));

        var result = await service.UpdateUserAsync(user.Id.ToString(), new UpdateUserRequest
        {
            Role = UserRole.Operator,
            IsActive = false
        });

        result.Success.Should().BeFalse();
        revocation.DidNotReceiveWithAnyArgs().RevokeSessionsForUser(default!, default!);
    }

    [Fact]
    public async Task SuccessfulDeleteAndPasswordReset_ShouldRevokeSessions()
    {
        var (service, repository, revocation, user) = CreateService();
        repository.TryDeletePreservingActiveAdminAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(UserAuthorityMutationResult.Succeeded(user));
        repository.GetByIdAsync(user.Id).Returns(user);

        (await service.DeleteUserAsync(user.Id.ToString())).Success.Should().BeTrue();
        (await service.ResetPasswordAsync(user.Id.ToString(), "NewPassword1")).Success.Should().BeTrue();

        revocation.Received(1).RevokeSessionsForUser(user.Id.ToString(), "user-deleted");
        revocation.Received(1).RevokeSessionsForUser(user.Id.ToString(), "password-reset");
    }

    private static (UserManagementService Service, IUserRepository Repository, IAuthSessionRevocationService Revocation, User User) CreateService()
    {
        var repository = Substitute.For<IUserRepository>();
        var passwordHasher = Substitute.For<IPasswordHasher>();
        var configuration = Substitute.For<IConfigurationService>();
        var revocation = Substitute.For<IAuthSessionRevocationService>();
        configuration.GetCurrent().Returns(new AppConfig
        {
            Security = new SecurityConfig { PasswordMinLength = 8 }
        });
        passwordHasher.HashPassword(Arg.Any<string>()).Returns("new-hash");
        var user = User.Create("session-user", "old-hash", "Session User", UserRole.Admin);
        return (new UserManagementService(repository, passwordHasher, configuration, revocation), repository, revocation, user);
    }
}
