using System.Reflection;
using ClearVision.Product.Core.Events;
using ClearVision.Product.Infrastructure.Events;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.Events;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class InMemoryEventStoreTests
{
    [Fact]
    public void Append_SameEventInstance_ReusesSequenceId()
    {
        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var projectId = Guid.NewGuid();
        var evt = new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        };

        var first = store.Append(projectId, evt);
        var second = store.Append(projectId, evt);

        first.Should().Be(second);
        store.GetEventsAfter(projectId, 0).Should().ContainSingle();
    }

    [Fact]
    public void GetEventsAfter_ReturnsStoredEventsInSequenceOrder()
    {
        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var projectId = Guid.NewGuid();

        var first = new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        };
        var second = new InspectionProgressEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ProcessedCount = 2
        };

        var firstSequence = store.Append(projectId, first);
        var secondSequence = store.Append(projectId, second);

        var replay = store.GetEventsAfter(projectId, firstSequence);

        replay.Select(e => e.SequenceId).Should().Equal(secondSequence);
        replay.Select(e => ((InspectionProgressEvent)e.Event).ProcessedCount).Should().Equal(2);
        store.ReplayedEventCount.Should().Be(1);
    }

    [Fact]
    public void Append_ShouldTrimByConfiguredCapacityAndRecordDroppedEvents()
    {
        var store = new InMemoryEventStore(
            NullLogger<InMemoryEventStore>.Instance,
            Options.Create(new InMemoryEventStoreOptions
            {
                MaxEventsPerProject = 2,
                MaxProjects = 50
            }));
        var projectId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            store.Append(projectId, new InspectionProgressEvent
            {
                ProjectId = projectId,
                SessionId = Guid.NewGuid(),
                ProcessedCount = i
            });
        }

        store.GetEventsAfter(projectId, 0).Should().HaveCount(2);
        store.DroppedEventCount.Should().Be(3);
    }

    [Fact]
    public void Append_WhenProjectCleanupAlreadyScheduled_ShouldSkipAdditionalCleanupSchedules()
    {
        var store = new InMemoryEventStore(
            NullLogger<InMemoryEventStore>.Instance,
            Options.Create(new InMemoryEventStoreOptions
            {
                MaxEventsPerProject = 2,
                MaxProjects = 1
            }));
        var cleanupScheduledField = typeof(InMemoryEventStore).GetField(
            "_cleanupScheduled",
            BindingFlags.Instance | BindingFlags.NonPublic);
        cleanupScheduledField.Should().NotBeNull();
        cleanupScheduledField!.SetValue(store, 1);

        store.Append(Guid.NewGuid(), new InspectionProgressEvent
        {
            ProjectId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            ProcessedCount = 1
        });
        store.Append(Guid.NewGuid(), new InspectionProgressEvent
        {
            ProjectId = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            ProcessedCount = 2
        });

        store.CleanupScheduleSkipCount.Should().Be(1);
        store.CleanupRunCount.Should().Be(0);
    }

    [Fact]
    public void Append_ResultEventWithInlineImage_ShouldStoreLightweightReplayEvent()
    {
        var store = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var evt = new InspectionResultEvent
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            ResultId = Guid.NewGuid(),
            ImageId = imageId,
            Status = "NG",
            DefectCount = 1,
            ProcessingTimeMs = 42,
            OutputImageBase64 = Convert.ToBase64String(new byte[1024 * 128]),
            OutputData = new Dictionary<string, object>
            {
                ["Score"] = 0.91d
            }
        };

        var firstSequence = store.Append(projectId, evt);
        var secondSequence = store.Append(projectId, evt);

        secondSequence.Should().Be(firstSequence);
        evt.OutputImageBase64.Should().NotBeNullOrEmpty("live subscribers still receive the inline image");

        var replay = store.GetEventsAfter(projectId, 0).Should().ContainSingle().Subject;
        var replayEvent = replay.Event.Should().BeOfType<InspectionResultEvent>().Subject;
        replayEvent.OutputImageBase64.Should().BeNull("SSE replay history must not retain large image payloads");
        replayEvent.ImageId.Should().Be(imageId);
        replayEvent.OutputData.Should().ContainKey("Score");
    }
}
