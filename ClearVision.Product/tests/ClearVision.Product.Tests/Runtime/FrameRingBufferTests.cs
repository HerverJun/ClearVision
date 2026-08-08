using ClearVision.Product.Core.Streaming;
using ClearVision.Product.Infrastructure.Streaming;
using FluentAssertions;

namespace ClearVision.Product.Tests.Runtime;

[TestClassification(TestDomain.Runtime, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "runtime")]
public class FrameRingBufferTests
{
    [Fact]
    public void Push_WhenCapacityExceeded_DropsOldestAndReportsStats()
    {
        var buffer = new FrameRingBuffer(3);

        buffer.Push(CreateFrame(1));
        buffer.Push(CreateFrame(2));
        buffer.Push(CreateFrame(3));
        buffer.Push(CreateFrame(4));

        buffer.TryGetLatest(out var latest).Should().BeTrue();
        latest!.Sequence.Should().Be(4);
        buffer.SliceBySequence(1, 4).Select(frame => frame.Sequence).Should().Equal(2, 3, 4);

        var stats = buffer.SnapshotStats();
        stats.Capacity.Should().Be(3);
        stats.Count.Should().Be(3);
        stats.OverwrittenCount.Should().Be(1);
        stats.OldestSequence.Should().Be(2);
        stats.LatestSequence.Should().Be(4);
    }

    [Fact]
    public void SliceAround_ShouldReturnRequestedSequenceWindow()
    {
        var buffer = new FrameRingBuffer(5);
        for (var sequence = 10; sequence < 15; sequence++)
        {
            buffer.Push(CreateFrame(sequence));
        }

        buffer.SliceAround(12, before: 1, after: 2)
            .Select(frame => frame.Sequence)
            .Should()
            .Equal(11, 12, 13, 14);
    }

    private static FrameEnvelope CreateFrame(long sequence)
    {
        return new FrameEnvelope(
            "cam-1",
            sequence,
            DateTimeOffset.UtcNow,
            1,
            1,
            "image/png",
            FramePayloadKind.EncodedImage,
            new byte[] { 1, 2, 3 },
            TimestampSource: FrameTimestampSource.HostFallback);
    }
}
