using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class ServerExecutionResourceAuthorityTests
{
    private const string DatabaseProfileId = "inspection-db";
    private const string DatabaseConnectionString = "Data Source=server-authorized.db;Password=server-secret";
    private const string SerialProfileId = "line-controller";

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ExecutionResources\":null}")]
    public void Normalize_LegacyOrNullExecutionResourceConfiguration_ShouldRemainEmptyAndDenyByDefault(string json)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(json)!;

        config.Normalize();

        config.ExecutionResources.Should().NotBeNull();
        config.ExecutionResources.DatabaseProfiles.Should().BeEmpty();
        config.ExecutionResources.SerialProfiles.Should().BeEmpty();
    }

    [Theory]
    [InlineData("missing-id", "RESOURCE_DATABASE_PROFILE_REQUIRED")]
    [InlineData("missing-profile", "RESOURCE_DATABASE_PROFILE_NOT_FOUND")]
    [InlineData("disabled-profile", "RESOURCE_DATABASE_PROFILE_DISABLED")]
    [InlineData("invalid-profile", "RESOURCE_DATABASE_PROFILE_INVALID")]
    [InlineData("duplicate-profile", "RESOURCE_DATABASE_PROFILE_AMBIGUOUS")]
    [InlineData("table-not-authorized", "RESOURCE_DATABASE_TABLE_NOT_AUTHORIZED")]
    public void Validate_DatabaseProfileRejectMatrix_ShouldFailClosedWithoutLeakingTarget(
        string scenario,
        string expectedCode)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = CreateDatabaseOperator();
        ApplyDatabaseScenario(config, @operator, scenario);
        var authority = CreateAuthority(config);

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(expectedCode);
        result.Message.Should().NotContain("server-secret");
        result.Message.Should().NotContain("server-authorized.db");
    }

    [Theory]
    [InlineData("connection-mismatch")]
    [InlineData("type-mismatch")]
    public void Validate_DatabaseRawClientTarget_ShouldNotOverrideAuthoritativeProfile(string scenario)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = CreateDatabaseOperator();
        ApplyDatabaseScenario(config, @operator, scenario);
        var authority = CreateAuthority(config);

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_DatabaseProfileExactBinding_ShouldAllowAuthoritativeTarget()
    {
        var authority = CreateAuthority(CreateConfiguredAppConfig());

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(CreateDatabaseOperator())));

        result.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, "RESOURCE_AUTHORITY_ALLOWED")]
    [InlineData(false, "RESOURCE_EXTERNAL_CAMERA_BINDING_INVALID")]
    public void Validate_ExternalCameraBinding_ShouldRequireEnabledServerBinding(
        bool enabled,
        string expectedCode)
    {
        const string cameraBindingId = "line-camera";
        var config = CreateConfiguredAppConfig();
        config.Cameras =
        [
            new CameraBindingConfig
            {
                Id = cameraBindingId,
                DisplayName = "Line camera",
                SerialNumber = "CAM-SERVER-001",
                IsEnabled = enabled
            }
        ];
        var flow = new OperatorFlow("external-camera-flow");
        const long revision = 42;
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "StoredProject",
            new Dictionary<string, string> { ["ProjectRevision"] = revision.ToString() },
            new ExecutionExternalResourceManifest(cameraBindingId));
        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            bindings,
            principal: ExecutionPrincipal.System("external-camera-test"),
            capabilityManifest: new ExecutionCapabilityManifest(ExecutionSideEffect.DeviceRead, false),
            externalCapabilities: ExecutionSideEffect.DeviceRead);

        var result = CreateAuthority(config).Validate(snapshot);

        result.Allowed.Should().Be(enabled);
        result.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void Validate_ImageSaveLegacyFolderPathOutsideApprovedRoot_ShouldRejectForgedSeed()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"clearvision-image-save-{Guid.NewGuid():N}");
        var approvedRoot = Path.Combine(baseDirectory, "approved");
        Directory.CreateDirectory(approvedRoot);
        try
        {
            var config = CreateConfiguredAppConfig();
            config.Storage.ImageSavePath = approvedRoot;
            var @operator = new Operator(Guid.NewGuid(), "Image save", OperatorType.ImageSave, 0, 0);
            AddParameter(@operator, "FolderPath", Path.Combine(baseDirectory, "outside"));
            var flow = SingleOperatorFlow(@operator);
            const long revision = 42;
            var bindings = ExecutionResourceBindingManifest.Build(
                flow,
                "StoredProject",
                new Dictionary<string, string>
                {
                    ["ProjectRevision"] = revision.ToString(),
                    [$"Resource:{@operator.Id:N}"] = "StoredProject:FORGED"
                });
            var snapshot = new ExecutionSnapshot(
                Guid.NewGuid(),
                flow,
                revision,
                ExecutionSnapshotSource.PersistedProject,
                ExecutionRunMode.FormalPrimary,
                bindings,
                principal: ExecutionPrincipal.System("image-save-path-test"));

            var result = CreateAuthority(config).Validate(snapshot);

            result.Allowed.Should().BeFalse();
            result.Code.Should().Be("RESOURCE_PATH_OUTSIDE_APPROVED_ROOT");
            bindings[$"Resource:{@operator.Id:N}"].Should().NotContain("FORGED");
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing-id", "RESOURCE_SERIAL_PROFILE_REQUIRED")]
    [InlineData("missing-profile", "RESOURCE_SERIAL_PROFILE_NOT_FOUND")]
    [InlineData("disabled-profile", "RESOURCE_SERIAL_PROFILE_DISABLED")]
    [InlineData("invalid-profile", "RESOURCE_SERIAL_PROFILE_INVALID")]
    [InlineData("duplicate-profile", "RESOURCE_SERIAL_PROFILE_AMBIGUOUS")]
    public void Validate_SerialProfileRejectMatrix_ShouldFailClosedWithoutLeakingTarget(
        string scenario,
        string expectedCode)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = CreateSerialOperator(OperatorType.SerialCommunication);
        ApplySerialScenario(config, @operator, scenario);
        var authority = CreateAuthority(config);

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(expectedCode);
        result.Message.Should().NotContain("COM37");
    }

    [Theory]
    [InlineData("port-mismatch")]
    [InlineData("baud-mismatch")]
    [InlineData("format-mismatch")]
    public void Validate_SerialRawClientTarget_ShouldNotOverrideAuthoritativeProfile(string scenario)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = CreateSerialOperator(OperatorType.SerialCommunication);
        ApplySerialScenario(config, @operator, scenario);
        var authority = CreateAuthority(config);

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(OperatorType.SerialCommunication)]
    [InlineData(OperatorType.ModbusRtuCommunication)]
    public void Validate_SerialAndModbusRtuExactBinding_ShouldAllowAuthoritativeTarget(OperatorType operatorType)
    {
        var authority = CreateAuthority(CreateConfiguredAppConfig());

        var result = authority.Validate(CreateStoredSnapshot(SingleOperatorFlow(CreateSerialOperator(operatorType))));

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void ResourceManifest_ModbusRtuAliasClone_ShouldPreserveSerialAuthorityFingerprint()
    {
        const long revision = 42;
        var flow = SingleOperatorFlow(CreateSerialOperator(OperatorType.ModbusRtuCommunication));
        var seed = new Dictionary<string, string>
        {
            ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var beforeClone = ExecutionResourceBindingManifest.Build(flow, "StoredProject", seed);

        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            beforeClone,
            principal: ExecutionPrincipal.System("resource-authority-test"));
        var capturedFlow = snapshot.CreateExecutionFlow();
        var afterClone = ExecutionResourceBindingManifest.Build(capturedFlow, "StoredProject", seed);

        capturedFlow.Operators.Should().ContainSingle()
            .Which.Type.Should().Be(OperatorType.ModbusCommunication);
        afterClone.Should().BeEquivalentTo(beforeClone);
    }

    [Fact]
    public void ResourceManifest_ModbusRtuAliasClone_WithProfileOnly_ShouldKeepSerialAuthority()
    {
        const long revision = 42;
        var @operator = new Operator(
            Guid.NewGuid(),
            "Profile-only Modbus RTU",
            OperatorType.ModbusRtuCommunication,
            0,
            0);
        AddParameter(@operator, "ProfileId", SerialProfileId);
        AddParameter(@operator, "Protocol", "RTU");
        var flow = SingleOperatorFlow(@operator);
        var seed = new Dictionary<string, string>
        {
            ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var beforeClone = ExecutionResourceBindingManifest.Build(flow, "StoredProject", seed);
        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            beforeClone,
            principal: ExecutionPrincipal.System("resource-authority-test"));

        var capturedFlow = snapshot.CreateExecutionFlow();
        var afterClone = ExecutionResourceBindingManifest.Build(capturedFlow, "StoredProject", seed);
        var result = CreateAuthority(CreateConfiguredAppConfig()).Validate(snapshot);

        capturedFlow.Operators.Should().ContainSingle()
            .Which.Type.Should().Be(OperatorType.ModbusCommunication);
        afterClone.Should().BeEquivalentTo(beforeClone);
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWithSnapshotAsync_ModbusTcpWithInjectedSerialShape_ShouldNotBypassPlcAuthority()
    {
        var @operator = CreateSerialOperator(OperatorType.ModbusCommunication);
        AddParameter(@operator, "Protocol", "TCP");
        AddParameter(@operator, "IpAddress", "203.0.113.50");
        AddParameter(@operator, "Port", 502);
        var engine = Substitute.For<IFlowExecutionEngine>();
        var service = new GovernedFlowExecutionService(engine, CreateAuthority(CreateConfiguredAppConfig()));

        var result = await service.ExecuteWithSnapshotAsync(
            CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("RESOURCE_MODBUS_RTU_BINDING_INVALID");
        engine.DidNotReceive().ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceive().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OperatorType.DatabaseWrite, "missing-id", "RESOURCE_DATABASE_PROFILE_REQUIRED")]
    [InlineData(OperatorType.DatabaseWrite, "missing-profile", "RESOURCE_DATABASE_PROFILE_NOT_FOUND")]
    [InlineData(OperatorType.DatabaseWrite, "disabled-profile", "RESOURCE_DATABASE_PROFILE_DISABLED")]
    [InlineData(OperatorType.DatabaseWrite, "invalid-profile", "RESOURCE_DATABASE_PROFILE_INVALID")]
    [InlineData(OperatorType.DatabaseWrite, "duplicate-profile", "RESOURCE_DATABASE_PROFILE_AMBIGUOUS")]
    [InlineData(OperatorType.DatabaseWrite, "table-not-authorized", "RESOURCE_DATABASE_TABLE_NOT_AUTHORIZED")]
    [InlineData(OperatorType.SerialCommunication, "missing-id", "RESOURCE_SERIAL_PROFILE_REQUIRED")]
    [InlineData(OperatorType.SerialCommunication, "missing-profile", "RESOURCE_SERIAL_PROFILE_NOT_FOUND")]
    [InlineData(OperatorType.SerialCommunication, "disabled-profile", "RESOURCE_SERIAL_PROFILE_DISABLED")]
    [InlineData(OperatorType.SerialCommunication, "invalid-profile", "RESOURCE_SERIAL_PROFILE_INVALID")]
    [InlineData(OperatorType.SerialCommunication, "duplicate-profile", "RESOURCE_SERIAL_PROFILE_AMBIGUOUS")]
    [InlineData(OperatorType.ModbusRtuCommunication, "missing-id", "RESOURCE_SERIAL_PROFILE_REQUIRED")]
    [InlineData(OperatorType.ModbusRtuCommunication, "missing-profile", "RESOURCE_SERIAL_PROFILE_NOT_FOUND")]
    [InlineData(OperatorType.ModbusRtuCommunication, "disabled-profile", "RESOURCE_SERIAL_PROFILE_DISABLED")]
    [InlineData(OperatorType.ModbusRtuCommunication, "invalid-profile", "RESOURCE_SERIAL_PROFILE_INVALID")]
    [InlineData(OperatorType.ModbusRtuCommunication, "duplicate-profile", "RESOURCE_SERIAL_PROFILE_AMBIGUOUS")]
    public async Task ExecuteWithSnapshotAsync_UntrustedResourceProfile_ShouldRejectBeforeEngineHandler(
        OperatorType operatorType,
        string scenario,
        string expectedCode)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = operatorType == OperatorType.DatabaseWrite
            ? CreateDatabaseOperator()
            : CreateSerialOperator(operatorType);
        if (operatorType == OperatorType.DatabaseWrite)
        {
            ApplyDatabaseScenario(config, @operator, scenario);
        }
        else
        {
            ApplySerialScenario(config, @operator, scenario);
        }

        var engine = Substitute.For<IFlowExecutionEngine>();
        var service = new GovernedFlowExecutionService(engine, CreateAuthority(config));
        var snapshot = CreateStoredSnapshot(SingleOperatorFlow(@operator));

        var result = await service.ExecuteWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain(expectedCode);
        engine.DidNotReceive().ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceive().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(OperatorType.DatabaseWrite, "connection-mismatch")]
    [InlineData(OperatorType.DatabaseWrite, "type-mismatch")]
    [InlineData(OperatorType.SerialCommunication, "port-mismatch")]
    [InlineData(OperatorType.SerialCommunication, "baud-mismatch")]
    [InlineData(OperatorType.SerialCommunication, "format-mismatch")]
    [InlineData(OperatorType.ModbusRtuCommunication, "port-mismatch")]
    [InlineData(OperatorType.ModbusRtuCommunication, "baud-mismatch")]
    [InlineData(OperatorType.ModbusRtuCommunication, "format-mismatch")]
    public async Task ExecuteWithSnapshotAsync_RawClientTargetMismatch_ShouldUseProfileAndReachEngine(
        OperatorType operatorType,
        string scenario)
    {
        var config = CreateConfiguredAppConfig();
        var @operator = operatorType == OperatorType.DatabaseWrite
            ? CreateDatabaseOperator()
            : CreateSerialOperator(operatorType);
        if (operatorType == OperatorType.DatabaseWrite)
        {
            ApplyDatabaseScenario(config, @operator, scenario);
        }
        else
        {
            ApplySerialScenario(config, @operator, scenario);
        }

        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>())
            .Returns(new FlowValidationResult { IsValid = true });
        engine.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult { IsSuccess = true }));
        var service = new GovernedFlowExecutionService(engine, CreateAuthority(config));

        var result = await service.ExecuteWithSnapshotAsync(
            CreateStoredSnapshot(SingleOperatorFlow(@operator)));

        result.IsSuccess.Should().BeTrue();
        engine.Received(1).ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.Received(1).ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    private static ServerExecutionResourceAuthority CreateAuthority(AppConfig config)
    {
        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(config);
        return new ServerExecutionResourceAuthority(configurationService);
    }

    private static AppConfig CreateConfiguredAppConfig()
    {
        return new AppConfig
        {
            ExecutionResources = new ExecutionResourceProfilesConfig
            {
                DatabaseProfiles =
                [
                    new DatabaseExecutionResourceProfile
                    {
                        Id = DatabaseProfileId,
                        Enabled = true,
                        DbType = "SQLite",
                        ConnectionString = DatabaseConnectionString,
                        AllowedTableNames = ["InspectionResults"]
                    }
                ],
                SerialProfiles =
                [
                    new SerialExecutionResourceProfile
                    {
                        Id = SerialProfileId,
                        Enabled = true,
                        PortName = "COM37",
                        BaudRate = 115200,
                        DataBits = 8,
                        StopBits = "One",
                        Parity = "None"
                    }
                ]
            }
        };
    }

    private static Operator CreateDatabaseOperator()
    {
        var @operator = new Operator(Guid.NewGuid(), "Database write", OperatorType.DatabaseWrite, 0, 0);
        AddParameter(@operator, "ProfileId", DatabaseProfileId);
        AddParameter(@operator, "ConnectionString", DatabaseConnectionString);
        AddParameter(@operator, "DbType", "SQLite");
        AddParameter(@operator, "TableName", "InspectionResults");
        return @operator;
    }

    private static Operator CreateSerialOperator(OperatorType operatorType)
    {
        var @operator = new Operator(Guid.NewGuid(), "Serial write", operatorType, 0, 0);
        AddParameter(@operator, "ProfileId", SerialProfileId);
        if (operatorType == OperatorType.ModbusRtuCommunication)
        {
            AddParameter(@operator, "Protocol", "RTU");
        }

        AddParameter(@operator, "PortName", "COM37");
        AddParameter(@operator, "BaudRate", "115200");
        AddParameter(@operator, "DataBits", 8);
        AddParameter(@operator, "StopBits", "One");
        AddParameter(@operator, "Parity", "None");
        return @operator;
    }

    private static void ApplyDatabaseScenario(AppConfig config, Operator @operator, string scenario)
    {
        switch (scenario)
        {
            case "missing-id":
                SetParameter(@operator, "ProfileId", string.Empty);
                break;
            case "missing-profile":
                config.ExecutionResources.DatabaseProfiles.Clear();
                break;
            case "disabled-profile":
                config.ExecutionResources.DatabaseProfiles[0].Enabled = false;
                break;
            case "invalid-profile":
                config.ExecutionResources.DatabaseProfiles[0].ConnectionString = string.Empty;
                break;
            case "duplicate-profile":
                config.ExecutionResources.DatabaseProfiles.Add(new DatabaseExecutionResourceProfile
                {
                    Id = DatabaseProfileId
                });
                break;
            case "connection-mismatch":
                SetParameter(@operator, "ConnectionString", "Data Source=client-forged.db");
                break;
            case "type-mismatch":
                SetParameter(@operator, "DbType", "MySQL");
                break;
            case "table-not-authorized":
                SetParameter(@operator, "TableName", "AdminSecrets");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private static void ApplySerialScenario(AppConfig config, Operator @operator, string scenario)
    {
        switch (scenario)
        {
            case "missing-id":
                SetParameter(@operator, "ProfileId", string.Empty);
                break;
            case "missing-profile":
                config.ExecutionResources.SerialProfiles.Clear();
                break;
            case "disabled-profile":
                config.ExecutionResources.SerialProfiles[0].Enabled = false;
                break;
            case "invalid-profile":
                config.ExecutionResources.SerialProfiles[0].PortName = string.Empty;
                break;
            case "duplicate-profile":
                config.ExecutionResources.SerialProfiles.Add(new SerialExecutionResourceProfile
                {
                    Id = SerialProfileId
                });
                break;
            case "port-mismatch":
                SetParameter(@operator, "PortName", "COM99");
                break;
            case "baud-mismatch":
                SetParameter(@operator, "BaudRate", "9600");
                break;
            case "format-mismatch":
                SetParameter(@operator, "Parity", "Even");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    private static OperatorFlow SingleOperatorFlow(Operator @operator)
    {
        var flow = new OperatorFlow($"resource-authority-{@operator.Type}");
        flow.AddOperator(@operator);
        return flow;
    }

    private static ExecutionSnapshot CreateStoredSnapshot(OperatorFlow flow)
    {
        const long revision = 42;
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "StoredProject",
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            bindings,
            principal: ExecutionPrincipal.System("resource-authority-test"));
    }

    private static void AddParameter(Operator @operator, string name, object value)
    {
        @operator.AddParameter(new Parameter(
            Guid.NewGuid(),
            name,
            name,
            string.Empty,
            value is int ? "int" : "string",
            value));
    }

    private static void SetParameter(Operator @operator, string name, object value)
    {
        var parameter = @operator.Parameters.Single(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        parameter.SetValue(value);
    }
}
