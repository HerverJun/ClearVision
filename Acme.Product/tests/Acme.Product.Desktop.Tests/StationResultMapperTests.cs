using Acme.Product.Runtime.Abstractions;
using Acme.Product.Station.Sync;
using FluentAssertions;

namespace Acme.Product.Desktop.Tests;

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
            key.Contains("binaryBlob", StringComparison.OrdinalIgnoreCase));
    }
}
