using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Enums;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using Acme.Product.Station;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Acme.Product.Tests.Runtime;

public sealed class RuntimeParameterContractsTests
{
    [Fact]
    public void RuntimeParameterDtos_ShouldRoundTripJson()
    {
        var operatorId = Guid.NewGuid();
        var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
        var schema = RuntimeParameterTestData.CreateSchema("pkg-1", "sha256:abc", operatorId, parameterId);
        var profile = RuntimeParameterTestData.CreateProfile(
            "pkg-1",
            "sha256:abc",
            RuntimeParameterTestData.CreateOverride(parameterId, 0.72d));

        var schemaJson = JsonSerializer.Serialize(schema, RuntimeParameterTestData.JsonOptions);
        var profileJson = JsonSerializer.Serialize(profile, RuntimeParameterTestData.JsonOptions);

        var roundTrippedSchema = JsonSerializer.Deserialize<RuntimeParameterSchema>(schemaJson, RuntimeParameterTestData.JsonOptions)!;
        var roundTrippedProfile = JsonSerializer.Deserialize<RuntimeSiteProfile>(profileJson, RuntimeParameterTestData.JsonOptions)!;

        roundTrippedSchema.Parameters.Single().ValueType.Should().Be(RuntimeParameterValueType.Number);
        roundTrippedSchema.Parameters.Single().UiKind.Should().Be(RuntimeParameterUiKind.NumericInput);
        roundTrippedSchema.Parameters.Single().DefaultValue.GetDouble().Should().Be(0.5d);
        roundTrippedProfile.Overrides.Single().Value.GetDouble().Should().Be(0.72d);
    }
}

public sealed class RuntimePackageExporterTests
{
    [Fact]
    public async Task ExportAsync_ShouldWriteDeepLearningConfidenceRuntimeParameterSchema()
    {
        var root = RuntimeParameterTestData.CreateTempDirectory("ClearVisionRuntimeParameterExporterTests");
        try
        {
            var modelPath = Path.Combine(root, "model.onnx");
            await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4]);
            var operatorId = Guid.NewGuid();
            var exporter = new RuntimePackageExporter(
                new Acme.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = root,
                Project = new ProjectDto
                {
                    Id = Guid.NewGuid(),
                    Name = "runtime-parameters",
                    Flow = new OperatorFlowDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "main",
                        Operators =
                        [
                            RuntimeParameterTestData.CreateDeepLearningOperator(
                                operatorId,
                                "线序检测",
                                modelPath,
                                confidence: 0.62d)
                        ]
                    }
                }
            });

            export.Manifest.FieldExtensions.RuntimeParameters.Should().Be("field/runtime-parameters.json");
            export.Manifest.FieldExtensions.DefaultSiteProfile.Should().Be("field/station-profile.default.json");

            var schemaPath = Path.Combine(export.PackageRootPath, "field", "runtime-parameters.json");
            var defaultProfilePath = Path.Combine(export.PackageRootPath, "field", "station-profile.default.json");
            File.Exists(schemaPath).Should().BeTrue();
            File.Exists(defaultProfilePath).Should().BeTrue();

            var schema = JsonSerializer.Deserialize<RuntimeParameterSchema>(
                await File.ReadAllTextAsync(schemaPath),
                RuntimeParameterTestData.JsonOptions)!;
            var definition = schema.Parameters.Should().ContainSingle().Subject;

            schema.PackageId.Should().Be(export.Manifest.PackageId);
            schema.FlowHash.Should().Be(export.Manifest.FlowHash);
            definition.Id.Should().Be(RuntimeParameterTestData.ParameterId(operatorId));
            definition.OperatorId.Should().Be(operatorId);
            definition.OperatorType.Should().Be(nameof(OperatorType.DeepLearning));
            definition.ParameterName.Should().Be("Confidence");
            definition.ValueType.Should().Be(RuntimeParameterValueType.Number);
            definition.UiKind.Should().Be(RuntimeParameterUiKind.NumericInput);
            definition.DefaultValue.GetDouble().Should().Be(0.62d);
            definition.Min.Should().Be(0.0d);
            definition.Max.Should().Be(1.0d);
            definition.Step.Should().Be(0.01d);

            var defaultProfile = JsonSerializer.Deserialize<RuntimeSiteProfile>(
                await File.ReadAllTextAsync(defaultProfilePath),
                RuntimeParameterTestData.JsonOptions)!;
            defaultProfile.ProfileId.Should().Be("package-default");
            defaultProfile.PackageId.Should().Be(export.Manifest.PackageId);
            defaultProfile.FlowHash.Should().Be(export.Manifest.FlowHash);
            defaultProfile.Overrides.Should().BeEmpty();
        }
        finally
        {
            RuntimeParameterTestData.SafeDeleteDirectory(root);
        }
    }
}

