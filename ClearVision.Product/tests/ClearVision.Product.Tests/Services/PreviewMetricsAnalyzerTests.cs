using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;
using DetectionResultValue = ClearVision.Product.Core.ValueObjects.DetectionResult;

namespace ClearVision.Product.Tests.Services;

public class PreviewMetricsAnalyzerTests
{
    [Fact]
    public void Analyze_BrightNoisyOutput_ProducesDiagnosticsAndSuggestions()
    {
        using var image = new Mat(20, 20, MatType.CV_8UC1, Scalar.All(240));
        var analyzer = new PreviewMetricsAnalyzer(NullLogger<PreviewMetricsAnalyzer>.Instance);
        var outputData = new Dictionary<string, object>
        {
            ["Defects"] = Enumerable.Range(0, 6)
                .Select(index => new Dictionary<string, object>
                {
                    ["X"] = index,
                    ["Y"] = index,
                    ["Width"] = 2,
                    ["Height"] = 3
                })
                .ToList()
        };

        var metrics = analyzer.Analyze(image, outputData, new AutoTuneGoal
        {
            TargetBlobCount = 2,
            MinArea = 50
        });

        metrics.BlobStats.Should().HaveCount(6);
        metrics.Diagnostics.Should().Contain("SpecularHighlightsDominant");
        metrics.Diagnostics.Should().Contain("MaskTooNoisy");
        metrics.Diagnostics.Should().Contain("LowContrast");
        metrics.Diagnostics.Should().Contain("BlurryImage");
        metrics.Goals.CurrentBlobCount.Should().Be(6);
        metrics.Goals.NoisePenalty.Should().Be(6);
        metrics.OverallScore.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(1);

        var suggestedParameters = metrics.Suggestions.Select(suggestion => suggestion.ParameterName).ToList();
        suggestedParameters.Should().Contain("Threshold");
        suggestedParameters.Should().Contain("MinArea");
        suggestedParameters.Should().Contain("MorphologyOperation");
    }

    [Fact]
    public void Analyze_DetectionOutput_ProducesSequenceDiagnosticsAndSuggestions()
    {
        using var image = new Mat(24, 24, MatType.CV_8UC1, Scalar.All(128));
        var analyzer = new PreviewMetricsAnalyzer(NullLogger<PreviewMetricsAnalyzer>.Instance);
        var outputData = new Dictionary<string, object>
        {
            ["DetectionList"] = new DetectionList(new[]
            {
                new DetectionResultValue("Wire_Brown", 0.98f, 10f, 10f, 8f, 8f),
                new DetectionResultValue("Wire_Blue", 0.62f, 30f, 10f, 8f, 8f)
            }),
            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
            ["RequiredMinConfidence"] = 0.8
        };

        var metrics = analyzer.Analyze(image, outputData, new AutoTuneGoal
        {
            TargetBlobCount = 3
        });

        metrics.BlobStats.Should().HaveCount(2);
        metrics.Diagnostics.Should().Contain(PreviewDiagnosticTags.MissingExpectedClass);
        metrics.Diagnostics.Should().Contain(PreviewDiagnosticTags.DetectionCountMismatch);
        metrics.Diagnostics.Should().Contain(PreviewDiagnosticTags.LowDetectionConfidence);
        metrics.Diagnostics.Should().Contain(PreviewDiagnosticTags.OrderMismatch);

        var suggestedParameters = metrics.Suggestions.Select(suggestion => suggestion.ParameterName).ToList();
        suggestedParameters.Should().Contain("ExpectedLabels");
        suggestedParameters.Should().Contain("BoxNms.ScoreThreshold");
    }

    [Fact]
    public void Analyze_DuplicateDetections_UsesBoxNmsRecommendations()
    {
        using var image = new Mat(24, 24, MatType.CV_8UC1, Scalar.All(128));
        var analyzer = new PreviewMetricsAnalyzer(NullLogger<PreviewMetricsAnalyzer>.Instance);
        var outputData = new Dictionary<string, object>
        {
            ["DetectionList"] = new DetectionList(new[]
            {
                new DetectionResultValue("Wire_Brown", 0.98f, 10f, 10f, 8f, 8f),
                new DetectionResultValue("Wire_Brown", 0.96f, 12f, 10f, 8f, 8f),
                new DetectionResultValue("Wire_Black", 0.94f, 22f, 10f, 8f, 8f)
            }),
            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
            ["ExpectedCount"] = 3,
            ["RequiredMinConfidence"] = 0.8
        };

        var metrics = analyzer.Analyze(image, outputData, new AutoTuneGoal());

        metrics.Diagnostics.Should().Contain(PreviewDiagnosticTags.DuplicateDetectedClass);
        metrics.Suggestions.Should().Contain(item =>
            item.ParameterName == "BoxNms.IouThreshold" &&
            string.Equals(Convert.ToString(item.SuggestedValue), "decrease", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyze_Defects_ShouldPreserveRejectReasonWhenPresentAndAllowNull()
    {
        using var image = new Mat(24, 24, MatType.CV_8UC1, Scalar.All(128));
        var analyzer = new PreviewMetricsAnalyzer(NullLogger<PreviewMetricsAnalyzer>.Instance);
        var outputData = new Dictionary<string, object>
        {
            ["Defects"] = new List<Dictionary<string, object>>
            {
                new()
                {
                    ["X"] = 1,
                    ["Y"] = 2,
                    ["Width"] = 3,
                    ["Height"] = 4,
                    ["RejectReason"] = null!
                },
                new()
                {
                    ["X"] = 5,
                    ["Y"] = 6,
                    ["Width"] = 7,
                    ["Height"] = 8,
                    ["RejectReason"] = "TooSmall"
                }
            }
        };

        var metrics = analyzer.Analyze(image, outputData!, new AutoTuneGoal());

        metrics.BlobStats.Should().HaveCount(2);
        metrics.BlobStats[0].RejectReason.Should().BeNull();
        metrics.BlobStats[1].RejectReason.Should().Be("TooSmall");
    }
}
