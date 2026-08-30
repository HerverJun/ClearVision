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

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
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
        maturity.MissingFields.Should().Contain(["image_source", "acceptance_criteria", "algorithm_strategy"]);

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
            .Contain(["q_fallback_image_source", "ok_ng_rule", "classification_strategy"]);
        plan.ClarificationQuestions.Should().OnlyContain(question =>
            question.Options.Count > 0 &&
            question.Options.Count(option => option.Recommended) == 1);
        plan.ClarificationQuestions.Select(question => question.Id)
            .Should()
            .NotContain(["inspection_object", "task_type"]);
    }

    [Fact(DisplayName = "Create Plan should not trust stale resolved fields without value evidence")]
    public async Task CreatePlanAsync_StaleResolvedFieldsWithoutEvidence_ShouldRebuildRemainingFields()
    {
        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "\u75c5\u7076\u68c0\u6d4b",
                OriginalUserPrompt = "\u75c5\u7076\u68c0\u6d4b",
                ResolvedPlanFields =
                [
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria
                ],
                RemainingPlanFields = [],
                SemanticExtraction = new VisionAgentSemanticExtractionResult
                {
                    IsVisionRequest = true,
                    Intent = "new_flow",
                    InspectionObject = "\u75c5\u7076",
                    TaskType = AiVisionTaskTypes.Unknown,
                    Source = VisionAgentSemanticSources.RuleFallback,
                    MetadataOnly = true
                }
            },
            CancellationToken.None);

        plan.ResolvedPlanFields.Should().BeEquivalentTo([VisionAgentPlanAnswerFields.InspectionObject]);
        plan.RemainingPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);
        plan.CanBuild.Should().BeFalse();
        plan.ClarificationQuestions.Should().OnlyContain(question =>
            question.Options.Count > 0 &&
            question.Options.Count(option => option.Recommended) == 1);
    }

    [Fact(DisplayName = "Create Plan rule fallback should keep lesion detection task type remaining")]
    public async Task CreatePlanAsync_LesionDetectionRuleFallback_ShouldKeepTaskTypeRemaining()
    {
        var semanticExtractor = new FakeSemanticExtractor(new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            FailureCode = "semantic_model_failed",
            Source = VisionAgentSemanticSources.RuleFallback,
            MetadataOnly = true
        });
        var orchestrator = CreateOrchestrator(
            Substitute.For<IAiFlowGenerationService>(),
            semanticExtractor);

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "病灶检测",
                OriginalUserPrompt = "病灶检测"
            },
            CancellationToken.None);

        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.CanPlan.Should().BeTrue();
        plan.CanBuild.Should().BeFalse();
        plan.ResolvedPlanFields.Should().Contain(VisionAgentPlanAnswerFields.InspectionObject);
        plan.ResolvedPlanFields.Should().NotContain(VisionAgentPlanAnswerFields.TaskType);
        plan.RemainingPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);
        plan.RemainingPlanFields.Should().NotContain(VisionAgentPlanAnswerFields.AlgorithmStrategy);
    }

    [Fact(DisplayName = "Semantic output target alone should not satisfy acceptance criteria")]
    public void Evaluate_OutputTargetOnly_ShouldLeaveAcceptanceRemaining()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            InspectionObject = "part",
            ImageSource = "camera",
            OutputTarget = "local",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };

        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest { Description = "part defect inspection" },
            semantic);

        maturity.CanPlan.Should().BeTrue();
        maturity.CanBuild.Should().BeFalse();
        maturity.MissingFields.Should().Contain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        maturity.MissingFields.Should().NotContain(VisionAgentPlanAnswerFields.OutputTarget);
    }

    [Fact(DisplayName = "Output destination text should not satisfy acceptance criteria")]
    public void Evaluate_OutputDestinationText_ShouldNotResolveAcceptanceCriteria()
    {
        var maturity = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = "检测零件表面缺陷，图像来自相机，结果输出到本地"
            });

        maturity.CanPlan.Should().BeTrue();
        maturity.CanBuild.Should().BeFalse();
        maturity.MissingFields.Should().Contain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
    }

    [Fact(DisplayName = "Strict blocking fields should keep Maturity Plan and Readiness aligned")]
    public async Task StrictBlockingFields_ShouldKeepMaturityPlanAndReadinessAligned()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            InspectionObject = "part surface",
            DefectType = "surface defect",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            ObjectSignals = ["part surface"],
            TaskSignals = ["surface defect"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
        var request = new VisionAgentRequirementMaturityRequest
        {
            Description = "检测零件表面缺陷",
            RequirementMode = AiRequirementModes.Strict
        };

        var maturity = VisionAgentRequirementMaturityGate.Evaluate(request, semantic);
        var orchestrator = CreateOrchestrator(
            Substitute.For<IAiFlowGenerationService>(),
            new FakeSemanticExtractor(semantic));
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "检测零件表面缺陷",
                OriginalUserPrompt = "检测零件表面缺陷",
                RequirementMode = AiRequirementModes.Strict
            },
            CancellationToken.None);
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        maturity.CanBuild.Should().BeFalse();
        plan.CanBuild.Should().BeFalse();
        readiness.CanBuild.Should().BeFalse();
        maturity.MissingFields.Should().Contain([
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);
        plan.RemainingPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);

        var completeSemantic = semantic with
        {
            ImageSource = "camera",
            OkCondition = "OK when no visible defect",
            NgCondition = "NG when scratch or dent is present"
        };
        var completeMaturity = VisionAgentRequirementMaturityGate.Evaluate(request, completeSemantic);
        var completePlan = await CreateOrchestrator(
                Substitute.For<IAiFlowGenerationService>(),
                new FakeSemanticExtractor(completeSemantic))
            .CreatePlanAsync(
                new VisionAgentPlanModeRequest
                {
                    Description = "检测零件表面缺陷，图像来自相机，OK 无可见缺陷，NG 有划痕或凹陷",
                    OriginalUserPrompt = "检测零件表面缺陷，图像来自相机，OK 无可见缺陷，NG 有划痕或凹陷",
                    RequirementMode = AiRequirementModes.Strict
                },
                CancellationToken.None);
        var completeReadiness = VisionAgentPlanReadinessEvaluator.Evaluate(completePlan);

        completeMaturity.CanBuild.Should().BeTrue();
        completePlan.CanBuild.Should().BeFalse("the implementation route is inferred, not explicitly confirmed");
        completeReadiness.CanBuild.Should().BeFalse();
        completeMaturity.MissingFields.Should().BeEmpty();
        completePlan.RemainingPlanFields.Should().ContainSingle()
            .Which.Should().Be(VisionAgentPlanAnswerFields.AlgorithmStrategy);
        completePlan.ClarificationQuestions.Should().ContainSingle(question =>
            question.Field == VisionAgentPlanAnswerFields.AlgorithmStrategy);
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
        result.CanBuild.Should().BeTrue();
        result.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.ObjectSignals.Should().Contain(signal => signal.Contains("草莓", StringComparison.Ordinal));
        result.TaskSignals.Should().Contain(signal => signal.Contains("熟透", StringComparison.Ordinal));
        result.MissingFields.Should().NotContain("task_type");
        result.MissingFields.Should().NotContain("model_or_rule_strategy");
        result.BlockingReasons.Should().NotContain("model_or_rule_strategy_missing");
    }

    [Fact(DisplayName = "Maturity gate should not require semantic implementation strategy before Plan")]
    public void Evaluate_WithModelSemanticWithoutSuggestedRoute_ShouldStillAllowBuildFacts()
    {
        var semantic = StrawberrySemantic(canBuildCandidate: false) with
        {
            SuggestedRoute = string.Empty
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = "检测果园里的草莓，熟透为 OK，否则 NG，输入源是相机。"
            },
            semantic);

        result.CanPlan.Should().BeTrue();
        result.CanBuild.Should().BeTrue();
        result.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.MissingFields.Should().NotContain("model_or_rule_strategy");
        result.BlockingReasons.Should().NotContain("model_or_rule_strategy_missing");
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

    [Fact(DisplayName = "Maturity gate should only ask semantic fields that are actually missing")]
    public void Evaluate_WithSemanticImageSourceOnly_ShouldNotAskImageSourceAgain()
    {
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.Unknown,
            Confidence = 0.8,
            ImageSource = "camera",
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest { Description = "use camera input" },
            semantic);

        result.CanPlan.Should().BeTrue();
        result.CanBuild.Should().BeFalse();
        result.MissingFields.Should().Contain(["inspection_object", "task_type", "acceptance_criteria"]);
        result.MissingFields.Should().NotContain("image_source");
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

    [Theory(DisplayName = "Model semantic route cases should not cross scenario routes")]
    [InlineData("tape_skew", AiVisionTaskTypes.SurfaceDefect, "SurfaceDefectDetection", true)]
    [InlineData("hole_distance", AiVisionTaskTypes.GeometryMeasurement, "Measurement", false)]
    [InlineData("terminal_wire_sequence", AiVisionTaskTypes.WireSequence, "DetectionSequenceJudge", false)]
    [InlineData("qr_code", AiVisionTaskTypes.CodeRecognition, "CodeRecognition", false)]
    [InlineData("missing_part", AiVisionTaskTypes.PresenceAbsence, "DeepLearning", false)]
    public async Task CreatePlanAsync_WithModelSemanticRouteCases_ShouldNotCrossScenario(
        string id,
        string taskType,
        string expectedOperator,
        bool expectSurfaceDefectRoute)
    {
        var semantic = SemanticRouteCase(id, taskType);
        var orchestrator = CreateOrchestrator(
            Substitute.For<IAiFlowGenerationService>(),
            new FakeSemanticExtractor(semantic));

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = $"{id} inspection from camera with OK/NG condition",
                OriginalUserPrompt = $"{id} inspection from camera with OK/NG condition"
            },
            CancellationToken.None);

        plan.SemanticExtraction.Should().NotBeNull();
        plan.SemanticExtraction!.Source.Should().Be(VisionAgentSemanticSources.Model);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.TaskType.Should().Be(taskType);
        plan.RecommendedRoute.Operators.Should().Contain(expectedOperator);
        if (!expectSurfaceDefectRoute)
        {
            plan.RecommendedRoute.Operators.Should().NotContain("SurfaceDefectDetection");
        }
    }

    [Fact(DisplayName = "Router semantic extraction should be reused by Plan without duplicate extractor call")]
    public async Task RouteThenPlan_WithSemanticExtraction_ShouldNotCallSemanticExtractorTwice()
    {
        var semanticExtractor = new FakeSemanticExtractor(new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.AttributeClassification,
            Confidence = 0.93,
            TaskTypeConfidence = 0.91,
            InspectionObject = "strawberry",
            TargetAttribute = "maturity",
            ImageSource = "camera",
            OkCondition = "ripe is OK",
            NgCondition = "otherwise NG",
            SuggestedRoute = "attribute classification OK/NG route",
            CanPlanCandidate = true,
            CanBuildCandidate = false,
            ObjectSignals = ["strawberry"],
            TaskSignals = ["maturity", "ripe"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        });
        var router = new VisionAgentIntentRouterService(
            new DelegateIntentCompletionSource((_, _) => throw new InvalidOperationException("router should be disabled")),
            Microsoft.Extensions.Options.Options.Create(new VisionAgentIntentRouterOptions { Enabled = false }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VisionAgentIntentRouterService>.Instance,
            semanticExtractor);

        var routerResult = await router.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "classify strawberry maturity from camera",
                OriginalUserPrompt = "classify strawberry maturity from camera"
            },
            CancellationToken.None);
        var orchestrator = CreateOrchestrator(
            Substitute.For<IAiFlowGenerationService>(),
            semanticExtractor);

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "classify strawberry maturity from camera",
                OriginalUserPrompt = "classify strawberry maturity from camera",
                SemanticExtraction = routerResult.SemanticExtraction
            },
            CancellationToken.None);

        semanticExtractor.CallCount.Should().Be(1);
        routerResult.SemanticExtraction.Should().NotBeNull();
        plan.SemanticExtraction.Should().NotBeNull();
        plan.SemanticExtraction!.Source.Should().Be(routerResult.SemanticExtraction!.Source);
        plan.SemanticExtraction.TaskType.Should().Be(routerResult.SemanticExtraction.TaskType);
        plan.SemanticExtraction.InspectionObject.Should().Be(routerResult.SemanticExtraction.InspectionObject);
        plan.SemanticExtraction.TargetAttribute.Should().Be(routerResult.SemanticExtraction.TargetAttribute);
        plan.SemanticExtraction.OkCondition.Should().Be(routerResult.SemanticExtraction.OkCondition);
        plan.SemanticExtraction.NgCondition.Should().Be(routerResult.SemanticExtraction.NgCondition);
        plan.SemanticExtraction.ImageSource.Should().Be(routerResult.SemanticExtraction.ImageSource);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        plan.RequirementMaturity.MissingFields.Should().NotContain("task_type");
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

    [Fact(DisplayName = "Semantic unknown vision task should still be plannable but not buildable")]
    public void SemanticUnknownVisionTask_ShouldPlanButBlockBuild()
    {
        var result = VisionAgentRequirementMaturityGate.Evaluate(
            new VisionAgentRequirementMaturityRequest
            {
                Description = "为我构建一个视觉引导机械臂打螺钉的视觉项目。"
            },
            new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                Intent = "new_flow",
                TaskType = "robot_guidance",
                TaskTypeConfidence = 0.72,
                CanPlanCandidate = true,
                CanBuildCandidate = false,
                Source = VisionAgentSemanticSources.Model,
                MetadataOnly = true
            });

        result.CanPlan.Should().BeTrue();
        result.CanBuild.Should().BeFalse();
        result.TaskType.Should().Be(AiVisionTaskTypes.Unknown);
        result.MissingFields.Should().Contain([
            VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);
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

    [Fact(DisplayName = "Intent Router lesion detection should match final Plan remaining fields")]
    public async Task RouteThenPlan_LesionDetectionRuleFallback_ShouldKeepStateConsistent()
    {
        var router = new VisionAgentIntentRouterService(
            new DelegateIntentCompletionSource((_, _) => throw new InvalidOperationException("router unavailable")),
            Microsoft.Extensions.Options.Options.Create(new VisionAgentIntentRouterOptions { Enabled = true }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VisionAgentIntentRouterService>.Instance);

        var routerResult = await router.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "病灶检测",
                OriginalUserPrompt = "病灶检测"
            },
            CancellationToken.None);

        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());
        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = "病灶检测",
                OriginalUserPrompt = "病灶检测",
                SemanticExtraction = routerResult.SemanticExtraction
            },
            CancellationToken.None);

        routerResult.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        routerResult.ShouldOpenPlan.Should().BeTrue();
        routerResult.CanBuild.Should().BeFalse();
        routerResult.ResolvedPlanFields.Should().Contain(VisionAgentPlanAnswerFields.InspectionObject);
        routerResult.ResolvedPlanFields.Should().NotContain(VisionAgentPlanAnswerFields.TaskType);
        routerResult.RemainingPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);

        plan.CanBuild.Should().BeFalse();
        plan.ResolvedPlanFields.Should().BeEquivalentTo(routerResult.ResolvedPlanFields);
        plan.RemainingPlanFields.Should().Contain(routerResult.RemainingPlanFields);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.CanBuild.Should().Be(routerResult.RequirementMaturity!.CanBuild);
    }

    [Fact(DisplayName = "Intent Router should open Plan but block Build for over-confident abstract intents")]
    public async Task RouteAsync_ShouldOpenPlanButBlockBuildForOverconfidentAbstractIntent()
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

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);
        result.RequirementMaturity.CanPlan.Should().BeFalse();
        result.DecisionTrace.Should().NotBeNull();
        result.DecisionTrace!.MaturityLevel.Should().Be(AiRequirementMaturity.AbstractGoal);
        result.DecisionTrace.FallbackReason.Should().Contain("planning_allowed_maturity_needs_plan");
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
        plan.CurrentPhase.Should().Be(VisionAgentPlanPhases.ClarificationOnly);
        plan.Intent.Should().Be(AiRequirementMaturity.AbstractGoal);
        plan.RequirementMaturity.Should().NotBeNull();
        plan.RequirementMaturity!.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);
        plan.RecommendedRoute.RouteId.Should().Be("requirement_decomposition");
        plan.RecommendedRoute.Operators.Should().BeEmpty();
        plan.RecommendedRoute.Title.Should().NotContain("表面缺陷");
        plan.BlockingReasons.Should().Contain("abstract_goal_needs_decomposition");
        plan.ClarificationQuestions.Count.Should().BeLessThanOrEqualTo(3);
        plan.ClarificationQuestions.Should().AllSatisfy(question =>
        {
            question.Options.Count.Should().BeInRange(2, 5);
            question.Options.Count(option => option.Recommended).Should().Be(1);
        });
    }

    [Fact(DisplayName = "BuildFromPlan should not fall back to legacy generation when Build execution is unavailable")]
    public async Task BuildFromPlanAsync_ShouldNotFallbackToLegacyGeneration()
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
                return Task.FromResult(new AiFlowGenerationResult
                {
                    Success = true,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                    GenerationMode = "build_from_plan_entry_reached",
                    Flow = new OperatorFlowDto()
                });
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
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        result.FailureSummary!.Code.Should().Be(VisionAgentBuildFailureCodes.BuildOrchestratorNotRegistered);
        generationCalls.Should().Be(0);
    }

    [Fact(DisplayName = "Fruit label classification should ask only route and judgment gaps")]
    public async Task CreatePlanAsync_FruitClassification_ShouldKeepExplicitFactsAndAskHighValueGaps()
    {
        const string prompt = "帮我构建一个超市水果标签识别的视觉流程，实现相机输入水果并输出水果类型";
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.Classification,
            InspectionObject = "水果",
            ImageSource = "相机",
            OutputTarget = "水果类型",
            OkCondition = "最高置信类别有效",
            NgCondition = "低置信度无效",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            Source = VisionAgentSemanticSources.Model
        };
        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt,
                SemanticExtraction = semantic
            },
            CancellationToken.None);

        plan.ConfirmedPlanAnswers.Select(answer => answer.Field).Should().Contain([
            VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.OutputTarget
        ]);
        plan.ConfirmedPlanAnswers.Should().OnlyContain(answer =>
            answer.Origin == VisionAgentPlanAnswerOrigins.ExplicitUserText);
        plan.RemainingPlanFields.Should().BeEquivalentTo([
            VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.AlgorithmStrategy
        ]);
        plan.ClarificationQuestions.Select(question => question.Field).Should().BeEquivalentTo([
            VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.AlgorithmStrategy
        ]);
        plan.ClarificationQuestions.Should().HaveCount(2);
        plan.ClarificationQuestions.Should().AllSatisfy(question =>
        {
            question.Options.Count.Should().BeInRange(2, 5);
            question.Options.Count(option => option.Recommended).Should().Be(1);
        });
    }

    [Fact(DisplayName = "DataMatrix request should not treat model-inferred source, judgment, or output as confirmed")]
    public async Task CreatePlanAsync_DataMatrix_ShouldAskThreeScenarioSpecificQuestions()
    {
        const string prompt = "读取产品上的DataMatrix二维码";
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.CodeRecognition,
            InspectionObject = "产品",
            ImageSource = "工站相机",
            OkCondition = "解码成功",
            NgCondition = "不可读",
            OutputTarget = "解码文本",
            CanPlanCandidate = true,
            CanBuildCandidate = true,
            Source = VisionAgentSemanticSources.Model
        };
        var orchestrator = CreateOrchestrator(Substitute.For<IAiFlowGenerationService>());

        var plan = await orchestrator.CreatePlanAsync(
            new VisionAgentPlanModeRequest
            {
                Description = prompt,
                OriginalUserPrompt = prompt,
                SemanticExtraction = semantic,
                ConfirmedPlanAnswers =
                [
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.ImageSource,
                        Value = "station_camera",
                        Origin = VisionAgentPlanAnswerOrigins.ModelInferred
                    }
                ]
            },
            CancellationToken.None);

        plan.ConfirmedPlanAnswers.Should().NotContain(answer =>
            answer.Origin == VisionAgentPlanAnswerOrigins.ModelInferred);
        plan.ResolvedPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType
        ]);
        plan.ResolvedPlanFields.Should().NotContain([
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.OutputTarget
        ]);
        plan.RemainingPlanFields.Should().BeEquivalentTo([
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.OutputTarget
        ]);
        plan.ClarificationQuestions.Select(question => question.Field).Should().BeEquivalentTo(
            plan.RemainingPlanFields);
        plan.ClarificationQuestions.Should().HaveCount(3);
        plan.ClarificationQuestions.Single(question => question.Field == VisionAgentPlanAnswerFields.AcceptanceCriteria)
            .Title.Should().Contain("不可读");
        plan.ClarificationQuestions.Single(question => question.Field == VisionAgentPlanAnswerFields.OutputTarget)
            .Title.Should().Contain("交付");
    }

    [Fact(DisplayName = "GenerateFlow WebMessage adapter should delegate low maturity input to generation service")]
    public async Task GenerateFlowMessageHandler_ShouldDelegateLowMaturityDirectRequest()
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

        var json = await handler.HandleAsTestOwnerAsync(
            "我想构建一个真正有野心的终极视觉检测方案。",
            mode: GenerateFlowMode.New,
            useVisionAgentGenerateFlow: true);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeTrue();
        generationCalls.Should().Be(1);
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
        _ = generationService;
        return new VisionAgentOrchestrator(
            new VisionAgentToolRegistry(
            [
                new OperatorCatalogTool(),
                new FlowTemplateMatchTool(),
                new FlowValidationTool()
            ]),
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

    private static VisionAgentSemanticExtractionResult SemanticRouteCase(string id, string taskType)
    {
        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = taskType,
            Confidence = 0.9,
            TaskTypeConfidence = 0.9,
            InspectionObject = id,
            TargetAttribute = taskType == AiVisionTaskTypes.AttributeClassification ? "attribute" : string.Empty,
            MeasurementTarget = taskType == AiVisionTaskTypes.GeometryMeasurement ? "distance" : string.Empty,
            DefectType = taskType is AiVisionTaskTypes.SurfaceDefect or AiVisionTaskTypes.SurfaceOrPoseDefect ? "pose_or_surface" : string.Empty,
            ImageSource = "camera",
            OkCondition = "condition met is OK",
            NgCondition = "otherwise NG",
            OutputTarget = "OK/NG",
            SuggestedRoute = string.Empty,
            CanPlanCandidate = true,
            CanBuildCandidate = false,
            ObjectSignals = [id],
            TaskSignals = [taskType],
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

        public int CallCount { get; private set; }

        public Task<VisionAgentSemanticExtractionResult> ExtractAsync(
            VisionAgentSemanticExtractionRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
