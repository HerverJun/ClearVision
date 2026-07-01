using System;
using System.IO;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.AgentRun;

public sealed class VisionAgentRunKindResolverTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "clearvision-runkind-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolve_ShouldUseTerminalIntentBeforePayload()
    {
        var service = CreateService();
        var run = service.CreateRun("intent wins", new
        {
            runKind = VisionAgentRunKindResolver.Build,
            metadataOnly = true
        });
        var reservation = service.TryReserveTerminal(run.RunId, AgentRunEventStatuses.Completed);
        service.PrepareTerminalIntent(
            run.RunId,
            new AgentRunTerminalIntentDraft
            {
                SessionId = "session-kind",
                RunType = VisionAgentRunKindResolver.Plan,
                TargetStatus = AgentRunEventStatuses.Completed,
                TerminalMutationId = "plan-terminal:kind:completed",
                PayloadFingerprint = "sha256:kind",
                Identity = "plan:kind"
            },
            reservation).Should().NotBeNull();

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(run.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Plan);
    }

    [Theory]
    [InlineData("plan", VisionAgentRunKind.Plan)]
    [InlineData("build", VisionAgentRunKind.Build)]
    public void Resolve_ShouldUseExplicitRunKind(string runKind, VisionAgentRunKind expected)
    {
        var service = CreateService();
        var run = service.CreateRun("explicit kind", new
        {
            runKind,
            metadataOnly = true
        });

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(run.RunId)!)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Resolve_ShouldTreatPlanEventsAsPlan()
    {
        var service = CreateService();
        var run = service.CreateRun("plan evidence", new { metadataOnly = true });
        service.Append(run.RunId, new AgentRunEventDraft
        {
            EventType = AgentRunEventTypes.PlanCompleted,
            Stage = "plan",
            Title = "Plan ready",
            Summary = "Plan completed.",
            Status = AgentRunEventStatuses.Completed,
            Payload = new { planId = "plan-a", planHash = "sha256:plan", metadataOnly = true }
        });

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(run.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Plan);
    }

    [Fact]
    public void Resolve_ShouldRequireBuildFromPlanExclusiveEvidenceForLegacyBuild()
    {
        var service = CreateService();
        var build = service.CreateRun("build evidence", new
        {
            buildFromPlan = new { planId = "plan-a", metadataOnly = true },
            metadataOnly = true
        });
        var unknown = service.CreateRun("plan ids alone", new
        {
            mode = "new",
            planId = "plan-a",
            planHash = "sha256:plan",
            metadataOnly = true
        });

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(build.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Build);
        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(unknown.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Unknown);
    }

    [Fact]
    public void Resolve_ShouldTreatLegacyRunStartedPlanModeAsPlan()
    {
        var service = CreateService();
        var run = service.CreateRun("legacy plan mode", new
        {
            mode = "plan",
            sessionId = "session-plan",
            metadataOnly = true
        });

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(run.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Plan);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("modify")]
    [InlineData("auto")]
    public void Resolve_ShouldNotInferBuildFromLegacyNonPlanModes(string mode)
    {
        var service = CreateService();
        var run = service.CreateRun("legacy non-build mode", new
        {
            mode,
            planId = "plan-a",
            planHash = "sha256:plan",
            metadataOnly = true
        });

        VisionAgentRunKindResolver.Resolve(service.ReplayRaw(run.RunId)!)
            .Should()
            .Be(VisionAgentRunKind.Unknown);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private AgentRunEventStreamService CreateService()
    {
        var redactor = new AgentRunEventRedactor();
        return new AgentRunEventStreamService(
            new AgentRunEventStore(Path.Combine(_tempRoot, Guid.NewGuid().ToString("N")), redactor),
            redactor);
    }
}
