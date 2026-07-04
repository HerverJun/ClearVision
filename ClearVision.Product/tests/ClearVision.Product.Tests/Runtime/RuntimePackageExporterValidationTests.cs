using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using ClearVision.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Runtime;

public class RuntimePackageExporterValidationTests
{
    [Fact]
    public async Task ExportAsync_ShouldAllowConditionallyOptionalParametersAndIgnoreNonPathFlags()
    {
        var root = CreateTempDirectory();
        try
        {
            var imagePath = Path.Combine(root, "sample.png");
            var modelPath = Path.Combine(root, "sample.onnx");
            await File.WriteAllBytesAsync(imagePath, [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(modelPath, [5, 6, 7, 8]);

            var exporter = CreateExporter();
            var project = new ProjectDto
            {
                Id = Guid.NewGuid(),
                Name = "conditional-optional",
                Flow = new OperatorFlowDto
                {
                    Id = Guid.NewGuid(),
                    Name = "main",
                    Operators =
                    [
                        CreateOperatorDto(
                            "Image acquisition",
                            OperatorType.ImageAcquisition,
                            CreateParameter("SourceType", "enum", "File"),
                            CreateParameter("FilePath", "file", imagePath),
                            CreateParameter("CameraId", "cameraBinding", string.Empty)),
                        CreateOperatorDto(
                            "Wire detection",
                            OperatorType.DeepLearning,
                            CreateParameter("ModelPath", "file", modelPath),
                            CreateParameter("Confidence", "double", 0.5),
                            CreateParameter("ModelVersion", "enum", "Auto"),
                            CreateParameter("LabelsPath", "file", string.Empty),
                            CreateParameter("TargetClasses", "string", string.Empty),
                            CreateParameter("ModelId", "string", string.Empty),
                            CreateParameter("ModelCatalogPath", "file", string.Empty)),
                        CreateOperatorDto(
                            "ROI box filter",
                            OperatorType.BoxFilter,
                            CreateParameter("FilterMode", "enum", "Class"),
                            CreateParameter("TargetClasses", "string", string.Empty)),
                        CreateOperatorDto(
                            "Sequence judge",
                            OperatorType.DetectionSequenceJudge,
                            CreateParameter("SortBy", "enum", "CenterX"),
                            CreateParameter("Direction", "enum", "Ascending"),
                            CreateParameter("GroupingMode", "enum", "SingleRow"),
                            CreateParameter("ExpectedSlots", "string", string.Empty),
                            CreateParameter("PerspectiveSrcPointsJson", "string", string.Empty),
                            CreateParameter("PerspectiveDstPointsJson", "string", string.Empty)),
                        CreateOperatorDto(
                            "Result output",
                            OperatorType.ResultOutput,
                            CreateParameter("Format", "enum", "JSON"),
                            CreateParameter("SaveToFile", "bool", false))
                    ]
                }
            };

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            export.Manifest.PendingParameters.Should().BeEmpty();
            export.Manifest.MissingResources.Should().BeEmpty();
            Directory.Exists(export.PackageRootPath).Should().BeTrue();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_ShouldAllowRuntimeSuppliedImageWhenFileModeHasNoFilePath()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = CreateExporter();
            var project = new ProjectDto
            {
                Id = Guid.NewGuid(),
                Name = "missing-file-path",
                Flow = new OperatorFlowDto
                {
                    Id = Guid.NewGuid(),
                    Name = "main",
                    Operators =
                    [
                        CreateOperatorDto(
                            "Image acquisition",
                            OperatorType.ImageAcquisition,
                            CreateParameter("SourceType", "enum", "File"),
                            CreateParameter("FilePath", "file", string.Empty),
                            CreateParameter("CameraId", "cameraBinding", string.Empty))
                    ]
                }
            };

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            export.Manifest.PendingParameters.Should().BeEmpty();
            export.Manifest.MissingResources.Should().BeEmpty();
            Directory.Exists(export.PackageRootPath).Should().BeTrue();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_ShouldBundleModelResourcesAndLoaderShouldRebaseThemToPackageRoot()
    {
        var root = CreateTempDirectory();
        try
        {
            var modelPath = Path.Combine(root, "controller.onnx");
            var labelsPath = Path.Combine(root, "labels.txt");
            await File.WriteAllBytesAsync(modelPath, [1, 2, 3, 4, 5]);
            await File.WriteAllTextAsync(labelsPath, "Wire_Black\nWire_Blue\n");

            var exporter = CreateExporter();
            var project = new ProjectDto
            {
                Id = Guid.NewGuid(),
                Name = "portable-model",
                Flow = new OperatorFlowDto
                {
                    Id = Guid.NewGuid(),
                    Name = "main",
                    Operators =
                    [
                        CreateOperatorDto(
                            "Wire detection",
                            OperatorType.DeepLearning,
                            CreateParameter("ModelPath", "file", modelPath),
                            CreateParameter("LabelsPath", "file", string.Empty),
                            CreateParameter("Confidence", "double", 0.5),
                            CreateParameter("ModelVersion", "enum", "Auto"),
                            CreateParameter("TargetClasses", "string", "Wire_Black,Wire_Blue"),
                            CreateParameter("ModelId", "string", string.Empty),
                            CreateParameter("ModelCatalogPath", "file", string.Empty))
                    ]
                }
            };

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var flowText = await File.ReadAllTextAsync(Path.Combine(export.PackageRootPath, "flow.json"));
            flowText.Should().NotContain(modelPath);

            var exportedFlow = JsonSerializer.Deserialize<OperatorFlowDto>(flowText, CreateJsonOptions())!;
            var exportedDeepLearning = exportedFlow.Operators.Single();
            var modelRelativePath = ReadParameter(exportedDeepLearning, "ModelPath");
            var labelsRelativePath = ReadParameter(exportedDeepLearning, "LabelsPath");

            Path.IsPathFullyQualified(modelRelativePath).Should().BeFalse();
            modelRelativePath.Replace('\\', '/').Should().StartWith("assets/resources/");
            File.Exists(Path.Combine(export.PackageRootPath, modelRelativePath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();

            labelsRelativePath.Should().NotBeNullOrWhiteSpace();
            Path.IsPathFullyQualified(labelsRelativePath).Should().BeFalse();
            File.Exists(Path.Combine(export.PackageRootPath, labelsRelativePath.Replace('/', Path.DirectorySeparatorChar))).Should().BeTrue();

            var modelAssetsText = await File.ReadAllTextAsync(Path.Combine(export.PackageRootPath, "field", "model-assets.json"));
            modelAssetsText.Should().Contain("ModelPath");
            modelAssetsText.Should().Contain("LabelsPath");
            modelAssetsText.Should().NotContain(modelPath);

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);
            var loadedDeepLearning = package.Flow.Operators.Single();
            var loadedModelPath = ReadParameter(loadedDeepLearning, "ModelPath");
            var loadedLabelsPath = ReadParameter(loadedDeepLearning, "LabelsPath");

            Path.IsPathFullyQualified(loadedModelPath).Should().BeTrue();
            loadedModelPath.Should().StartWith(export.PackageRootPath);
            File.Exists(loadedModelPath).Should().BeTrue();

            Path.IsPathFullyQualified(loadedLabelsPath).Should().BeTrue();
            loadedLabelsPath.Should().StartWith(export.PackageRootPath);
            File.Exists(loadedLabelsPath).Should().BeTrue();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadAsync_ShouldRejectAbsoluteEntryFlowPath()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = CreateExporter();
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = new ProjectDto
                {
                    Id = Guid.NewGuid(),
                    Name = "absolute-entry-flow",
                    Flow = new OperatorFlowDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "main",
                        Operators =
                        [
                            CreateOperatorDto(
                                "Result output",
                                OperatorType.ResultOutput,
                                CreateParameter("Format", "enum", "JSON"))
                        ]
                    }
                },
                TargetRootDirectory = root
            });

            var manifestPath = Path.Combine(export.PackageRootPath, "package.json");
            var manifest = JsonSerializer.Deserialize<RuntimePackageManifest>(
                await File.ReadAllTextAsync(manifestPath),
                CreateJsonOptions())!;
            manifest.EntryFlow = Path.Combine(export.PackageRootPath, "flow.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, CreateJsonOptions()));

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var act = () => loader.LoadAsync(export.PackageRootPath);

            await act.Should()
                .ThrowAsync<RuntimePackageException>()
                .WithMessage("*relative*");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_ShouldRejectTargetRootOutsideControlledDirectories()
    {
        var exporter = CreateExporter();
        var project = CreateMinimalProject("reject-root");
        var root = Path.GetPathRoot(Environment.CurrentDirectory)
            ?? Environment.CurrentDirectory;

        var act = () => exporter.ExportAsync(new RuntimePackageExportRequest
        {
            Project = project,
            TargetRootDirectory = root
        });

        await act.Should()
            .ThrowAsync<RuntimePackageException>()
            .WithMessage("*outside the controlled export directories*");
    }

    [Fact]
    public async Task ExportAsync_ShouldAllowPublishCheckTargetRoot()
    {
        var root = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            ".tmp",
            "publish-check",
            "runtime-exporter-tests",
            Guid.NewGuid().ToString("N")));

        try
        {
            var exporter = CreateExporter();
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateMinimalProject("publish-check-root"),
                TargetRootDirectory = root
            });

            export.PackageRootPath.Should().StartWith(root);
            Directory.Exists(export.PackageRootPath).Should().BeTrue();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenProjectGlobalVariableGraphHasCycle_ShouldRejectBeforeCreatingPackageDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = CreateExporter();
            var project = CreateProjectWithGlobalVariableCycle("global-variable-cycle");

            var act = () => exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await act.Should()
                .ThrowAsync<RuntimePackageException>()
                .WithMessage("*GV024*");
            Directory.EnumerateDirectories(root).Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenProjectHasCalibrationAuthorityAsset_ShouldPackageRelativeAssetWithHashes()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectWithCalibrationAsset("project-assets", "calibration-main", projectRevision: 12);
            var metadata = CreateAssetMetadata(project);
            var exporter = CreateExporter();

            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root,
                ProjectAssetStorageMetadata = metadata,
                RequireProjectAssetStorageMetadata = true
            });

            export.Manifest.Assets.Should().NotBeNull();
            export.Manifest.FieldExtensions.ProjectAssets.Should().Be("assets");
            var manifestAsset = export.Manifest.Assets!.CalibrationAssets.Should().ContainSingle().Subject;
            manifestAsset.AssetId.Should().Be("calibration-main");
            manifestAsset.Kind.Should().Be("CalibrationBundleV2");
            manifestAsset.ProjectRevision.Should().Be(12);
            manifestAsset.Required.Should().BeFalse();
            Path.IsPathFullyQualified(manifestAsset.RelativePath).Should().BeFalse();
            manifestAsset.RelativePath.Should().StartWith("assets/calibration/");
            manifestAsset.RelativePath.Should().NotContain("..");
            manifestAsset.RelativePath.Should().NotContain("\\");
            manifestAsset.ContentHash.Should().Be(project.Assets.CalibrationAssets.Single().ContentHash);

            var assetPath = Path.Combine(export.PackageRootPath, manifestAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Exists(assetPath).Should().BeTrue();
            RuntimePathGuard.ComputeSha256(await File.ReadAllBytesAsync(assetPath)).Should().Be(manifestAsset.FileHash);

            var packagedAsset = JsonSerializer.Deserialize<ProjectCalibrationAssetDto>(
                await File.ReadAllTextAsync(assetPath),
                ProjectAssetJson.Options)!;
            packagedAsset.AssetId.Should().Be("calibration-main");
            packagedAsset.ProjectRevision.Should().Be(12);
            packagedAsset.Status.Should().Be("authority");
            packagedAsset.ContentHash.Should().Be(manifestAsset.ContentHash);
            ProjectAssetJson.ComputePayloadHash(packagedAsset.Payload).Should().Be(manifestAsset.ContentHash);

            var manifestText = await File.ReadAllTextAsync(Path.Combine(export.PackageRootPath, "package.json"));
            manifestText.Should().Contain("\"assets\"");
            manifestText.Should().NotContain(root.Replace("\\", "\\\\"));

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);
            package.Manifest.Assets!.CalibrationAssets.Should().ContainSingle();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenManifestOmitsAssetsSection_ShouldRemainCompatible()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = CreateExporter();
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateMinimalProject("old-no-assets"),
                TargetRootDirectory = root
            });

            var manifestText = await File.ReadAllTextAsync(Path.Combine(export.PackageRootPath, "package.json"));
            manifestText.Should().NotContain("\"assets\"");

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);

            package.Manifest.Assets.Should().BeNull();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadAsync_WhenAssetsSectionIsEmpty_ShouldRemainCompatible()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = CreateExporter();
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateMinimalProject("empty-assets"),
                TargetRootDirectory = root
            });
            var manifestPath = Path.Combine(export.PackageRootPath, "package.json");
            var manifest = await ReadManifestAsync(manifestPath);
            manifest.Assets = new RuntimePackageAssets();
            await WriteManifestAsync(manifestPath, manifest);

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);

            package.Manifest.Assets.Should().NotBeNull();
            package.Manifest.Assets!.CalibrationAssets.Should().BeEmpty();
            package.Manifest.Assets.SpatialAssets.Should().BeEmpty();
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("checksum")]
    [InlineData("traversal")]
    [InlineData("absolute")]
    [InlineData("malformed")]
    [InlineData("payloadHash")]
    [InlineData("schemaVersion")]
    public async Task LoadAsync_WhenProjectAssetPackageIsTampered_ShouldFailClosed(string scenario)
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectWithCalibrationAsset("tamper-assets", "asset-tamper", projectRevision: 7);
            var exporter = CreateExporter();
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var manifestPath = Path.Combine(export.PackageRootPath, "package.json");
            var manifest = await ReadManifestAsync(manifestPath);
            var manifestAsset = manifest.Assets!.CalibrationAssets.Single();
            var assetPath = Path.Combine(export.PackageRootPath, manifestAsset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var expectedCode = scenario switch
            {
                "missing" => "ProjectAssetFileMissing",
                "checksum" => "ProjectAssetFileHashMismatch",
                "traversal" => "ProjectAssetPathInvalid",
                "absolute" => "ProjectAssetPathInvalid",
                "malformed" => "ProjectAssetJsonMalformed",
                "payloadHash" => "ProjectAssetContentHashMismatch",
                "schemaVersion" => "ProjectAssetsSchemaVersionUnsupported",
                _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
            };

            switch (scenario)
            {
                case "missing":
                    File.Delete(assetPath);
                    break;
                case "checksum":
                    await File.WriteAllTextAsync(assetPath, "{}");
                    break;
                case "traversal":
                    manifestAsset.RelativePath = "assets/../calibration.json";
                    await WriteManifestAsync(manifestPath, manifest);
                    break;
                case "absolute":
                    manifestAsset.RelativePath = Path.Combine(export.PackageRootPath, "assets", "calibration.json");
                    await WriteManifestAsync(manifestPath, manifest);
                    break;
                case "malformed":
                    await File.WriteAllTextAsync(assetPath, "{");
                    manifestAsset.FileHash = RuntimePathGuard.ComputeSha256(await File.ReadAllBytesAsync(assetPath));
                    await WriteManifestAsync(manifestPath, manifest);
                    break;
                case "payloadHash":
                    var asset = JsonSerializer.Deserialize<ProjectCalibrationAssetDto>(
                        await File.ReadAllTextAsync(assetPath),
                        ProjectAssetJson.Options)!;
                    asset.Payload = CreateCalibrationPayload("mutated-payload");
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(asset, ProjectAssetJson.Options);
                    await File.WriteAllBytesAsync(assetPath, bytes);
                    manifestAsset.FileHash = RuntimePathGuard.ComputeSha256(bytes);
                    await WriteManifestAsync(manifestPath, manifest);
                    break;
                case "schemaVersion":
                    manifest.Assets!.SchemaVersion = 99;
                    await WriteManifestAsync(manifestPath, manifest);
                    break;
            }

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var act = () => loader.LoadAsync(export.PackageRootPath);

            await act.Should()
                .ThrowAsync<RuntimePackageException>()
                .WithMessage($"*{expectedCode}*");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenProjectAssetStorageMetadataMismatches_ShouldRejectBeforeCreatingPackageDirectory()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectWithCalibrationAsset("metadata-mismatch", "asset-mismatch", projectRevision: 5);
            var metadata = CreateAssetMetadata(project) with { PersistenceRevision = 4 };
            var exporter = CreateExporter();

            var act = () => exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root,
                ProjectAssetStorageMetadata = metadata,
                RequireProjectAssetStorageMetadata = true
            });

            await act.Should()
                .ThrowAsync<RuntimePackageException>()
                .WithMessage("*RPA003*");
            Directory.EnumerateDirectories(root).Should().BeEmpty();
            project.Assets.CalibrationAssets.Should().ContainSingle(asset =>
                asset.AssetId == "asset-mismatch" &&
                asset.ProjectRevision == 5 &&
                asset.Status == "authority");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExportAsync_WhenAuthorityAssetPayloadHashMismatches_ShouldRejectWithoutPollutingProjectAuthority()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectWithCalibrationAsset("bad-asset", "asset-bad", projectRevision: 3);
            var originalHash = project.Assets.CalibrationAssets.Single().ContentHash;
            project.Assets.CalibrationAssets.Single().ContentHash = "sha256:" + new string('0', 64);
            var exporter = CreateExporter();

            var act = () => exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            await act.Should()
                .ThrowAsync<RuntimePackageException>()
                .WithMessage("*RPA010*");
            Directory.EnumerateDirectories(root).Should().BeEmpty();
            ProjectAssetJson.ComputePayloadHash(project.Assets.CalibrationAssets.Single().Payload).Should().Be(originalHash);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static RuntimePackageExporter CreateExporter()
    {
        var cameraManager = Substitute.For<ICameraManager>();
        var executors = new IOperatorExecutor[]
        {
            new ImageAcquisitionOperator(NullLogger<ImageAcquisitionOperator>.Instance, cameraManager),
            new DeepLearningOperator(NullLogger<DeepLearningOperator>.Instance),
            new BoundingBoxFilterOperator(NullLogger<BoundingBoxFilterOperator>.Instance),
            new DetectionSequenceJudgeOperator(NullLogger<DetectionSequenceJudgeOperator>.Instance),
            new ResultOutputOperator(NullLogger<ResultOutputOperator>.Instance)
        };

        return new RuntimePackageExporter(
            new OperatorFactory(),
            NullLogger<RuntimePackageExporter>.Instance,
            executors);
    }

    private static ProjectDto CreateMinimalProject(string name)
    {
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "main",
                Operators =
                [
                    CreateOperatorDto(
                        "Result output",
                        OperatorType.ResultOutput,
                        CreateParameter("Format", "enum", "JSON"))
                ]
            }
        };
    }

    private static ProjectDto CreateProjectWithCalibrationAsset(
        string name,
        string assetId,
        long projectRevision)
    {
        var payload = CreateCalibrationPayload(assetId);
        var contentHash = ProjectAssetJson.ComputePayloadHash(payload);
        var project = CreateMinimalProject(name);
        project.PersistenceRevision = projectRevision;
        project.Assets = new ProjectAssetsDto
        {
            CalibrationAssets =
            [
                new ProjectCalibrationAssetDto
                {
                    AssetId = assetId,
                    Kind = "CalibrationBundleV2",
                    Version = "2.0",
                    Producer = "test",
                    SourceDraftSessionId = "draft-session",
                    ImageIdentity = "image:test",
                    ContentHash = contentHash,
                    ProjectRevision = projectRevision,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Status = "authority",
                    Payload = payload
                }
            ]
        };

        return project;
    }

    private static JsonElement CreateCalibrationPayload(string bundleId) =>
        JsonSerializer.SerializeToElement(
            new
            {
                schemaVersion = 2,
                bundleId,
                calibrationVersion = "2.0",
                quality = new
                {
                    accepted = true,
                    rmsErrorPx = 0.12
                },
                transform2D = new
                {
                    kind = "Affine",
                    matrix = new[] { 1d, 0d, 0d, 0d, 1d, 0d }
                }
            },
            ProjectAssetJson.Options);

    private static ProjectAssetStorageMetadata CreateAssetMetadata(ProjectDto project) =>
        new(
            SchemaVersion: 1,
            ProjectId: project.Id,
            PersistenceRevision: project.PersistenceRevision,
            AssetsHash: ProjectAssetJson.ComputeAssetsHash(project.Assets),
            SaveId: Guid.NewGuid(),
            SavedAtUtc: DateTimeOffset.UtcNow);

    private static async Task<RuntimePackageManifest> ReadManifestAsync(string manifestPath) =>
        JsonSerializer.Deserialize<RuntimePackageManifest>(
            await File.ReadAllTextAsync(manifestPath),
            CreateJsonOptions())!;

    private static async Task WriteManifestAsync(string manifestPath, RuntimePackageManifest manifest) =>
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, CreateJsonOptions()));

    private static ProjectDto CreateProjectWithGlobalVariableCycle(string name)
    {
        var variableA = Guid.NewGuid();
        var variableB = Guid.NewGuid();
        var portA = Guid.NewGuid();
        var portB = Guid.NewGuid();
        var paramA = Guid.NewGuid();
        var paramB = Guid.NewGuid();
        var operatorA = CreateOperatorDto("OperatorA", OperatorType.ResultJudgment,
            CreateParameter("InA", "int", 0));
        operatorA.Parameters[0].Id = paramA;
        operatorA.OutputPorts.Add(CreatePort(portA, "OutA", PortDirection.Output, PortDataType.Integer));

        var operatorB = CreateOperatorDto("OperatorB", OperatorType.ResultJudgment,
            CreateParameter("InB", "int", 0));
        operatorB.Parameters[0].Id = paramB;
        operatorB.OutputPorts.Add(CreatePort(portB, "OutB", PortDirection.Output, PortDataType.Integer));

        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "main",
                Operators = [operatorA, operatorB],
                Connections = []
            },
            GlobalVariables = new ProjectGlobalVariableSchema
            {
                Variables =
                [
                    CreateGlobalVariable(variableA, "stats.a"),
                    CreateGlobalVariable(variableB, "stats.b")
                ],
                SourceBindings =
                [
                    new ProjectGlobalVariableSourceBinding
                    {
                        Id = Guid.NewGuid(),
                        VariableId = variableA,
                        OperatorId = operatorA.Id,
                        OutputPortId = portA,
                        OperatorName = operatorA.Name,
                        OutputPortName = "OutA"
                    },
                    new ProjectGlobalVariableSourceBinding
                    {
                        Id = Guid.NewGuid(),
                        VariableId = variableB,
                        OperatorId = operatorB.Id,
                        OutputPortId = portB,
                        OperatorName = operatorB.Name,
                        OutputPortName = "OutB"
                    }
                ],
                TargetBindings =
                [
                    new ProjectGlobalVariableTargetBinding
                    {
                        Id = Guid.NewGuid(),
                        VariableId = variableA,
                        OperatorId = operatorB.Id,
                        ParameterId = paramB,
                        OperatorName = operatorB.Name,
                        ParameterName = "InB"
                    },
                    new ProjectGlobalVariableTargetBinding
                    {
                        Id = Guid.NewGuid(),
                        VariableId = variableB,
                        OperatorId = operatorA.Id,
                        ParameterId = paramA,
                        OperatorName = operatorA.Name,
                        ParameterName = "InA"
                    }
                ]
            }
        };
    }

    private static ProjectGlobalVariableDefinition CreateGlobalVariable(Guid id, string name)
    {
        return new ProjectGlobalVariableDefinition
        {
            Id = id,
            Name = name,
            DisplayName = name,
            ValueType = ProjectGlobalVariableValueType.Int64,
            InitialValue = JsonSerializer.SerializeToElement(0L),
            ManualWriteAllowed = true
        };
    }

    private static OperatorDto CreateOperatorDto(string name, OperatorType type, params ParameterDto[] parameters)
    {
        return new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            X = 0,
            Y = 0,
            Parameters = parameters.ToList(),
            ExecutionStatus = OperatorExecutionStatus.NotExecuted
        };
    }

    private static PortDto CreatePort(Guid id, string name, PortDirection direction, PortDataType dataType)
    {
        return new PortDto
        {
            Id = id,
            Name = name,
            Direction = direction,
            DataType = dataType,
            IsRequired = direction == PortDirection.Input
        };
    }

    private static ParameterDto CreateParameter(string name, string dataType, object? value)
    {
        return new ParameterDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DisplayName = name,
            DataType = dataType,
            Value = value
        };
    }

    private static string ReadParameter(OperatorDto op, string parameterName)
    {
        return op.Parameters
            .Single(parameter => parameter.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
            .Value
            ?.ToString()
            ?? string.Empty;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter()
            }
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionRuntimeExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
