using System.Text.Json;
using System.Text.Json.Serialization;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Outcomes;
using ClearVision.Product.Runtime.Abstractions;

namespace ClearVision.Product.Tests.Runtime;

[TestClassification(TestDomain.Runtime, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "runtime")]
public sealed class StationSyncContractsSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    [Fact]
    public void StationResultSummaryDto_SerializesWithCamelCaseAndStringEnums()
    {
        var payload = new StationResultSummaryDto
        {
            StationId = "station-a",
            LineName = "line-1",
            SequenceId = 42,
            RunId = "run-42",
            PackageId = "pkg-1",
            PackageName = "Main Package",
            FlowHash = "sha256:abc",
            ImageId = "image-7",
            Outcome = RuntimeRunOutcome.Ng,
            InspectionStatus = InspectionStatus.NG,
            ExecutionOutcome = ExecutionOutcome.Succeeded,
            DecisionOutcome = DecisionOutcome.Ng,
            HasJudgmentSignal = true,
            DecisionSource = "FinalDecisionBinding:judge:Judgment",
            ReasonCode = "StringMapNg",
            ExecutionTimeMs = 28,
            DiagnosticCode = "JudgeNg",
            DiagnosticMessage = "threshold exceeded",
            StartedAtUtc = DateTimeOffset.Parse("2026-05-04T10:00:00Z"),
            CompletedAtUtc = DateTimeOffset.Parse("2026-05-04T10:00:00.028Z")
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        Assert.Contains("\"stationId\":\"station-a\"", json);
        Assert.Contains("\"lineName\":\"line-1\"", json);
        Assert.Contains("\"sequenceId\":42", json);
        Assert.Contains("\"outcome\":\"Ng\"", json);
        Assert.Contains("\"inspectionStatus\":\"NG\"", json);
        Assert.Contains("\"executionOutcome\":\"Succeeded\"", json);
        Assert.Contains("\"decisionOutcome\":\"Ng\"", json);
        Assert.Contains("\"hasJudgmentSignal\":true", json);
        Assert.Contains("\"decisionSource\":\"FinalDecisionBinding:judge:Judgment\"", json);
        Assert.Contains("\"reasonCode\":\"StringMapNg\"", json);
        Assert.Contains("\"completedAtUtc\":\"2026-05-04T10:00:00.028+00:00\"", json);
    }

    [Fact]
    public void LegacyStationResultSummary_DeserializesWithoutInventingCanonicalFields()
    {
        const string json = """
            {"stationId":"station-legacy","outcome":"Error","inspectionStatus":"Error"}
            """;

        var restored = JsonSerializer.Deserialize<StationResultSummaryDto>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Null(restored!.ExecutionOutcome);
        Assert.Null(restored.DecisionOutcome);
        Assert.Null(restored.HasJudgmentSignal);
        Assert.Null(restored.DecisionSource);
        Assert.Null(restored.ReasonCode);
    }

    [Fact]
    public void StationSnapshotDto_RoundTripsUtcFields()
    {
        var payload = new StationSnapshotDto
        {
            StationId = "station-b",
            LineName = "line-2",
            CapturedAtUtc = DateTimeOffset.Parse("2026-05-04T12:34:56Z"),
            State = RuntimeHostState.Running,
            PackageId = "pkg-2",
            PackageName = "Runtime Package",
            FlowHash = "sha256:def",
            CurrentRunId = "run-99",
            SessionOkCount = 12,
            SessionNgCount = 3,
            SessionErrorCount = 1,
            SessionOutcomeStatistics = new InspectionOutcomeStatistics
            {
                TotalAttemptCount = 17,
                ExecutionSucceededCount = 15,
                ValidDecisionCount = 15,
                OkCount = 12,
                NgCount = 3,
                FailedCount = 1,
                TimedOutCount = 1
            }
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var restored = JsonSerializer.Deserialize<StationSnapshotDto>(json, JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(DateTimeOffset.Parse("2026-05-04T12:34:56Z"), restored!.CapturedAtUtc);
        Assert.Equal(RuntimeHostState.Running, restored.State);
        Assert.Equal("Runtime Package", restored.PackageName);
        Assert.Equal(12, restored.SessionOkCount);
        Assert.Equal(3, restored.SessionNgCount);
        Assert.Equal(1, restored.SessionErrorCount);
        Assert.NotNull(restored.SessionOutcomeStatistics);
        Assert.Equal(17, restored.SessionOutcomeStatistics!.TotalAttemptCount);
        Assert.Equal(2, restored.SessionOutcomeStatistics.ExecutionFailureCount);
    }

    [Fact]
    public void StationResultSummaryDto_DoesNotExposeBinaryTransportFields()
    {
        var binaryFields = typeof(StationResultSummaryDto)
            .GetProperties()
            .Where(property =>
                property.PropertyType == typeof(byte[]) ||
                property.Name.Contains("Base64", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Thumbnail", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("ResultImage", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("OriginalImage", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(binaryFields);
        Assert.Equal(typeof(Dictionary<string, string?>), typeof(StationResultSummaryDto).GetProperty(nameof(StationResultSummaryDto.PrimaryOutputsPreview))!.PropertyType);
    }
}
