using ClearVision.Product.Application.Security;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using FluentAssertions;
using NSubstitute;
using System.Reflection;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class AuthenticatedContextProjectionServiceTests
{
    [Theory]
    [InlineData("Admin", true, true, true)]
    [InlineData("Engineer", false, true, true)]
    [InlineData("Operator", false, false, false)]
    public void Project_ShouldUseServerPermissionRulesForRoleMatrix(
        string role,
        bool canManageUsers,
        bool canOperateHardware,
        bool canEditProject)
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig
        {
            Security = new SecurityConfig { PasswordMinLength = 13 }
        });
        var service = new AuthenticatedContextProjectionService(configuration);

        var result = service.Project(new UserSession
        {
            UserId = "user-42",
            Username = "line-user",
            Role = role
        });

        result.UserId.Should().Be("user-42");
        result.Username.Should().Be("line-user");
        result.Role.Should().Be(role);
        result.PasswordPolicy.MinimumLength.Should().Be(13);
        result.Capabilities.Contains(ClearVisionCapabilities.UsersRead).Should().Be(canManageUsers);
        result.Capabilities.Contains(ClearVisionCapabilities.CameraBindingsUpdate).Should().Be(canOperateHardware);
        result.Capabilities.Contains(ClearVisionCapabilities.PlcConnectionTest).Should().Be(canOperateHardware);
        result.Capabilities.Contains(ClearVisionCapabilities.ProjectEdit).Should().Be(canEditProject);
        result.Capabilities.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Project_ShouldKeepAdminOnlyAndHardwarePoliciesDistinct()
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig());
        var service = new AuthenticatedContextProjectionService(configuration);

        var engineer = service.Project(new UserSession
        {
            UserId = "engineer-id",
            Username = "engineer",
            Role = "Engineer"
        });

        engineer.Capabilities.Should().Contain([
            ClearVisionCapabilities.CameraBindingsUpdate,
            ClearVisionCapabilities.CameraCapture,
            ClearVisionCapabilities.PlcConnectionTest,
            ClearVisionCapabilities.TcpConnectionsOperate
        ]);
        engineer.Capabilities.Should().NotContain([
            ClearVisionCapabilities.PlcSettingsUpdate,
            ClearVisionCapabilities.DatabaseBackup,
            ClearVisionCapabilities.AiModelsUpdate,
            ClearVisionCapabilities.StationCommandsCreate,
            ClearVisionCapabilities.UsersUpdate
        ]);
    }

    [Fact]
    public void Project_ShouldReturnExactCapabilitySetsForEverySupportedRole()
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig());
        var service = new AuthenticatedContextProjectionService(configuration);

        var admin = service.Project(SessionFor("Admin"));
        var engineer = service.Project(SessionFor("Engineer"));
        var @operator = service.Project(SessionFor("Operator"));
        var unknown = service.Project(SessionFor("Unknown"));

        var allDeclaredCapabilities = typeof(ClearVisionCapabilities)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        admin.Capabilities.Should().Equal(allDeclaredCapabilities);
        engineer.Capabilities.Should().Equal([
            ClearVisionCapabilities.CameraBindingsUpdate,
            ClearVisionCapabilities.CameraCapture,
            ClearVisionCapabilities.CameraPreviewOperate,
            ClearVisionCapabilities.InspectionResultsRead,
            ClearVisionCapabilities.PlcConnectionTest,
            ClearVisionCapabilities.ProjectEdit,
            ClearVisionCapabilities.TcpConnectionsOperate,
            ClearVisionCapabilities.TriggerInputOperate
        ]);
        @operator.Capabilities.Should().Equal([
            ClearVisionCapabilities.InspectionResultsRead
        ]);
        unknown.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public void Project_ShouldApplySharedMinimumPasswordFloor()
    {
        var configuration = Substitute.For<IConfigurationService>();
        configuration.GetCurrent().Returns(new AppConfig
        {
            Security = new SecurityConfig { PasswordMinLength = 4 }
        });
        var service = new AuthenticatedContextProjectionService(configuration);

        var result = service.Project(SessionFor("Operator"));

        result.PasswordPolicy.MinimumLength.Should().Be(6);
    }

    private static UserSession SessionFor(string role) => new()
    {
        UserId = $"{role.ToLowerInvariant()}-id",
        Username = role.ToLowerInvariant(),
        Role = role
    };
}
