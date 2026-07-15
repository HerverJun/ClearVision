using ClearVision.Product.Application.Analysis;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class InspectionServiceHistoryComparisonTests
{
    [Fact]
    public async Task CompareInspectionHistoryAsync_ShouldReturnStructuredDiffsAndCompatibilityWarnings()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        var left = CreateDetail(
            projectId,
            leftId,
            InspectionStatus.OK,
            new DateTime(2026, 7, 4, 8, 0, 0, DateTimeKind.Utc),
            flowHash: "FLOW-A",
            bundleId: "BUNDLE-A",
            outputJson: """{"score":42,"oldOnly":true,"same":"yes"}""",
            analysisJson: """{"measurements":{"diameter":12.5}}""");
        var right = CreateDetail(
            projectId,
            rightId,
            InspectionStatus.NG,
            new DateTime(2026, 7, 4, 8, 1, 0, DateTimeKind.Utc),
            flowHash: "FLOW-B",
            bundleId: "BUNDLE-B",
            outputJson: """{"score":45,"newOnly":"added","same":"yes"}""",
            analysisJson: """{"measurements":{"diameter":13.1}}""");
        right.Defects =
        [
            new InspectionHistoryDefectItem
            {
                Id = Guid.NewGuid(),
                Type = DefectType.Scratch,
                X = 1,
                Y = 2,
                Width = 3,
                Height = 4,
                ConfidenceScore = 0.92
            }
        ];

        repository.GetHistoryDetailAsync(projectId, leftId).Returns(left);
        repository.GetHistoryDetailAsync(projectId, rightId).Returns(right);
        var service = CreateService(repository);

        var comparison = await service.CompareInspectionHistoryAsync(projectId, leftId, rightId);

        comparison.Should().NotBeNull();
        comparison!.Compatibility.FlowVersionCompatible.Should().BeFalse();
        comparison.Compatibility.CalibrationBundleCompatible.Should().BeFalse();
        comparison.Warnings.Should().Contain("流程版本不一致，对比仅供参考");
        comparison.Warnings.Should().Contain("标定资产不一致，空间坐标对比可能无效");
        comparison.TraceabilityDiff.Should().Contain(diff =>
            diff.Path == """$["traceability"]["flowVersionHash"]""" &&
            diff.DiffType == "Incompatible");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["outputDataPreview"]["score"]""" &&
            diff.DiffType == "Changed" &&
            diff.LeftValuePreview == "42" &&
            diff.RightValuePreview == "45");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["outputDataPreview"]["oldOnly"]""" &&
            diff.DiffType == "Removed" &&
            diff.RightValuePreview == "本次结果未记录");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["outputDataPreview"]["newOnly"]""" &&
            diff.DiffType == "Added" &&
            diff.LeftValuePreview == "旧数据未记录");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["analysisDataPreview"]["measurements"]["diameter"]""" &&
            diff.DiffType == "Changed");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["defectSummary"]["Scratch"]["count"]""" &&
            diff.DiffType == "Added");
    }

    [Fact]
    public async Task CompareInspectionHistoryAsync_ShouldFailSoftForMalformedAndTruncatedJson()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        var longValue = new string('A', 900);
        var left = CreateDetail(
            projectId,
            leftId,
            InspectionStatus.Error,
            DateTime.UtcNow.AddMinutes(-2),
            flowHash: "FLOW-A",
            bundleId: "BUNDLE-A",
            outputJson: "{not-json",
            analysisJson: $$"""{"token":"secret-token","notes":"{{longValue}}"}""");
        var right = CreateDetail(
            projectId,
            rightId,
            InspectionStatus.Error,
            DateTime.UtcNow,
            flowHash: "FLOW-A",
            bundleId: "BUNDLE-A",
            outputJson: """{"score":1}""",
            analysisJson: $$"""{"token":"secret-token-2","notes":"{{longValue}}"}""");

        repository.GetHistoryDetailAsync(projectId, leftId).Returns(left);
        repository.GetHistoryDetailAsync(projectId, rightId).Returns(right);
        var service = CreateService(repository);

        var comparison = await service.CompareInspectionHistoryAsync(projectId, leftId, rightId);

        comparison.Should().NotBeNull();
        comparison!.Compatibility.OnlySafePreviewComparison.Should().BeTrue();
        comparison.Compatibility.HasUnknownFields.Should().BeTrue();
        comparison.Warnings.Should().Contain("仅比较安全预览字段");
        comparison.FieldDiffs.Should().Contain(diff =>
            diff.Path == """$["outputDataPreview"]""" &&
            diff.DiffType == "Unknown");
        var serializedValues = string.Join("\n", comparison.FieldDiffs.SelectMany(diff =>
            new[] { diff.LeftValuePreview, diff.RightValuePreview }));
        serializedValues.Should().NotContain("secret-token");
        serializedValues.Should().Contain("[REDACTED]");
        serializedValues.Should().NotContain(longValue);
    }

    [Fact]
    public async Task CompareInspectionHistoryAsync_ShouldReturnNullWhenEitherResultIsOutsideProjectScope()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var leftId = Guid.NewGuid();
        var rightId = Guid.NewGuid();
        repository.GetHistoryDetailAsync(projectId, leftId).Returns(CreateDetail(projectId, leftId, InspectionStatus.OK, DateTime.UtcNow));
        repository.GetHistoryDetailAsync(projectId, rightId).Returns(Task.FromResult<InspectionHistoryDetail?>(null));
        var service = CreateService(repository);

        var comparison = await service.CompareInspectionHistoryAsync(projectId, leftId, rightId);

        comparison.Should().BeNull();
        _ = repository.Received(1).GetHistoryDetailAsync(projectId, leftId);
        _ = repository.Received(1).GetHistoryDetailAsync(projectId, rightId);
    }

    [Fact]
    public async Task FindPreviousSuccessfulInspectionAsync_ShouldPreferSameFlowAndClampLimit()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var currentTime = new DateTime(2026, 7, 4, 8, 10, 0, DateTimeKind.Utc);
        var current = CreateDetail(projectId, currentId, InspectionStatus.NG, currentTime, flowHash: "FLOW-A");
        var previous = CreateDetail(projectId, Guid.NewGuid(), InspectionStatus.OK, currentTime.AddMinutes(-1), flowHash: "FLOW-A");
        repository.GetHistoryDetailAsync(projectId, currentId).Returns(current);
        repository.FindPreviousSuccessfulInspectionAsync(projectId, currentTime, "FLOW-A", 200).Returns(previous);
        var service = CreateService(repository);

        var reference = await service.FindPreviousSuccessfulInspectionAsync(projectId, currentId, limit: 500);

        reference.Should().NotBeNull();
        reference!.Found.Should().BeTrue();
        reference.IsFlowVersionFallback.Should().BeFalse();
        reference.QueryLimit.Should().Be(200);
        reference.ReferenceSummary!.ResultId.Should().Be(previous.Id);
        _ = repository.Received(1).FindPreviousSuccessfulInspectionAsync(projectId, currentTime, "FLOW-A", 200);
        _ = repository.DidNotReceive().FindPreviousSuccessfulInspectionAsync(projectId, currentTime, flowVersionHash: null, limit: 200);
    }

    [Fact]
    public async Task FindPreviousSuccessfulInspectionAsync_ShouldUseFallbackWithFlowWarning()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var currentTime = new DateTime(2026, 7, 4, 8, 10, 0, DateTimeKind.Utc);
        var current = CreateDetail(projectId, currentId, InspectionStatus.Error, currentTime, flowHash: "FLOW-A");
        var fallback = CreateDetail(projectId, Guid.NewGuid(), InspectionStatus.OK, currentTime.AddMinutes(-5), flowHash: "FLOW-B");
        repository.GetHistoryDetailAsync(projectId, currentId).Returns(current);
        repository.FindPreviousSuccessfulInspectionAsync(projectId, currentTime, "FLOW-A", 50)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(null));
        repository.FindPreviousSuccessfulInspectionAsync(projectId, currentTime, flowVersionHash: null, limit: 50)
            .Returns(fallback);
        var service = CreateService(repository);

        var reference = await service.FindPreviousSuccessfulInspectionAsync(projectId, currentId);

        reference.Should().NotBeNull();
        reference!.Found.Should().BeTrue();
        reference.IsFlowVersionFallback.Should().BeTrue();
        reference.Warnings.Should().Contain("流程版本不一致，对比仅供参考");
        reference.ReferenceSummary!.ResultId.Should().Be(fallback.Id);
    }

    [Fact]
    public async Task FindPreviousSuccessfulInspectionAsync_ShouldFailSoftWhenNotFound()
    {
        var repository = Substitute.For<IInspectionResultRepository>();
        var projectId = Guid.NewGuid();
        var currentId = Guid.NewGuid();
        var currentTime = new DateTime(2026, 7, 4, 8, 10, 0, DateTimeKind.Utc);
        var current = CreateDetail(projectId, currentId, InspectionStatus.NG, currentTime, flowHash: "FLOW-A");
        repository.GetHistoryDetailAsync(projectId, currentId).Returns(current);
        repository.FindPreviousSuccessfulInspectionAsync(projectId, currentTime, "FLOW-A", 50)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(null));
        repository.FindPreviousSuccessfulInspectionAsync(projectId, currentTime, flowVersionHash: null, limit: 50)
            .Returns(Task.FromResult<InspectionHistoryDetail?>(null));
        var service = CreateService(repository);

        var reference = await service.FindPreviousSuccessfulInspectionAsync(projectId, currentId);

        reference.Should().NotBeNull();
        reference!.Found.Should().BeFalse();
        reference.Message.Should().Be("未找到失败前成功参考");
        reference.ReferenceSummary.Should().BeNull();
    }

    private static InspectionService CreateService(IInspectionResultRepository repository)
    {
        var projectRepository = Substitute.For<IProjectRepository>();
        projectRepository.GetByIdFreshAsync(Arg.Any<Guid>()).Returns(new Project("history-comparison-project"));
        return new InspectionService(
            repository,
            projectRepository,
            Substitute.For<IFlowExecutionService>(),
            Substitute.For<IImageAcquisitionService>(),
            Substitute.For<IConfigurationService>(),
            Substitute.For<IInspectionRuntimeCoordinator>(),
            Substitute.For<IInspectionWorker>(),
            Substitute.For<IImageCacheRepository>(),
            new AnalysisDataBuilder(),
            Substitute.For<IProjectFlowStorage>(),
            NullLogger<InspectionService>.Instance);
    }

    private static InspectionHistoryDetail CreateDetail(
        Guid projectId,
        Guid resultId,
        InspectionStatus status,
        DateTime inspectionTime,
        string? flowHash = "FLOW-A",
        string? bundleId = "BUNDLE-A",
        string? outputJson = """{"score":1}""",
        string? analysisJson = """{"cards":[]}""")
    {
        return new InspectionHistoryDetail
        {
            Id = resultId,
            ProjectId = projectId,
            Status = status,
            ProcessingTimeMs = 25,
            ConfidenceScore = 0.87,
            ErrorMessage = status == InspectionStatus.Error ? "failure" : null,
            InspectionTime = inspectionTime,
            FlowVersionHash = flowHash,
            CalibrationBundleId = bundleId,
            SessionId = Guid.NewGuid(),
            HasOutputData = !string.IsNullOrWhiteSpace(outputJson),
            HasAnalysisData = !string.IsNullOrWhiteSpace(analysisJson),
            OutputDataJson = outputJson,
            AnalysisDataJson = analysisJson,
            Defects = []
        };
    }
}
