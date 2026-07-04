using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationResultMapperTests
{
    [Fact]
    public void ToSummary_ShouldNeverCopyImagePayloadsIntoStudioSyncPreview()
    {
        var result = new RuntimeNormalizedResult
        {
            RunId = "run-1",
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:flow",
            ImageId = "image-1",
            Outcome = RuntimeRunOutcome.Ok,
            ExecutionTimeMs = 12,
            DiagnosticCode = "OK",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-12),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            SourceImageBytes = [1, 2, 3],
            OutputImageBytes = [4, 5, 6],
            PrimaryOutputs = new Dictionary<string, object?>
            {
                ["score"] = 0.98d,
                ["thumbnail"] = "base64-image-data",
                ["outputImage"] = "should-not-leave-station",
                ["binaryBlob"] = new byte[] { 7, 8, 9 },
                ["Scene"] = new { layers = new[] { "full-scene" } },
                ["OutputScene"] = "should-not-leave-station",
                ["ArtifactPayload"] = "large-payload",
                ["measurements"] = new[] { 1, 2, 3 }
            }
        };

        var summary = StationResultMapper.ToSummary(
            result,
            new StationIdentityContext
            {
                StationId = "station-a",
                LineName = "line-a",
                CurrentPackageVersion = "1.0.0"
            });

        summary.PrimaryOutputsPreview.Should().ContainKey("score");
        summary.PrimaryOutputsPreview.Should().ContainKey("measurements");
        summary.PrimaryOutputsPreview.Keys.Should().NotContain(key =>
            key.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("base64", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("binaryBlob", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("scene", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("artifactPayload", StringComparison.OrdinalIgnoreCase));
    }
}
