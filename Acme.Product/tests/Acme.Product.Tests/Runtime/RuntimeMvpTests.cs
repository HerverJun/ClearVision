using System.Text.Json;
using System.Diagnostics;
using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.Services;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Acme.Product.Tests.Runtime;

public class RuntimeMvpTests
{
    [Fact]
    public async Task PackageExporterAndLoader_ShouldRoundTripValidPackage()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("roundtrip")
            });

            Directory.Exists(export.PackageRootPath).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "package.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "flow.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "runtime-profile.json")).Should().BeTrue();
            File.Exists(Path.Combine(export.PackageRootPath, "quality", "validation-report.json")).Should().BeTrue();

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var package = await loader.LoadAsync(export.PackageRootPath);

            package.Manifest.PackageId.Should().Be(export.Manifest.PackageId);
            package.Manifest.FlowHash.Should().Be(export.Manifest.FlowHash);
            package.Flow.Operators.Should().HaveCount(1);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task PackageLoader_ShouldRejectFlowHashMismatch()
    {
        var root = CreateTempDirectory();
        try
        {
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("invalid-hash"),
                TargetRootDirectory = root
            });

            var manifestPath = Path.Combine(export.PackageRootPath, "package.json");
            var manifest = JsonSerializer.Deserialize<RuntimePackageManifest>(
                await File.ReadAllTextAsync(manifestPath),
                CreateJsonOptions())!;
            manifest.FlowHash = "sha256:deadbeef";
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, CreateJsonOptions()));

            var loader = new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance);
            var act = async () => await loader.LoadAsync(export.PackageRootPath);

            await act.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*FlowHashMismatch*");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_ShouldMatchInspectionServiceSingleRunOutcome()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreateProjectDto("consistency");
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var imageBytes = new byte[] { 2, 4, 6, 8 };
            var imagePath = Path.Combine(root, "input.png");
            await File.WriteAllBytesAsync(imagePath, imageBytes);

            var flowExecutionService = CreateFlowExecutionService(new DeterministicJudgmentExecutor());
            var inspectionService = CreateInspectionService(flowExecutionService);

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);

            var studioResult = await inspectionService.ExecuteSingleAsync(project.Id, imageBytes, project.Flow!.ToEntity());
            var stationResult = await runtimeHost.RunSingleAsync(imagePath);

            studioResult.Status.Should().Be(InspectionStatus.OK);
            stationResult.Outcome.Should().Be(RuntimeRunOutcome.Ok);
            stationResult.InspectionStatus.Should().Be(studioResult.Status);
            stationResult.PrimaryOutputs["JudgmentResult"]?.ToString().Should().Be("OK");
            stationResult.PrimaryOutputs["DecisionByte"]?.ToString().Should().Be("2");
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_RunPackageConfiguredSingleAsync_ShouldUsePackageConfiguredInputs()
    {
        var root = CreateTempDirectory();
        try
        {
            var project = CreatePackageConfiguredImageProject("package-configured");
            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = project,
                TargetRootDirectory = root
            });

            var flowExecutionService = CreateFlowExecutionService(
                new PackageConfiguredImageAcquisitionExecutor([4, 2, 1]),
                new DeterministicJudgmentExecutor());

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);

            var result = await runtimeHost.RunPackageConfiguredSingleAsync();

            result.Outcome.Should().Be(RuntimeRunOutcome.Ok);
            result.SourceImagePath.Should().BeNull();
            result.ImageId.Should().StartWith("package-configured-");
            result.PrimaryOutputs["DecisionByte"]?.ToString().Should().Be("4");
            result.SourceImageBytes.Should().BeNull();
            result.OutputImageBytes.Should().Equal([4, 2, 1]);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_StopAsync_ShouldCancelFolderReplayAndBeIdempotent()
    {
        var root = CreateTempDirectory();
        try
        {
            var replayRoot = Path.Combine(root, "replay");
            Directory.CreateDirectory(replayRoot);
            for (var index = 0; index < 5; index += 1)
            {
                await File.WriteAllBytesAsync(Path.Combine(replayRoot, $"input-{index}.png"), new byte[] { (byte)(index + 1), 1, 1 });
            }

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("stop-replay"),
                TargetRootDirectory = root
            });

            var flowExecutionService = CreateFlowExecutionService(new DeterministicJudgmentExecutor(delayMs: 250));

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.StartFolderRunAsync(replayRoot);
            await Task.Delay(100);

            var firstStop = await runtimeHost.StopAsync();
            var secondStop = await runtimeHost.StopAsync();

            firstStop.WasRunning.Should().BeTrue();
            firstStop.TimedOut.Should().BeFalse();
            secondStop.WasRunning.Should().BeFalse();
            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Loaded);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHost_StopAsync_WhenCancellationTokenIsCanceledAfterStopping_ShouldStillFinalize()
    {
        var root = CreateTempDirectory();
        try
        {
            var replayRoot = Path.Combine(root, "replay");
            Directory.CreateDirectory(replayRoot);
            await File.WriteAllBytesAsync(Path.Combine(replayRoot, "input-1.png"), new byte[] { 2, 4, 6, 8 });

            var exporter = new RuntimePackageExporter(new OperatorFactory(), NullLogger<RuntimePackageExporter>.Instance);
            var export = await exporter.ExportAsync(new RuntimePackageExportRequest
            {
                Project = CreateProjectDto("stop-cancel"),
                TargetRootDirectory = root
            });

            var profilePath = Path.Combine(export.PackageRootPath, "runtime-profile.json");
            var profile = JsonSerializer.Deserialize<RuntimeProfile>(
                await File.ReadAllTextAsync(profilePath),
                CreateJsonOptions())!;
            profile.StopTimeoutMs = 100;
            await File.WriteAllTextAsync(profilePath, JsonSerializer.Serialize(profile, CreateJsonOptions()));

            var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var flowExecutionService = CreateFlowExecutionService(new BlockingResultOutputExecutor(started, release));

            await using var runtimeHost = new RuntimeHost(
                flowExecutionService,
                new RuntimePackageLoader(new RuntimePackageValidator(), NullLogger<RuntimePackageLoader>.Instance),
                new RuntimeResultNormalizer(),
                NullLogger<RuntimeHost>.Instance);

            await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await runtimeHost.StartFolderRunAsync(replayRoot);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            var stopCts = new CancellationTokenSource();
            var stopTask = runtimeHost.StopAsync(stopCts.Token);
            await WaitForStateAsync(runtimeHost, RuntimeHostState.Stopping, TimeSpan.FromSeconds(1));
            stopCts.Cancel();

            var stopSummary = await stopTask.WaitAsync(TimeSpan.FromSeconds(2));
            stopSummary.WasRunning.Should().BeTrue();
            stopSummary.TimedOut.Should().BeTrue();
            runtimeHost.GetSnapshot().State.Should().Be(RuntimeHostState.Faulted);

            var reloadWhileOldRunIsAlive = async () => await runtimeHost.LoadPackageAsync(export.PackageRootPath);
            await reloadWhileOldRunIsAlive.Should().ThrowAsync<RuntimePackageException>()
                .WithMessage("*忙*");

            release.TrySetResult(null);
            await WaitForStateAsync(runtimeHost, RuntimeHostState.Loaded, TimeSpan.FromSeconds(2));
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void ArchitectureGuard_ShouldKeepRuntimeAndStationFreeFromDesktopWebDependencies()
    {
        var repoRoot = FindRepositoryRoot();
        var runtimeAndStationFiles = new[]
        {
            Path.Combine(repoRoot, "Acme.Product", "src", "Acme.Product.Runtime"),
            Path.Combine(repoRoot, "Acme.Product", "src", "Acme.Product.Station")
        }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();

        runtimeAndStationFiles.Should().NotBeEmpty();

        var bannedPatterns = new[]
        {
            "Microsoft.Web.WebView2",
            "wwwroot",
            "Kestrel",
            "WebApplication",
            "MapVisionApiEndpoints",
            "Acme.Product.Desktop"
        };

        foreach (var path in runtimeAndStationFiles)
        {
            var content = File.ReadAllText(path);
            foreach (var bannedPattern in bannedPatterns)
            {
                content.Should().NotContain(
                    bannedPattern,
                    $"'{bannedPattern}' must not leak into {path}");
            }
        }
    }

    private static IFlowExecutionService CreateFlowExecutionService(params IOperatorExecutor[] executors)
    {
        return new FlowExecutionService(
            executors,
            NullLogger<FlowExecutionService>.Instance,
            new VariableContext());
    }

    private static async Task WaitForStateAsync(RuntimeHost runtimeHost, RuntimeHostState state, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < timeout)
        {
            if (runtimeHost.GetSnapshot().State == state)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"RuntimeHost did not reach state {state} within {timeout}.");
    }

    private static InspectionService CreateInspectionService(IFlowExecutionService flowExecutionService)
    {
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        resultRepository.AddAsync(Arg.Any<InspectionResult>())
            .Returns(callInfo => Task.FromResult(callInfo.Arg<InspectionResult>()));

        var configurationService = Substitute.For<IConfigurationService>();
        configurationService.GetCurrent().Returns(new AppConfig
        {
            Storage = new StorageConfig
            {
                SavePolicy = "None",
                ImageSavePath = Path.GetTempPath()
            }
        });

        return new InspectionService(
            resultRepository,
            Substitute.For<IProjectRepository>(),
            flowExecutionService,
            Substitute.For<IImageAcquisitionService>(),
            configurationService,
            Substitute.For<IInspectionRuntimeCoordinator>(),
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new Acme.Product.Application.Analysis.AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);
    }

    private static ProjectDto CreateProjectDto(string name)
    {
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "runtime-flow",
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

    private static ProjectDto CreatePackageConfiguredImageProject(string name)
    {
        var acquisitionId = Guid.NewGuid();
        var acquisitionOutputPortId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var resultInputPortId = Guid.NewGuid();
        return new ProjectDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Flow = new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "package-configured-flow",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = acquisitionId,
                        Name = "PackageConfiguredImage",
                        Type = OperatorType.ImageAcquisition,
                        OutputPorts =
                        [
                            new PortDto
                            {
                                Id = acquisitionOutputPortId,
                                Name = "Image",
                                DataType = PortDataType.Image,
                                IsRequired = true
                            }
                        ]
                    },
                    new OperatorDto
                    {
                        Id = resultId,
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput,
                        InputPorts =
                        [
                            new PortDto
                            {
                                Id = resultInputPortId,
                                Name = "Image",
                                DataType = PortDataType.Image,
                                IsRequired = false
                            }
                        ]
                    }
                ],
                Connections =
                [
                    new OperatorConnectionDto
                    {
                        Id = Guid.NewGuid(),
                        SourceOperatorId = acquisitionId,
                        SourcePortId = acquisitionOutputPortId,
                        TargetOperatorId = resultId,
                        TargetPortId = resultInputPortId
                    }
                ]
            }
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionRuntimeTests", Guid.NewGuid().ToString("N"));
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Acme.Product", "Acme.Product.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class DeterministicJudgmentExecutor : IOperatorExecutor
    {
        private readonly int _delayMs;

        public DeterministicJudgmentExecutor(int delayMs = 0)
        {
            _delayMs = delayMs;
        }

        public OperatorType OperatorType => OperatorType.ResultOutput;

        public async Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            if (_delayMs > 0)
            {
                await Task.Delay(_delayMs, cancellationToken);
            }

            var imageBytes = inputs?["Image"] as byte[] ?? [];
            var decisionByte = imageBytes.Length == 0 ? 0 : imageBytes[0];
            var isOk = decisionByte % 2 == 0;

            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["JudgmentResult"] = isOk ? "OK" : "NG",
                ["DecisionByte"] = decisionByte,
                ["Image"] = imageBytes
            });
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class PackageConfiguredImageAcquisitionExecutor : IOperatorExecutor
    {
        private readonly byte[] _imageBytes;

        public PackageConfiguredImageAcquisitionExecutor(byte[] imageBytes)
        {
            _imageBytes = imageBytes;
        }

        public OperatorType OperatorType => OperatorType.ImageAcquisition;

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            if (inputs?.ContainsKey("Image") == true)
            {
                throw new InvalidOperationException("Package-configured runs must not inject an external image.");
            }

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Image"] = _imageBytes
            }));
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class BlockingResultOutputExecutor : IOperatorExecutor
    {
        private readonly TaskCompletionSource<object?> _started;
        private readonly TaskCompletionSource<object?> _release;

        public BlockingResultOutputExecutor(TaskCompletionSource<object?> started, TaskCompletionSource<object?> release)
        {
            _started = started;
            _release = release;
        }

        public OperatorType OperatorType => OperatorType.ResultOutput;

        public async Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult(null);
            await _release.Task.ConfigureAwait(false);
            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["JudgmentResult"] = "OK",
                ["Image"] = new byte[] { 2, 4, 6, 8 }
            });
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }
}
