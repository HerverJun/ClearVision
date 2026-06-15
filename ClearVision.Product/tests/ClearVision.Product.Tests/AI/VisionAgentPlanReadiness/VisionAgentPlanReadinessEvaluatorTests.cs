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

    [Fact]
    public void Evaluate_StrictMissingImageSource_ShouldBlockAsHardRequirement()
    {
        var baseline = Plan([]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"],
                PublicReason = "Image source is required before strict Build."
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Strict);

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == "hard_requirement:image_source_missing" &&
            blocker.Category == VisionAgentBuildBlockerCategories.HardRequirement &&
            blocker.Field == VisionAgentPlanAnswerFields.ImageSource &&
            blocker.BlocksBuild &&
            blocker.ResolutionMode == VisionAgentBuildBlockerResolutionModes.AnswerQuestion);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_StrictAnsweredImageSource_ShouldResolve()
    {
        var baseline = Plan([]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            ClarificationQuestions =
            [
                Question(
                    "image_source",
                    VisionAgentPlanAnswerFields.ImageSource,
                    "camera",
                    "Camera")
            ],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Value = "camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validatedAnswers: validation,
            requirementMode: AiRequirementModes.Strict);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().NotContain(blocker => blocker.BlocksBuild);
        readiness.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.ImageSource);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_DraftMissingImageSourceWithAcquisitionRoute_ShouldRemainResourcePending()
    {
        var baseline = Plan(["hard_requirement:image_source_missing"]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"],
                PublicReason = "Image source can be bound later for draft Build."
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == "resource_pending:image_source_missing" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.Field == VisionAgentPlanAnswerFields.ImageSource &&
            blocker.BlocksBuild == false &&
            blocker.ResolutionMode == VisionAgentBuildBlockerResolutionModes.ProvideResource);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_DraftMissingImageSourceWithoutObjectOrTask_ShouldStillBlock()
    {
        var baseline = Plan([]);
        var plan = baseline with
        {
            Intent = AiVisionTaskTypes.Unknown,
            RecommendedRoute = baseline.RecommendedRoute with
            {
                Operators = ["ImageAcquisition", "ResultOutput"]
            },
            SemanticExtraction = baseline.SemanticExtraction! with
            {
                TaskType = AiVisionTaskTypes.Unknown,
                InspectionObject = string.Empty,
                ImageSource = string.Empty,
                ObjectSignals = [],
                TaskSignals = []
            },
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = false,
                CanBuild = false,
                TaskType = AiVisionTaskTypes.Unknown,
                ObjectSignals = [],
                TaskSignals = [],
                MissingFields =
                [
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.ImageSource
                ],
                BlockingReasons = ["inspection_object_missing", "task_type_missing", "image_source_missing"]
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().Contain(blocker =>
            blocker.BlocksBuild &&
            (blocker.Field == VisionAgentPlanAnswerFields.InspectionObject ||
             blocker.Field == VisionAgentPlanAnswerFields.TaskType));
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_RapidInspection_ShouldNotRequireExternalOutput()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("rapid inspection of capital letters"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_CapitalLetterOcr_ShouldNotRequireExternalOutput()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("capital letter OCR inspection"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ApiInspectionAsOrdinaryWordFragment_ShouldNotBlock()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("ApiInspection local OCR workflow"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExplicitMesOutput_ShouldBlockUntilAnswered()
    {
        var plan = ExternalOutputPlan("classify apples and send result to MES");

        var blocked = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        blocked.CanBuild.Should().BeFalse();
        blocked.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(blocked);

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
        ready.Blockers.Should().NotContain(blocker => blocker.BlocksBuild);
        AssertCanBuildInvariant(ready);
    }

    [Fact]
    public void Evaluate_AcceptedLocalOutputTarget_ShouldOverrideStaleExternalText()
    {
        var plan = ExternalOutputPlan("classify apples and send result to MES") with
        {
            ClarificationQuestions =
            [
                Question(
                    "output_target",
                    VisionAgentPlanAnswerFields.OutputTarget,
                    "local_result_payload",
                    "Local structured output")
            ]
        };
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new VisionAgentPlanAnswer
                {
                    QuestionId = "output_target",
                    Field = VisionAgentPlanAnswerFields.OutputTarget,
                    Value = "local_result_payload",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, validatedAnswers: validation);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().NotContain(blocker => blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExplicitPlcOutput_ShouldBlockUntilAnswered()
    {
        var plan = ExternalOutputPlan("write inspection result to PLC") with
        {
            Intent = AiVisionTaskTypes.PlcOutput,
            SemanticExtraction = ExternalOutputPlan("write inspection result to PLC").SemanticExtraction! with
            {
                TaskType = AiVisionTaskTypes.PlcOutput
            }
        };

        var blocked = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        blocked.CanBuild.Should().BeFalse();
        blocked.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(blocked);
    }

    [Fact]
    public void Evaluate_ExplicitHttpApiOutput_ShouldBlockUntilAnswered()
    {
        var plan = ExternalOutputPlan("send defect result to HTTP API endpoint");

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_LocalStructuredOutput_ShouldNotBlock()
    {
        var plan = LocalOutputPlan("classify apples") with
        {
            SemanticExtraction = LocalOutputPlan("classify apples").SemanticExtraction! with
            {
                OutputTarget = "local_result_payload"
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ConnectorMatingInspection_ShouldRemainLocal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("检测连接器对接到位"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExternalHousingInspection_ShouldRemainLocal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("external housing scratch inspection"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExternalLabelInspection_ShouldRemainLocal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("外部标签缺失检测"));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_LocalOutputWithAmbiguousObjectWords_ShouldRemainLocal()
    {
        var baseline = LocalOutputPlan("外部壳体连接器对接到位检测") with
        {
            SemanticExtraction = LocalOutputPlan("外部壳体连接器对接到位检测").SemanticExtraction! with
            {
                OutputTarget = "local_result_payload"
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(baseline);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExplicitMesOutput_ShouldRemainExternal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(ExternalOutputPlan("检测结果输出到 MES"));

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExplicitPlcWrite_ShouldRemainExternal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(ExternalOutputPlan("OK NG 结果写入 PLC"));

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ExplicitHttpApiOutput_ShouldRemainExternal()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(ExternalOutputPlan("调用 HTTP API 推送检测结果"));

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_DisabledPlcPolicy_ShouldRemainLocal()
    {
        var plan = LocalOutputPlan("classify apples") with
        {
            PlcOutputPolicy = "PLC disabled; local ResultOutput first; 不写入 PLC; 不对接业务系统"
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_SafetyBlocker_ShouldNotBecomeContractWarning()
    {
        var plan = Plan(
            ["safety_blocker:unsafe_operation"],
            [
                Question("unsafe_operation", "unsafe_operation", "acknowledged", "Unsafe operation")
            ]);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, acceptedRecommendedDefaults: true);

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == "safety_blocker:unsafe_operation" &&
            blocker.Category == VisionAgentBuildBlockerCategories.SafetyBlocker &&
            blocker.QuestionId == "unsafe_operation" &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ResourcePending_ShouldRemainNonBlocking()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(Plan(["resource_pending:model_resource_missing"]));

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Id == "resource_pending:model_resource" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_CanBuild_ShouldEqualAbsenceOfBlockingBlockers()
    {
        var snapshots = new[]
        {
            VisionAgentPlanReadinessEvaluator.Evaluate(Plan([])),
            VisionAgentPlanReadinessEvaluator.Evaluate(LocalOutputPlan("rapid inspection")),
            VisionAgentPlanReadinessEvaluator.Evaluate(ExternalOutputPlan("send result to MES")),
            VisionAgentPlanReadinessEvaluator.Evaluate(Plan(["safety_blocker:unsafe_operation"]))
        };

        snapshots.Should().OnlyContain(snapshot =>
            snapshot.CanBuild == !snapshot.Blockers.Any(blocker => blocker.BlocksBuild));
    }

    private static VisionAgentPlanModeResult LocalOutputPlan(string goal)
    {
        var baseline = Plan(
            ["strategy_confirmation:output_target_missing"],
            [
                Question(
                    "output_target",
                    VisionAgentPlanAnswerFields.OutputTarget,
                    "local_result_payload",
                    "Local structured output")
            ]);
        return baseline with
        {
            Goal = goal,
            OriginalUserPrompt = goal,
            SemanticExtraction = baseline.SemanticExtraction! with { OutputTarget = string.Empty }
        };
    }

    private static VisionAgentPlanModeResult ExternalOutputPlan(string goal)
    {
        var baseline = Plan(
            ["strategy_confirmation:output_target_missing"],
            [
                Question(
                    "output_target",
                    VisionAgentPlanAnswerFields.OutputTarget,
                    "business_system_output",
                    "Business system output")
            ]);
        return baseline with
        {
            Goal = goal,
            OriginalUserPrompt = goal,
            SemanticExtraction = baseline.SemanticExtraction! with { OutputTarget = string.Empty }
        };
    }

    private static void AssertCanBuildInvariant(VisionAgentBuildReadinessSnapshot readiness)
    {
        readiness.CanBuild.Should().Be(!readiness.Blockers.Any(blocker => blocker.BlocksBuild));
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
