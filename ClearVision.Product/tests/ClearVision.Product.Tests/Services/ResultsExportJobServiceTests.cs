using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

public sealed class ResultsExportJobServiceTests
{
    [Fact]
    public async Task CreateCsvExport_ShouldCompleteAndReplayByClientOperationId()
    {
        var projectId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("结果导出工程"));
        var analysisService = Substitute.For<IResultAnalysisService>();
        analysisService.ExportToCsvAsync(
                projectId,
                null,
                Arg.Any<DateTime>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns("检测ID,工程ID\nresult-1," + projectId.ToString("D"));

        await using var provider = CreateProvider(projectRepository, analysisService);
        using var service = CreateService(provider);
        var request = new ResultsExportRequest(
            projectId,
            "local",
            ResultsExportFormat.Csv,
            null,
            null,
            null,
            null,
            null,
            operationId);

        var first = await service.CreateAsync(request);
        var completed = await WaitForStateAsync(service, first.Job.ExportId, ResultsExportJobState.Completed);
        var replay = await service.CreateAsync(request);

        completed.DownloadAvailable.Should().BeTrue();
        replay.OperationReplayed.Should().BeTrue();
        replay.Job.ExportId.Should().Be(first.Job.ExportId);
        service.FindByClientOperationId(operationId)?.ExportId.Should().Be(first.Job.ExportId);
        service.TryReadArtifact(first.Job.ExportId, out var artifact).Should().BeTrue();
        artifact.Should().NotBeNull();
        artifact!.ContentType.Should().Be("text/csv");
        artifact.FileName.Should().EndWith(".csv");
        artifact.Bytes.Should().Contain((byte)'r');
        artifact.Sha256.Should().HaveLength(64);
        await analysisService.Received(1).ExportToCsvAsync(
            projectId,
            null,
            Arg.Any<DateTime>(),
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateJsonExport_ShouldUseJsonFormatAndStableUpperBound()
    {
        var projectId = Guid.NewGuid();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("结果导出工程"));
        var analysisService = Substitute.For<IResultAnalysisService>();
        analysisService.ExportToJsonAsync(
                projectId,
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                "NG",
                "Scratch",
                Arg.Any<CancellationToken>())
            .Returns("{\"results\":[]}");

        await using var provider = CreateProvider(projectRepository, analysisService);
        using var service = CreateService(provider);
        var requestedEnd = DateTime.UtcNow.AddHours(1);
        var request = new ResultsExportRequest(
            projectId,
            "local",
            ResultsExportFormat.Json,
            DateTime.UtcNow.AddHours(-1),
            requestedEnd,
            "NG",
            "Scratch",
            null,
            Guid.NewGuid());

        var started = await service.CreateAsync(request);
        var completed = await WaitForStateAsync(service, started.Job.ExportId, ResultsExportJobState.Completed);

        completed.SnapshotUpperBoundUtc.Should().NotBeNull();
        completed.SnapshotUpperBoundUtc.Should().BeOnOrBefore(requestedEnd);
        completed.FileName.Should().EndWith(".json");
        service.TryReadArtifact(started.Job.ExportId, out var artifact).Should().BeTrue();
        artifact!.ContentType.Should().Be("application/json");
        await analysisService.Received(1).ExportToJsonAsync(
            projectId,
            Arg.Any<DateTime>(),
            Arg.Is<DateTime>(value => value <= requestedEnd),
            "NG",
            "Scratch",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateExport_ShouldRejectMissingProjectAndDateRangeBeforeCreatingJob()
    {
        var projectId = Guid.NewGuid();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns((Project?)null);
        var analysisService = Substitute.For<IResultAnalysisService>();

        await using var provider = CreateProvider(projectRepository, analysisService);
        using var service = CreateService(provider);

        var missingProject = new ResultsExportRequest(
            projectId,
            "local",
            ResultsExportFormat.Csv,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid());
        var missingProjectAction = () => service.CreateAsync(missingProject);
        await missingProjectAction.Should().ThrowAsync<ResultsExportProjectNotFoundException>();

        var reversed = missingProject with
        {
            ProjectId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(-1)
        };
        var reversedAction = () => service.CreateAsync(reversed);
        await reversedAction.Should().ThrowAsync<ResultsExportValidationException>()
            .Where(error => error.Code == "RESULTS_EXPORT_DATE_RANGE_INVALID");
        service.Get(reversed.ClientOperationId).Should().BeNull();
        await analysisService.DidNotReceiveWithAnyArgs().ExportToCsvAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task CreateExport_ShouldRejectUnsupportedStationSourceAndIdentityConflict()
    {
        var projectId = Guid.NewGuid();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("结果导出工程"));
        var analysisService = Substitute.For<IResultAnalysisService>();
        analysisService.ExportToCsvAsync(
                projectId,
                null,
                Arg.Any<DateTime>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns("csv");

        await using var provider = CreateProvider(projectRepository, analysisService);
        using var service = CreateService(provider);
        var operationId = Guid.NewGuid();
        var stationRequest = new ResultsExportRequest(
            projectId,
            "station",
            ResultsExportFormat.Csv,
            null,
            null,
            null,
            null,
            null,
            operationId);
        var unsupportedAction = () => service.CreateAsync(stationRequest);
        await unsupportedAction.Should().ThrowAsync<ResultsExportValidationException>()
            .Where(error => error.Code == "RESULTS_EXPORT_SOURCE_UNSUPPORTED");

        var localRequest = stationRequest with { Source = "local" };
        await service.CreateAsync(localRequest);
        var conflictingRequest = localRequest with { Format = ResultsExportFormat.Json };
        var conflictAction = () => service.CreateAsync(conflictingRequest);
        await conflictAction.Should().ThrowAsync<ResultsExportIdentityConflictException>();
    }

    [Fact]
    public async Task CancelRunningExport_ShouldNotProduceArtifact()
    {
        var projectId = Guid.NewGuid();
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(projectId).Returns(new Project("结果导出工程"));
        var analysisStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var analysisService = Substitute.For<IResultAnalysisService>();
        analysisService.ExportToCsvAsync(
                projectId,
                null,
                Arg.Any<DateTime>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var cancellationToken = callInfo.Arg<CancellationToken>();
                analysisStarted.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return "never";
            });

        await using var provider = CreateProvider(projectRepository, analysisService);
        using var service = CreateService(provider);
        var started = await service.CreateAsync(new ResultsExportRequest(
            projectId,
            "local",
            ResultsExportFormat.Csv,
            null,
            null,
            null,
            null,
            null,
            Guid.NewGuid()));
        await analysisStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var cancelled = service.Cancel(started.Job.ExportId);
        cancelled.Should().NotBeNull();
        var terminal = await WaitForStateAsync(service, started.Job.ExportId, ResultsExportJobState.Cancelled);

        terminal.ErrorCode.Should().Be("RESULTS_EXPORT_CANCELLED");
        terminal.DownloadAvailable.Should().BeFalse();
        service.TryReadArtifact(started.Job.ExportId, out _).Should().BeFalse();
    }

    private static ServiceProvider CreateProvider(
        IProjectRepository projectRepository,
        IResultAnalysisService analysisService)
    {
        return new ServiceCollection()
            .AddLogging()
            .AddScoped(_ => projectRepository)
            .AddScoped(_ => analysisService)
            .BuildServiceProvider();
    }

    private static ResultsExportJobService CreateService(ServiceProvider provider) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ResultsExportJobService>.Instance);

    private static async Task<ResultsExportJobSnapshot> WaitForStateAsync(
        IResultsExportJobService service,
        Guid exportId,
        ResultsExportJobState expected)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var snapshot = service.Get(exportId);
            if (snapshot?.State == expected)
            {
                return snapshot;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException($"Export {exportId:D} did not reach {expected}.");
    }
}
