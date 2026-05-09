using System.Text.Json;
using System.Text.Json.Serialization;
using Acme.Product.Application.DTOs;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Infrastructure.Operators;
using Acme.Product.Infrastructure.Services;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Acme.Product.Tests.Runtime;

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
