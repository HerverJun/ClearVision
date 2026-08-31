using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class ServerExecutionResourceProfileResolverTests
{
    [Fact]
    public void ResolveDatabase_EnabledProfile_ShouldReturnOnlyServerTargetAndCanonicalTable()
    {
        const string serverConnectionString = "Data Source=server-owned.db;Password=server-secret";
        var resolver = CreateResolver(new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                DatabaseProfiles =
                [
                    new DatabaseExecutionResourceProfile
                    {
                        Id = "inspection-db",
                        Enabled = true,
                        DbType = "SQLite",
                        ConnectionString = serverConnectionString,
                        AllowedTableNames = ["InspectionResults"]
                    }
                ]
            }
        });

        var result = resolver.ResolveDatabase("inspection-db", "inspectionresults");

        result.Resolved.Should().BeTrue();
        result.Resource.Should().NotBeNull();
        result.Resource!.ConnectionString.Should().Be(serverConnectionString);
        result.Resource.DbType.Should().Be("SQLite");
        result.Resource.TableName.Should().Be("InspectionResults");
    }

    [Theory]
    [InlineData("missing", "RESOURCE_DATABASE_PROFILE_NOT_FOUND")]
    [InlineData("disabled", "RESOURCE_DATABASE_PROFILE_DISABLED")]
    [InlineData("table", "RESOURCE_DATABASE_TABLE_NOT_AUTHORIZED")]
    public void ResolveDatabase_UnavailableAuthority_ShouldFailClosedWithoutLeakingSecret(
        string scenario,
        string expectedCode)
    {
        var profile = new DatabaseExecutionResourceProfile
        {
            Id = "inspection-db",
            Enabled = scenario != "disabled",
            DbType = "SQLite",
            ConnectionString = "Data Source=server-owned.db;Password=server-secret",
            AllowedTableNames = ["InspectionResults"]
        };
        var config = new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                DatabaseProfiles = scenario == "missing" ? [] : [profile]
            }
        };
        var resolver = CreateResolver(config);

        var result = resolver.ResolveDatabase(
            "inspection-db",
            scenario == "table" ? "AdminSecrets" : "InspectionResults");

        result.Resolved.Should().BeFalse();
        result.Code.Should().Be(expectedCode);
        result.Resource.Should().BeNull();
        result.Message.Should().NotContain("server-secret");
        result.Message.Should().NotContain("server-owned.db");
    }

    [Fact]
    public void ResolveSerial_EnabledProfile_ShouldReturnExactServerDeviceTarget()
    {
        var resolver = CreateResolver(new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                SerialProfiles =
                [
                    new SerialExecutionResourceProfile
                    {
                        Id = "line-controller",
                        Enabled = true,
                        PortName = "COM37",
                        BaudRate = 115200,
                        DataBits = 7,
                        StopBits = "Two",
                        Parity = "Even"
                    }
                ]
            }
        });

        var result = resolver.ResolveSerial("line-controller");

        result.Resolved.Should().BeTrue();
        result.Resource.Should().NotBeNull();
        result.Resource!.PortName.Should().Be("COM37");
        result.Resource.BaudRate.Should().Be(115200);
        result.Resource.DataBits.Should().Be(7);
        result.Resource.StopBits.Should().Be("Two");
        result.Resource.Parity.Should().Be("Even");
    }

    [Fact]
    public void ResolveSerial_MissingConfiguration_ShouldFailClosedBeforeReturningDeviceTarget()
    {
        var resolver = new ServerExecutionResourceProfileResolver(configurationService: null);

        var result = resolver.ResolveSerial("line-controller");

        result.Resolved.Should().BeFalse();
        result.Code.Should().Be("RESOURCE_CONFIGURATION_UNAVAILABLE");
        result.Resource.Should().BeNull();
    }

    private static ServerExecutionResourceProfileResolver CreateResolver(AppConfig config)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(config);
        return new ServerExecutionResourceProfileResolver(configurationService);
    }
}