public sealed class RuntimePackageLoaderTests
{
    [Fact]
    public async Task LoadAsync_ShouldAllowOldPackageWithoutRuntimeParameterSchema()
    {
        var root = RuntimeParameterTestData.CreateTempDirectory("ClearVisionRuntimeParameterLoaderTests");
        try
        {
            var exporter = new RuntimePackageExporter(
                new Acme.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = root,
                Project = RuntimeParameterTestData.CreateResultOnlyProject()
            });

            File.Delete(Path.Combine(export.PackageRootPath, "field", "runtime-parameters.json"));
            File.Delete(Path.Combine(export.PackageRootPath, "field", "station-profile.default.json"));

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);

            package.ParameterSchema.PackageId.Should().Be(export.Manifest.PackageId);
            package.ParameterSchema.FlowHash.Should().Be(export.Manifest.FlowHash);
            package.ParameterSchema.Parameters.Should().BeEmpty();
            package.DefaultSiteProfile.ProfileId.Should().Be("package-default");
            package.DefaultSiteProfile.Overrides.Should().BeEmpty();
        }
        finally
        {
            RuntimeParameterTestData.SafeDeleteDirectory(root);
        }
    }
}

public sealed class RuntimeParameterValidatorTests
{
    [Fact]
    public void Validate_ShouldRejectFlowHashMismatchUnknownParameterNonNumberAndOutOfRange()
    {
        var operatorId = Guid.NewGuid();
        var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
        var schema = RuntimeParameterTestData.CreateSchema("pkg-1", "sha256:abc", operatorId, parameterId);

        RuntimeParameterValidator.Validate(
                schema,
                RuntimeParameterTestData.CreateProfile("pkg-1", "sha256:def"))
            .Errors.Should().Contain(error => error.Contains("flowHash", StringComparison.OrdinalIgnoreCase));

        RuntimeParameterValidator.Validate(
                schema,
                RuntimeParameterTestData.CreateProfile(
                    "pkg-1",
                    "sha256:abc",
                    RuntimeParameterTestData.CreateOverride("node.unknown.Confidence", 0.7d)))
            .Errors.Should().Contain(error => error.Contains("Unknown runtime parameter", StringComparison.OrdinalIgnoreCase));

        RuntimeParameterValidator.Validate(
                schema,
                RuntimeParameterTestData.CreateProfile(
                    "pkg-1",
                    "sha256:abc",
                    new RuntimeParameterOverride
                    {
                        ParameterId = parameterId,
                        Value = JsonSerializer.SerializeToElement("0.7")
                    }))
            .Errors.Should().Contain(error => error.Contains("JSON number", StringComparison.OrdinalIgnoreCase));

        RuntimeParameterValidator.Validate(
                schema,
                RuntimeParameterTestData.CreateProfile(
                    "pkg-1",
                    "sha256:abc",
                    RuntimeParameterTestData.CreateOverride(parameterId, 1.2d)))
            .Errors.Should().Contain(error => error.Contains("above max", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class RuntimeParameterOverrideApplierTests
{
    [Fact]
    public void CloneAndApply_ShouldApplyOverrideToCloneWithoutMutatingPackageFlow()
    {
        var operatorId = Guid.NewGuid();
        var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
        var package = RuntimeParameterTestData.CreateRuntimePackage(operatorId, parameterId, confidence: 0.5d);
        var profile = RuntimeParameterTestData.CreateProfile(
            package.Manifest.PackageId,
            package.Manifest.FlowHash,
            RuntimeParameterTestData.CreateOverride(parameterId, 0.72d));

        var result = RuntimeParameterOverrideApplier.CloneAndApply(package, profile);

        RuntimeParameterTestData.ReadConfidence(result.Flow).Should().Be(0.72d);
        RuntimeParameterTestData.ReadConfidence(package.Flow).Should().Be(0.5d);
        result.Flow.Should().NotBeSameAs(package.Flow);
        result.AppliedOverrideCount.Should().Be(1);
    }
}

public sealed class StationSiteProfileStoreTests
{
    [Fact]
    public void Store_ShouldSaveReloadAndResetLocalSiteProfile()
    {
        var root = RuntimeParameterTestData.CreateTempDirectory("ClearVisionStationSiteProfileStoreTests");
        try
        {
            var operatorId = Guid.NewGuid();
            var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
            var package = RuntimeParameterTestData.CreateRuntimePackage(operatorId, parameterId, rootPath: root);
            var store = new StationSiteProfileStore(root);

            var loaded = store.LoadOrCreate(package);
            loaded.ProfileId.Should().Be("local-site");
            loaded.Overrides.Should().BeEmpty();

            loaded.Overrides =
            [
                RuntimeParameterTestData.CreateOverride(parameterId, 0.7d)
            ];
            var saved = store.Save(package, loaded);
            saved.Revision.Should().Be(1);

            var profilePath = store.GetProfilePath(package);
            File.Exists(profilePath).Should().BeTrue();
            profilePath.Should().Contain($"{package.Manifest.PackageId}_");
            profilePath.Should().NotContain("sha256:");

            var reloaded = store.LoadOrCreate(package);
            reloaded.Overrides.Should().ContainSingle();
            reloaded.Overrides.Single().Value.GetDouble().Should().Be(0.7d);

            var reset = store.ResetToPackageDefault(package, reloaded);
            reset.Revision.Should().Be(2);
            reset.Overrides.Should().BeEmpty();
            store.LoadOrCreate(package).Overrides.Should().BeEmpty();
        }
        finally
        {
            RuntimeParameterTestData.SafeDeleteDirectory(root);
        }
    }
}

internal static class RuntimeParameterTestData
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static string ParameterId(Guid operatorId) => $"node.{operatorId:D}.Confidence";

    public static RuntimeParameterSchema CreateSchema(
        string packageId,
        string flowHash,
        Guid operatorId,
        string parameterId)
    {
        return new RuntimeParameterSchema
        {
            PackageId = packageId,
            FlowHash = flowHash,
            Parameters =
            [
                new RuntimeParameterDefinition
                {
                    Id = parameterId,
                    OperatorId = operatorId,
                    OperatorName = "线序检测",
                    OperatorType = nameof(OperatorType.DeepLearning),
                    ParameterName = "Confidence",
                    DisplayName = "线序检测置信度",
                    GroupName = "现场参数",
                    ValueType = RuntimeParameterValueType.Number,
                    UiKind = RuntimeParameterUiKind.NumericInput,
                    DefaultValue = JsonSerializer.SerializeToElement(0.5d),
                    Min = 0.0d,
                    Max = 1.0d,
                    Step = 0.01d,
                    SiteTunable = true,
                    RequiresEngineerMode = true,
                    ApplyMode = RuntimeParameterApplyMode.NextRun,
                    Order = 10
                }
            ]
        };
    }

    public static RuntimeSiteProfile CreateProfile(
        string packageId,
        string flowHash,
        params RuntimeParameterOverride[] overrides)
    {
        return new RuntimeSiteProfile
        {
            ProfileId = "local-site",
            PackageId = packageId,
            FlowHash = flowHash,
            Revision = 0,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "local-engineer",
            Overrides = overrides.ToList()
        };
    }

    public static RuntimeParameterOverride CreateOverride(string parameterId, double value)
    {
        return new RuntimeParameterOverride
        {
            ParameterId = parameterId,
            Value = JsonSerializer.SerializeToElement(value)
        };
    }

    public static RuntimePackage CreateRuntimePackage(
        Guid operatorId,
        string parameterId,
        double confidence = 0.5d,
        string? rootPath = null)
    {
        var packageId = "pkg-1";
        var flowHash = "sha256:abc";
        var flow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "main",
            Operators =
            [
                CreateDeepLearningOperator(operatorId, "线序检测", "assets/model.onnx", confidence)
            ]
        };

        return new RuntimePackage
        {
            RootPath = rootPath ?? Path.GetTempPath(),
            Manifest = new RuntimePackageManifest
            {
                PackageId = packageId,
                PackageName = "test-package",
                EntryFlow = "flow.json",
                FlowHash = flowHash,
                FieldExtensions = new RuntimeFieldExtensions
                {
                    RuntimeParameters = "field/runtime-parameters.json",
                    DefaultSiteProfile = "field/station-profile.default.json"
                }
            },
            Flow = flow,
            FlowBytes = JsonSerializer.SerializeToUtf8Bytes(flow, JsonOptions),
            RuntimeProfile = new RuntimeProfile(),
            ValidationReport = new RuntimeValidationReport { IsValid = true, FlowHash = flowHash },
            ParameterSchema = CreateSchema(packageId, flowHash, operatorId, parameterId),
            DefaultSiteProfile = new RuntimeSiteProfile
            {
                ProfileId = "package-default",
                PackageId = packageId,
                FlowHash = flowHash,
                Revision = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = "ClearVision Studio",
                Overrides = []
            }
        };
    }

    public static ProjectDto CreateResultOnlyProject()
    {
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = "old-package",
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "main",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput,
                        X = 0,
                        Y = 0
                    }
                ]
            }
        };
    }

    public static OperatorDto CreateDeepLearningOperator(
        Guid operatorId,
        string name,
        string modelPath,
        double confidence)
    {
        return new OperatorDto
        {
            Id = operatorId,
            Name = name,
            Type = OperatorType.DeepLearning,
            X = 0,
            Y = 0,
            Parameters =
            [
                CreateParameter("ModelPath", "模型路径", "file", modelPath),
                CreateParameter("Confidence", "线序检测置信度", "double", confidence, 0.5d, 0.0d, 1.0d)
            ]
        };
    }

    public static double ReadConfidence(OperatorFlowDto flow)
    {
        var value = flow.Operators.Single().Parameters.Single(parameter => parameter.Name == "Confidence").Value;
        return value switch
        {
            double number => number,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            _ => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void SafeDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static ParameterDto CreateParameter(
        string name,
        string displayName,
        string dataType,
        object? value,
        object? defaultValue = null,
        object? minValue = null,
        object? maxValue = null)
    {
        return new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = displayName,
            DataType = dataType,
            Value = value,
            DefaultValue = defaultValue,
            MinValue = minValue,
            MaxValue = maxValue,
            IsRequired = true
        };
    }
}
