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

    [Fact]
    public async Task ExportAsync_ShouldExposeBoundedTraditionalVisionRuntimeParameters()
    {
        var root = RuntimeParameterTestData.CreateTempDirectory("ClearVisionTraditionalRuntimeParameterExporterTests");
        try
        {
            var templateOperatorId = Guid.NewGuid();
            var blobOperatorId = Guid.NewGuid();
            var exporter = new RuntimePackageExporter(
                new Acme.Product.Infrastructure.Services.OperatorFactory(),
                NullLogger<RuntimePackageExporter>.Instance);

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                TargetRootDirectory = root,
                Project = new ProjectDto
                {
                    Id = Guid.NewGuid(),
                    Name = "traditional-runtime-parameters",
                    Flow = new OperatorFlowDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "main",
                        Operators =
                        [
                            new OperatorDto
                            {
                                Id = templateOperatorId,
                                Name = "TemplateMatch",
                                Type = OperatorType.TemplateMatching,
                                Parameters =
                                [
                                    new ParameterDto
                                    {
                                        Id = Guid.NewGuid(),
                                        Name = "threshold",
                                        DisplayName = "Match threshold",
                                        DataType = "double",
                                        Value = 0.82d,
                                        DefaultValue = 0.8d,
                                        MinValue = 0.0d,
                                        MaxValue = 1.0d,
                                        IsRequired = true
                                    }
                                ]
                            },
                            new OperatorDto
                            {
                                Id = blobOperatorId,
                                Name = "BlobFilter",
                                Type = OperatorType.BlobAnalysis,
                                Parameters =
                                [
                                    new ParameterDto
                                    {
                                        Id = Guid.NewGuid(),
                                        Name = "maxArea",
                                        DisplayName = "Max area",
                                        DataType = "int",
                                        Value = 100_000,
                                        DefaultValue = 100_000,
                                        MinValue = 0,
                                        IsRequired = true
                                    }
                                ]
                            }
                        ]
                    }
                }
            });

            var schema = JsonSerializer.Deserialize<RuntimeParameterSchema>(
                await File.ReadAllTextAsync(Path.Combine(export.PackageRootPath, "field", "runtime-parameters.json")),
                RuntimeParameterTestData.JsonOptions)!;

            var threshold = schema.Parameters.Should()
                .ContainSingle(parameter => parameter.OperatorId == templateOperatorId && parameter.ParameterName == "threshold")
                .Subject;
            threshold.OperatorType.Should().Be(nameof(OperatorType.TemplateMatching));
            threshold.DefaultValue.GetDouble().Should().Be(0.82d);
            threshold.Min.Should().Be(0.0d);
            threshold.Max.Should().Be(1.0d);
            threshold.Step.Should().Be(0.01d);
            threshold.RequiresInteger.Should().BeFalse();

            var maxArea = schema.Parameters.Should()
                .ContainSingle(parameter => parameter.OperatorId == blobOperatorId && parameter.ParameterName == "maxArea")
                .Subject;
            maxArea.OperatorType.Should().Be(nameof(OperatorType.BlobAnalysis));
            maxArea.DefaultValue.GetDouble().Should().Be(100_000d);
            maxArea.Min.Should().Be(0.0d);
            maxArea.Max.Should().Be(1_000_000d);
            maxArea.Step.Should().Be(1.0d);
            maxArea.RequiresInteger.Should().BeTrue();
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

    [Fact]
    public void Validate_ShouldRejectFractionalOverrideForIntegerParameters()
    {
        var operatorId = Guid.NewGuid();
        var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
        var schema = RuntimeParameterTestData.CreateSchema("pkg-1", "sha256:abc", operatorId, parameterId);
        schema.Parameters.Single().RequiresInteger = true;

        var validation = RuntimeParameterValidator.Validate(
            schema,
            RuntimeParameterTestData.CreateProfile(
                "pkg-1",
                "sha256:abc",
                RuntimeParameterTestData.CreateOverride(parameterId, 0.7d)));

        validation.Errors.Should().Contain(error => error.Contains("must be an integer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldReportDuplicateDefinitionsAndBlankOverrideIds()
    {
        var operatorId = Guid.NewGuid();
        var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
        var schema = RuntimeParameterTestData.CreateSchema("pkg-1", "sha256:abc", operatorId, parameterId);
        schema.Parameters.Add(new RuntimeParameterDefinition
        {
            Id = parameterId,
            OperatorId = Guid.NewGuid(),
            OperatorName = "duplicate",
            OperatorType = nameof(OperatorType.DeepLearning),
            ParameterName = "Confidence"
        });

        var profile = RuntimeParameterTestData.CreateProfile(
            "pkg-1",
            "sha256:abc",
            new RuntimeParameterOverride
            {
                ParameterId = " ",
                Value = JsonSerializer.SerializeToElement(0.7d)
            });

        var validation = RuntimeParameterValidator.Validate(schema, profile);

        validation.Errors.Should().Contain(error => error.Contains("Duplicate runtime parameter definition", StringComparison.OrdinalIgnoreCase));
        validation.Errors.Should().Contain(error => error.Contains("override id", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void Store_ShouldExportAndImportProfileForMatchingPackage()
    {
        var sourceRoot = RuntimeParameterTestData.CreateTempDirectory("ClearVisionStationSiteProfileStoreExportSource");
        var targetRoot = RuntimeParameterTestData.CreateTempDirectory("ClearVisionStationSiteProfileStoreExportTarget");
        try
        {
            var operatorId = Guid.NewGuid();
            var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
            var package = RuntimeParameterTestData.CreateRuntimePackage(operatorId, parameterId, rootPath: sourceRoot);
            var sourceStore = new StationSiteProfileStore(sourceRoot);
            var targetStore = new StationSiteProfileStore(targetRoot);

            var profile = sourceStore.LoadOrCreate(package);
            profile.Overrides =
            [
                RuntimeParameterTestData.CreateOverride(parameterId, 0.73d)
            ];

            var saved = sourceStore.Save(package, profile);
            var exportPath = Path.Combine(sourceRoot, "export", sourceStore.GetSuggestedExportFileName(package));

            sourceStore.ExportToFile(package, saved, exportPath);

            File.Exists(exportPath).Should().BeTrue();
            var exported = JsonSerializer.Deserialize<RuntimeSiteProfile>(File.ReadAllText(exportPath), RuntimeParameterTestData.JsonOptions);
            exported.Should().NotBeNull();
            exported!.PackageId.Should().Be(package.Manifest.PackageId);
            exported.FlowHash.Should().Be(package.Manifest.FlowHash);
            exported.Overrides.Should().ContainSingle();
            exported.Overrides.Single().Value.GetDouble().Should().Be(0.73d);

            var imported = targetStore.ImportFromFile(package, exportPath);
            imported.ProfileId.Should().Be("local-site");
            imported.Revision.Should().Be(1);
            imported.Overrides.Should().ContainSingle();
            imported.Overrides.Single().Value.GetDouble().Should().Be(0.73d);
            imported.UpdatedBy.Should().Be("local-engineer");

            var reloaded = targetStore.LoadOrCreate(package);
            reloaded.Revision.Should().Be(1);
            reloaded.Overrides.Should().ContainSingle();
            reloaded.Overrides.Single().Value.GetDouble().Should().Be(0.73d);
        }
        finally
        {
            RuntimeParameterTestData.SafeDeleteDirectory(sourceRoot);
            RuntimeParameterTestData.SafeDeleteDirectory(targetRoot);
        }
    }

    [Fact]
    public void Import_ShouldRejectProfileWhenPackageBindingDoesNotMatch()
    {
        var root = RuntimeParameterTestData.CreateTempDirectory("ClearVisionStationSiteProfileStoreMismatch");
        try
        {
            var operatorId = Guid.NewGuid();
            var parameterId = RuntimeParameterTestData.ParameterId(operatorId);
            var package = RuntimeParameterTestData.CreateRuntimePackage(operatorId, parameterId, rootPath: root);
            var store = new StationSiteProfileStore(root);
            var mismatchedProfile = RuntimeParameterTestData.CreateProfile(
                "pkg-other",
                package.Manifest.FlowHash,
                RuntimeParameterTestData.CreateOverride(parameterId, 0.66d));
            var json = JsonSerializer.Serialize(mismatchedProfile, RuntimeParameterTestData.JsonOptions);

            Action act = () => store.Import(package, json);

            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*不匹配*");
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
