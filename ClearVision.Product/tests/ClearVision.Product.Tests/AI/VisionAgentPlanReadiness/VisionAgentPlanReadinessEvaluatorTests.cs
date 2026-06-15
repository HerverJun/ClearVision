using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanReadiness;

public sealed class VisionAgentPlanReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_UnknownUnmappedBlocker_ShouldBecomeNonBlockingContractWarning()
    {
        var plan = Plan(["new_business_knob_missing"]);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.BlocksBuild == false &&
            blocker.Id == "contract_warning:new_business_knob");
    }

    [Fact]
    public void Evaluate_PlannerCandidateNotBuildable_ShouldNotBlockBuild()
    {
        var plan = Plan(["strategy_confirmation:planner_candidate_not_buildable"]);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == "contract_warning:planner_candidate_not_buildable" &&
            blocker.BlocksBuild == false);
        readiness.Blockers.Should().NotContain(blocker =>
            blocker.Id.StartsWith("strategy_confirmation:planner_candidate_not_buildable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_LocalOutputTargetQuestion_ShouldNotBlockBuild()
    {
        var plan = Plan(
            ["strategy_confirmation:output_target_missing"],
            [
                Question(
                    "output_target",
                    VisionAgentPlanAnswerFields.OutputTarget,
                    "local_result_payload",
                    "Local structured output")
            ]) with
        {
            SemanticExtraction = Plan([]).SemanticExtraction! with { OutputTarget = string.Empty }
        };
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeTrue();
        readiness.PrimaryMessage.Should().Be("规划已完成，可以开始构建。");
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.QuestionId == "output_target" &&
            blocker.BlocksBuild == false);
    }

    [Fact]
    public void Evaluate_ExternalOutputTargetQuestion_ShouldBlockUntilAnswered()
    {
        var plan = Plan(
            ["strategy_confirmation:output_target_missing"],
            [
                Question(
                    "output_target",
                    VisionAgentPlanAnswerFields.OutputTarget,
                    "business_system_output",
                    "Business system output")
            ]) with
        {
            Goal = "classify supermarket apples and send result to MES",
            SemanticExtraction = Plan([]).SemanticExtraction! with { OutputTarget = string.Empty }
        };
        var blocked = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        blocked.CanBuild.Should().BeFalse();
        blocked.PrimaryMessage.Should().Be("请选择输出目标。");
        blocked.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.QuestionId == "output_target" &&
            blocker.BlocksBuild);

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "output_target",
                    Field = VisionAgentPlanAnswerFields.OutputTarget,
                    Value = "business_system_output",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var ready = VisionAgentPlanReadinessEvaluator.Evaluate(plan, validatedAnswers: validation);

        ready.CanBuild.Should().BeTrue();
        ready.Blockers.Where(blocker => blocker.BlocksBuild).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_UnknownQuestionIdBlocker_ShouldResolveByAnsweredQuestionId()
    {
        var plan = Plan(
            ["strategy_confirmation:line_guidance_profile_missing"],
            [
                Question(
                    "line_guidance_profile",
                    "line_guidance_profile",
                    "profile_a",
                    "Profile A")
            ]);
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "line_guidance_profile",
                    Field = "line_guidance_profile",
                    Value = "profile_a",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, validatedAnswers: validation);

        readiness.CanBuild.Should().BeTrue();
        validation.AcceptedAnswers.Should().ContainSingle(answer =>
            answer.QuestionId == "line_guidance_profile" &&
            answer.Value == "profile_a");
    }

    private static VisionAgentPlanModeResult Plan(
        List<string> blockingReasons,
        List<VisionAgentClarificationQuestion>? questions = null)
    {
        return new VisionAgentPlanModeResult
        {
            PlanId = "plan_readiness_test",
            Goal = "classify supermarket apples",
            Intent = AiVisionTaskTypes.Classification,
            Confidence = "high",
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "classification_route",
                Title = "Classification route",
                Summary = "Classify apple categories.",
                Operators = ["ImageAcquisition", "DeepLearning", "ResultOutput"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions = questions ?? [],
            CanBuild = false,
            BlockingReasons = blockingReasons,
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                Intent = "new_flow",
                TaskType = AiVisionTaskTypes.Classification,
                InspectionObject = "supermarket apple",
                ImageSource = "camera",
                OkCondition = "apple category identified",
                OutputTarget = "local_result_payload",
                CanPlanCandidate = true,
                CanBuildCandidate = true,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Actionable,
                TaskType = AiVisionTaskTypes.Classification,
                CanPlan = true,
                CanBuild = true,
                ObjectSignals = ["apple"],
                TaskSignals = ["classification"],
                MissingFields = [],
                BlockingReasons = [],
                PublicReason = "Requirement is actionable.",
                MetadataOnly = true
            },
            MetadataOnly = true
        };
    }

    private static VisionAgentClarificationQuestion Question(
        string id,
        string field,
        string recommendedValue,
        string label)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Field = field,
            Title = label,
            DefaultValue = recommendedValue,
            Options =
            [
                new VisionAgentClarificationOption
                {
                    Value = recommendedValue,
                    Label = label,
                    Recommended = true,
                    Description = label,
                    Impact = "Editable draft can continue."
                }
            ]
        };
    }
}
