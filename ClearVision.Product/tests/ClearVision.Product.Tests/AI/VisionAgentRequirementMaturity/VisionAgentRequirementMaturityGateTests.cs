using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Tests.AI.VisionAgentRequirementMaturity;

public sealed class VisionAgentRequirementMaturityGateTests
{
    [Theory(DisplayName = "Requirement maturity gate should match golden intent cases")]
    [MemberData(nameof(GoldenCases))]
    public void Evaluate_ShouldMatchGoldenCases(GoldenCase item)
    {
        var result = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = item.Input,
            HasCurrentFlow = item.HasCurrentFlow
        });

        result.Maturity.Should().Be(item.ExpectedMaturity, item.Id);
        result.TaskType.Should().Be(item.ExpectedTaskType, item.Id);
        result.CanBuild.Should().Be(item.ExpectedCanBuild, item.Id);
        VisionAgentRequirementMaturityGate.ToRouterIntent(result).Should().Be(item.ExpectedIntent, item.Id);
        if (item.ExpectedMissingFields.Count > 0)
        {
            result.MissingFields.Should().Contain(item.ExpectedMissingFields, item.Id);
        }
        else
        {
            result.MissingFields.Should().BeEmpty(item.Id);
        }
    }

    [Fact(DisplayName = "Intent Router should downgrade over-confident abstract build intents")]
    public async Task RouteAsync_ShouldDowngradeOverconfidentAbstractIntent()
    {
        var service = new VisionAgentIntentRouterService(
            new DelegateIntentCompletionSource((_, _) => Task.FromResult(JsonSerializer.Serialize(new
            {
                intent = "actionable_vision_plan",
                confidence = "high",
                shouldOpenPlan = true,
                shouldBuildDirectly = false,
                canBuild = true,
                needsClarification = false,
                publicReason = "Model says buildable.",
                assistantReply = "I can build it.",
                clarificationQuestions = Array.Empty<string>(),
                fallbackAllowed = true
            }))),
            Microsoft.Extensions.Options.Options.Create(new VisionAgentIntentRouterOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VisionAgentIntentRouterService>.Instance);

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "我想构建一个真正有野心的终极视觉检测方案。"
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentAmbiguousVisionRequirement);
        result.ShouldOpenPlan.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeTrue();
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);
        result.DecisionTrace.Should().NotBeNull();
        result.DecisionTrace!.MaturityLevel.Should().Be(AiRequirementMaturity.AbstractGoal);
        result.DecisionTrace.FallbackReason.Should().Contain("maturity_gate_blocked");
    }

    [Fact(DisplayName = "Plan should not choose operator route for abstract goals")]
    public async Task CreatePlanAsync_ShouldReturnClarificationPlanForAbstractGoal()
    {
        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "帮我做一个高级视觉检测方案。",
                OriginalUserPrompt = "帮我做一个高级视觉检测方案。"
            },
            CancellationToken.None);

        plan.CanBuild.Should().BeFalse();
        plan.Intent.Should().Be(AiRequirementMaturity.AbstractGoal);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);
        plan.RecommendedRoute.RouteId.Should().Be("requirement_decomposition");
        plan.RecommendedRoute.Operators.Should().BeEmpty();
        plan.RecommendedRoute.Title.Should().NotContain("表面缺陷");
        plan.BlockingReasons.Should().Contain("abstract_goal_needs_decomposition");
    }

    [Fact(DisplayName = "BuildFromPlan should hard-block low maturity plans before generation")]
    public async Task BuildFromPlanAsync_ShouldBlockLowMaturityPlanBeforeGeneration()
    {
        var generationCalls = 0;
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>?>())
            .Returns(_ =>
            {
                generationCalls++;
                return Task.FromResult(new AiFlowGenerationResult { Success = true, Flow = new OperatorFlowDto() });
            });
        var orchestrator = CreateOrchestrator(generationService);
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "我想构建一个真正有野心的终极视觉检测方案。",
                OriginalUserPrompt = "我想构建一个真正有野心的终极视觉检测方案。"
            },
            CancellationToken.None);

        var result = await orchestrator.BuildFromPlanAsync(
            new AiFlowGenerationRequest(plan.OriginalUserPrompt, Mode: GenerateFlowMode.New)
            {
                UseVisionAgentGenerateFlow = true,
                BuildFromPlan = new VisionAgentBuildFromPlanRequest
                {
                    PlanId = plan.PlanId,
                    PlanHash = plan.PlanHash,
                    PlanSnapshot = plan,
                    OriginalUserPrompt = plan.OriginalUserPrompt,
                    BuildIntent = "new",
                    MetadataOnly = true
                }
            },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeClarificationRequired);
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.CanBuild.Should().BeFalse();
        result.DecisionTrace.Should().NotBeNull();
        generationCalls.Should().Be(0);
    }

    [Fact(DisplayName = "GenerateFlow direct Vision Agent request should hard-block low maturity input")]
    public async Task GenerateFlowMessageHandler_ShouldBlockLowMaturityDirectRequest()
    {
        var generationCalls = 0;
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<AiStreamChunk>?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>?>())
            .Returns(_ =>
            {
                generationCalls++;
                return Task.FromResult(new AiFlowGenerationResult { Success = true, Flow = new OperatorFlowDto() });
            });
        var handler = new GenerateFlowMessageHandler(
            generationService,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());

        var json = await handler.HandleAsync(
            "我想构建一个真正有野心的终极视觉检测方案。",
            mode: GenerateFlowMode.New,
            useVisionAgentGenerateFlow: true);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        root.GetProperty("failureType").GetString().Should().Be(AiFlowGenerationResult.FailureTypeClarificationRequired);
        root.GetProperty("requirementMaturity").GetProperty("maturity").GetString().Should().Be(AiRequirementMaturity.AbstractGoal);
        root.GetProperty("decisionTrace").GetProperty("fallbackReason").GetString().Should().Contain("maturity_gate_blocked");
        generationCalls.Should().Be(0);
    }

    public static IEnumerable<object[]> GoldenCases()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "quality", "ai", "vision_agent_intent_cases.json"));
        var cases = JsonSerializer.Deserialize<List<GoldenCase>>(json, options) ?? [];
        return cases.Select(item => new object[] { item });
    }

    private static VisionAgentOrchestrator CreateOrchestrator(IAiFlowGenerationService generationService)
    {
        return new VisionAgentOrchestrator(
            new VisionAgentToolRegistry(
            [
                new OperatorCatalogTool(),
                new FlowTemplateMatchTool(),
                new FlowValidationTool()
            ]),
            generationService);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "quality", "ai", "vision_agent_intent_cases.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing quality/ai/vision_agent_intent_cases.json.");
    }

    public sealed record GoldenCase
    {
        public string Id { get; init; } = string.Empty;
        public string Input { get; init; } = string.Empty;
        public bool HasCurrentFlow { get; init; }
        public string ExpectedMaturity { get; init; } = string.Empty;
        public string ExpectedIntent { get; init; } = string.Empty;
        public string ExpectedTaskType { get; init; } = string.Empty;
        public bool ExpectedCanBuild { get; init; }
        public List<string> ExpectedMissingFields { get; init; } = new();
    }

    private sealed class DelegateIntentCompletionSource : IVisionAgentIntentRouterCompletionSource
    {
        private readonly Func<VisionAgentIntentRouterCompletionRequest, CancellationToken, Task<string>> _completion;

        public DelegateIntentCompletionSource(Func<VisionAgentIntentRouterCompletionRequest, CancellationToken, Task<string>> completion)
        {
            _completion = completion;
        }

        public Task<string> CompleteAsync(
            VisionAgentIntentRouterCompletionRequest request,
            CancellationToken cancellationToken)
        {
            return _completion(request, cancellationToken);
        }
    }
}
