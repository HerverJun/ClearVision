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
        var expectedCanPlan = item.ExpectedCanPlan ?? item.ExpectedCanBuild;
        result.CanPlan.Should().Be(expectedCanPlan, item.Id);
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

    [Fact(DisplayName = "Unknown explicit object and target should allow Plan but block Build")]
    public async Task ExplicitUnknownObjectAndTarget_ShouldPlanWithoutAskingKnownSlots()
    {
        const string prompt = "检测目标是外星人，识别内容是额头上的第三只竖眼";

        var maturity = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = prompt
        });

        maturity.ObjectSignals.Should().Contain("外星人");
        maturity.TaskSignals.Should().Contain("额头上的第三只竖眼");
        maturity.CanPlan.Should().BeTrue();
        maturity.CanBuild.Should().BeFalse();
        maturity.MissingFields.Should().NotContain("inspection_object");
        maturity.MissingFields.Should().NotContain("task_type");
        maturity.MissingFields.Should().Contain(["image_source", "acceptance_criteria", "model_or_rule_strategy"]);

        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt
            },
            CancellationToken.None);

        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.CanPlan.Should().BeTrue();
        plan.CanBuild.Should().BeFalse();
        plan.Intent.Should().NotBe(AiRequirementMaturity.Ambiguous);
        plan.RecommendedRoute.Operators.Should().NotBeEmpty();
        plan.ClarificationQuestions.Select(question => question.Id)
            .Should()
            .Contain(["image_source", "acceptance_criteria", "model_or_rule_strategy"]);
        plan.ClarificationQuestions.Select(question => question.Id)
            .Should()
            .NotContain(["inspection_object", "task_type"]);
    }

    [Fact(DisplayName = "Maturity gate should use model semantic task type without rule term hit")]
    public void Evaluate_WithModelSemantic_ShouldTrustSemanticTaskTypeForPlanning()
    {
        var semantic = StrawberrySemantic(canBuildCandidate: true) with
        {
            SuggestedRoute = "属性分类 / OK-NG 判别路线"
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = "检测目标是果园里的成熟了的草莓，如果草莓熟透了，则为OK，否则为NG，输入源是相机。"
            },
            semantic);

        result.CanPlan.Should().BeTrue();
        result.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.ObjectSignals.Should().Contain(signal => signal.Contains("草莓", StringComparison.Ordinal));
        result.TaskSignals.Should().Contain(signal => signal.Contains("熟透", StringComparison.Ordinal));
        result.MissingFields.Should().NotContain("task_type");
    }

    [Fact(DisplayName = "Maturity gate should not let semantic CanBuildCandidate bypass missing engineering fields")]
    public void Evaluate_WithModelSemanticCandidate_ShouldStillBlockBuildWhenFieldsMissing()
    {
        var semantic = StrawberrySemantic(canBuildCandidate: true) with
        {
            ImageSource = string.Empty,
            OkCondition = string.Empty,
            NgCondition = string.Empty,
            SuggestedRoute = "属性分类 / OK-NG 判别路线"
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest { Description = "检测成熟草莓。" },
            semantic);

        result.CanPlan.Should().BeTrue();
        result.CanBuild.Should().BeFalse();
        result.MissingFields.Should().Contain(["image_source", "acceptance_criteria"]);
        result.MissingFields.Should().NotContain("task_type");
    }

    [Fact(DisplayName = "Semantic failure should fall back to legacy rule maturity")]
    public void Evaluate_WithSemanticFailure_ShouldUseRuleFallback()
    {
        var semantic = StrawberrySemantic(canBuildCandidate: false) with
        {
            Source = VisionAgentSemanticSources.RuleFallback,
            FailureCode = VisionAgentSemanticFailureCodes.JsonParseFailed
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest { Description = "检测成熟草莓。" },
            semantic);

        result.TaskType.Should().NotBe(AiVisionTaskTypes.AttributeClassification);
    }

    [Fact(DisplayName = "Rule fallback Plan should preserve semantic route for mature strawberry")]
    public async Task CreatePlanAsync_WithSemanticAttributeClassification_ShouldNotFallbackToSurfaceDefectRoute()
    {
        var orchestrator = CreateOrchestrator(
            Substitute.For<IAiFlowGenerationService>(),
            new FakeSemanticExtractor(StrawberrySemantic(canBuildCandidate: false)));

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "检测目标是果园里的成熟了的草莓，如果草莓熟透了，则为OK，否则为NG，输入源是相机。",
                OriginalUserPrompt = "检测目标是果园里的成熟了的草莓，如果草莓熟透了，则为OK，否则为NG，输入源是相机。"
            },
            CancellationToken.None);

        plan.SemanticExtraction.Should().NotBeNull();
        plan.SemanticExtraction!.Source.Should().Be(VisionAgentSemanticSources.Model);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        plan.RequirementMaturity.MissingFields.Should().NotContain("task_type");
        plan.RecommendedRoute.Title.Should().Contain("属性分类");
        plan.RecommendedRoute.Summary.Should().Contain("OK/NG");
        plan.RecommendedRoute.Operators.Should().NotContain("SurfaceDefectDetection");
        plan.PublicEvents.Select(evt => evt.Stage).Should().Contain("semantic_extraction");
    }

    [Fact(DisplayName = "Abstract ambition should stay non-plannable and enter decomposition")]
    public async Task AbstractAmbition_ShouldRemainNonPlannable()
    {
        const string prompt = "我想构建一个真正有野心的终极视觉检测方案";

        var result = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = prompt
        });

        result.CanPlan.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);

        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt
            },
            CancellationToken.None);

        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.CanPlan.Should().BeFalse();
        plan.RecommendedRoute.RouteId.Should().Be("requirement_decomposition");
        plan.RecommendedRoute.Operators.Should().BeEmpty();
    }

    [Theory(DisplayName = "Concrete known prompts should be plannable with expected task type")]
    [InlineData("检测包装箱胶带是否贴歪", AiVisionTaskTypes.SurfaceOrPoseDefect)]
    [InlineData("测量两个圆形孔位的圆心距离", AiVisionTaskTypes.GeometryMeasurement)]
    public void ConcreteKnownPrompt_ShouldBePlannable(string prompt, string expectedTaskType)
    {
        var result = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = prompt
        });

        result.CanPlan.Should().BeTrue();
        result.TaskType.Should().Be(expectedTaskType);
    }

    [Theory(DisplayName = "Explicit object target sentence patterns should create known slots")]
    [InlineData("检测外星人上的第三只竖眼", "外星人", "第三只竖眼")]
    [InlineData("判断异形水晶是否存在", "异形水晶", "是否存在")]
    public void ExplicitObjectTargetPatterns_ShouldCreateKnownSlots(
        string prompt,
        string expectedObject,
        string expectedTarget)
    {
        var result = VisionAgentRequirementMaturityGate.Evaluate(new VisionAgentRequirementMaturityRequest
        {
            Description = prompt
        });

        result.CanPlan.Should().BeTrue();
        result.CanBuild.Should().BeFalse();
        result.ObjectSignals.Should().Contain(expectedObject);
        result.TaskSignals.Should().Contain(expectedTarget);
        result.MissingFields.Should().NotContain("inspection_object");
        result.MissingFields.Should().NotContain("task_type");
    }

    [Fact(DisplayName = "Rule fallback router should open Plan for unknown explicit slots")]
    public async Task RuleFallbackRouter_ShouldOpenPlanForUnknownExplicitSlots()
    {
        var service = new VisionAgentIntentRouterService(
            new DelegateIntentCompletionSource((_, _) => throw new InvalidOperationException("router unavailable")),
            Microsoft.Extensions.Options.Options.Create(new VisionAgentIntentRouterOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VisionAgentIntentRouterService>.Instance);

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "检测目标是外星人，识别内容是额头上的第三只竖眼"
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RouterSource.Should().Be("rule_fallback");
        result.PublicReason.Should().Contain("模型路由不可用，当前为规则降级解析");
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.CanPlan.Should().BeTrue();
        result.RequirementMaturity.MissingFields.Should().NotContain("inspection_object");
        result.RequirementMaturity.MissingFields.Should().NotContain("task_type");
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

    private static VisionAgentOrchestrator CreateOrchestrator(
        IAiFlowGenerationService generationService,
        IVisionAgentSemanticExtractorService? semanticExtractor = null)
    {
        return new VisionAgentOrchestrator(
            new VisionAgentToolRegistry(
            [
                new OperatorCatalogTool(),
                new FlowTemplateMatchTool(),
                new FlowValidationTool()
            ]),
            generationService,
            semanticExtractor: semanticExtractor);
    }

    private static VisionAgentSemanticExtractionResult StrawberrySemantic(bool canBuildCandidate)
    {
        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            Confidence = 0.92,
            TaskTypeConfidence = 0.9,
            InspectionObject = "草莓",
            TargetAttribute = "成熟度/熟透",
            ImageSource = "相机",
            OkCondition = "草莓熟透了则为OK",
            NgCondition = "否则为NG",
            SuggestedRoute = "属性分类 / OK-NG 判别路线",
            CanPlanCandidate = true,
            CanBuildCandidate = canBuildCandidate,
            ObjectSignals = ["草莓"],
            TaskSignals = ["成熟度", "熟透"],
            MissingFields = [],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
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
        public bool? ExpectedCanPlan { get; init; }
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

    private sealed class FakeSemanticExtractor : IVisionAgentSemanticExtractorService
    {
        private readonly VisionAgentSemanticExtractionResult _result;

        public FakeSemanticExtractor(VisionAgentSemanticExtractionResult result)
        {
            _result = result;
        }

        public Task<VisionAgentSemanticExtractionResult> ExtractAsync(
            VisionAgentSemanticExtractionRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
