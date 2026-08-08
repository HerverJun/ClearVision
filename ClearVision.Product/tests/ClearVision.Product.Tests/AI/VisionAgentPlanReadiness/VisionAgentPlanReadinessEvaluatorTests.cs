using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI.VisionAgentPlanReadiness;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
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
                    "image_folder",
                    "Image folder")
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
                    Value = "image_folder",
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
    public void Evaluate_RecommendedResolveField_ShouldResolveBlocker()
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
                    "file_sample",
                    "File sample",
                    VisionAgentClarificationAnswerEffects.ResolveField)
            ],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(plan, [], null, true);
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            acceptedRecommendedDefaults: true,
            validatedAnswers: validation,
            requirementMode: AiRequirementModes.Strict);

        validation.AcceptedAnswers.Should().ContainSingle(answer =>
            answer.Field == VisionAgentPlanAnswerFields.ImageSource &&
            answer.Value == "file_sample");
        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().NotContain(blocker => blocker.BlocksBuild);
    }

    [Fact]
    public void Evaluate_RecommendedDefer_ShouldNotResolveBlockerOrEnterConfirmedAnswers()
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
                    "camera_pending",
                    "Keep pending",
                    VisionAgentClarificationAnswerEffects.Defer)
            ],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(plan, [], null, true);
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            acceptedRecommendedDefaults: true,
            validatedAnswers: validation,
            requirementMode: AiRequirementModes.Strict);

        validation.AcceptedAnswers.Should().BeEmpty();
        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().Contain(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.ImageSource &&
            blocker.BlocksBuild);
    }

    [Fact]
    public void Validate_AcceptedRecommendedDefaults_ShouldSkipDeferAndInformational()
    {
        var plan = Plan([]) with
        {
            ClarificationQuestions =
            [
                Question(
                    "image_source",
                    VisionAgentPlanAnswerFields.ImageSource,
                    "camera_pending",
                    "Keep pending",
                    VisionAgentClarificationAnswerEffects.Defer),
                Question(
                    "algorithm_strategy",
                    VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    "strategy_note",
                    "Read note",
                    VisionAgentClarificationAnswerEffects.Informational),
                Question(
                    "acceptance_criteria",
                    VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    "defect_is_ng",
                    "Defect is NG",
                    VisionAgentClarificationAnswerEffects.ResolveField)
            ]
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(plan, [], null, true);

        validation.AcceptedAnswers.Should().ContainSingle(answer =>
            answer.Field == VisionAgentPlanAnswerFields.AcceptanceCriteria &&
            answer.Value == "defect_is_ng");
        validation.AcceptedAnswers.Should().NotContain(answer =>
            answer.Value == "camera_pending" ||
            answer.Value == "strategy_note");
    }

    [Fact]
    public void Validate_LegacyOptionWithoutAnswerEffect_ShouldInferPendingAsDeferAndConcreteAsResolve()
    {
        var plan = Plan([]) with
        {
            ClarificationQuestions =
            [
                Question("image_source", VisionAgentPlanAnswerFields.ImageSource, "camera_pending", "Pending"),
                Question("acceptance_criteria", VisionAgentPlanAnswerFields.AcceptanceCriteria, "defect_is_ng", "Defect is NG")
            ]
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(plan, [], null, true);

        validation.AcceptedAnswers.Should().ContainSingle(answer =>
            answer.Field == VisionAgentPlanAnswerFields.AcceptanceCriteria &&
            answer.Value == "defect_is_ng");
        validation.AcceptedAnswers.Should().NotContain(answer => answer.Value == "camera_pending");
    }

    [Fact]
    public void Evaluate_StationCamera_ShouldResolveImageSourceAndKeepCameraBindingResourceBlocker()
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
                    "station_camera",
                    "Station camera",
                    VisionAgentClarificationAnswerEffects.ResolveField)
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
                    Value = "station_camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validatedAnswers: validation,
            requirementMode: AiRequirementModes.Strict);

        readiness.ResolvedFields.Should().Contain(VisionAgentPlanAnswerFields.ImageSource);
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null && blocker.Resource.ResourceType == "camera_binding" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.ResolutionMode == VisionAgentBuildBlockerResolutionModes.ProvideResource &&
            blocker.BlocksBuild);
        readiness.CanBuild.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_StationCameraDraft_ShouldAllowEditableDraftButKeepResourcePending()
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
                    "station_camera",
                    "Station camera",
                    VisionAgentClarificationAnswerEffects.ResolveField)
            ],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
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
                    Value = "station_camera",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validatedAnswers: validation,
            requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null && blocker.Resource.ResourceType == "camera_binding" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.BlocksBuild == false);
    }

    [Fact]
    public void Evaluate_StrictModelAndTemplateResources_ShouldExposeDistinctCanonicalTasks()
    {
        var baseline = Plan(["resource_pending:model_resource_missing", "resource_pending:template_artifact_missing"]);
        var plan = baseline with
        {
            RecommendedRoute = baseline.RecommendedRoute with
            {
                Operators = ["ImageAcquisition", "OnnxInference", "TemplateMatching", "ResultOutput"]
            }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Strict);

        readiness.CanBuild.Should().BeFalse();
        readiness.MissingResources.Should().HaveCount(2);
        readiness.MissingResources.Select(item => item.CanonicalId).Should().OnlyHaveUniqueItems();
        readiness.MissingResources.Should().Contain(item =>
            item.ResourceType == "model_resource" &&
            item.OperatorKey == "onnxinference#1" &&
            item.ParameterName == "ModelPath" &&
            item.ResolutionTarget == VisionAgentResourceResolutionTargets.ModelPicker);
        readiness.MissingResources.Should().Contain(item =>
            item.ResourceType == "template_artifact" &&
            item.OperatorKey == "templatematching#1" &&
            item.ParameterName == "Template" &&
            item.ResolutionTarget == VisionAgentResourceResolutionTargets.TemplatePicker);
    }

    [Fact]
    public void Evaluate_BoundCanonicalResource_ShouldReleaseStrictBuildGate()
    {
        var plan = Plan(["resource_pending:model_resource_missing"]);
        var blocked = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Strict);
        var model = blocked.MissingResources.Should().ContainSingle(item => item.ResourceType == "model_resource").Subject;

        var ready = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            requirementMode: AiRequirementModes.Strict,
            resourceDecisions:
            [
                new VisionAgentResourceDecision
                {
                    CanonicalId = model.CanonicalId,
                    Status = VisionAgentResourceStatuses.Bound,
                    ResourceType = model.ResourceType,
                    OperatorKey = model.OperatorKey,
                    ParameterName = model.ParameterName,
                    Source = "resource_binding"
                }
            ]);

        ready.CanBuild.Should().BeTrue();
        ready.MissingResources.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_DraftableModelResource_ShouldAllowDraftButKeepDeployRunBlock()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            Plan(["resource_pending:model_resource_missing"]),
            requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeTrue();
        readiness.MissingResources.Should().ContainSingle(item =>
            item.ResourceType == "model_resource" &&
            item.BlockingScope == VisionAgentResourceBlockingScopes.DeployRun);
        readiness.PrimaryMessage.Should().Contain("部署和运行");
    }

    [Fact]
    public void Evaluate_NonDraftableExternalResource_ShouldStillBlockDraft()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            Plan(["resource_pending:plc_output_missing"]),
            requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeFalse();
        readiness.MissingResources.Should().ContainSingle(item =>
            item.ResourceType == "plc_output" &&
            item.DraftPolicy == VisionAgentResourceDraftPolicies.BuildRequired);
    }

    [Fact]
    public void Evaluate_LegacyAndRouteCameraSignals_ShouldMergeByCanonicalIdentity()
    {
        var baseline = Plan(["resource_pending:station_camera_configuration_or_identifier"]);
        var plan = baseline with
        {
            ClarificationQuestions =
            [
                Question("image_source", VisionAgentPlanAnswerFields.ImageSource, "station_camera", "Station camera", VisionAgentClarificationAnswerEffects.ResolveField)
            ]
        };
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [new VisionAgentPlanAnswer { QuestionId = "image_source", Field = VisionAgentPlanAnswerFields.ImageSource, Value = "station_camera", Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection }],
            null,
            false);

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, validatedAnswers: validation);

        readiness.MissingResources.Should().ContainSingle(item => item.ResourceType == "camera_binding");
        readiness.Blockers.Count(item => item.Category == VisionAgentBuildBlockerCategories.ResourcePending).Should().Be(1);
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
            blocker.Resource != null && blocker.Resource.ResourceType == "camera_binding" &&
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
    public void Evaluate_ResourcePending_ShouldBlockInStrictMode()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(Plan(["resource_pending:model_resource_missing"]));

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource!.ResourceType == "model_resource" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ResourcePending_ShouldRemainNonBlockingInDraftWhenRouteCanGenerate()
    {
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            Plan(["resource_pending:model_resource_missing"]),
            requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource!.ResourceType == "model_resource" &&
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.BlocksBuild == false &&
            blocker.ResolutionMode == VisionAgentBuildBlockerResolutionModes.ProvideResource);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_StrawberryDraftWithRepairablePlannerRoute_ShouldAllowEditableDraft()
    {
        var plan = StrawberryDraftPlan();
        var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["classification_strategy"] = "strategy_pending",
            ["ok_ng_rule"] = "threshold_pending",
            ["q_fallback_image_source"] = "camera_pending"
        };
        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            plan.ConfirmedPlanAnswers,
            selections,
            false);
        var effective = new VisionAgentPlanRequirementOverlay().Build(
            plan,
            validation,
            new VisionAgentRequirementMaturityRequest
            {
                Description = plan.OriginalUserPrompt,
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Draft
            });

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validation.BuildDecisions,
            validatedAnswers: validation,
            effectiveRequirement: effective,
            requirementMode: AiRequirementModes.Draft);

        effective.Maturity.CanPlan.Should().BeTrue();
        validation.DeferredFields.Should().Contain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        validation.AcceptedAnswers.Should().NotContain(answer =>
            answer.Field == VisionAgentPlanAnswerFields.AlgorithmStrategy);
        effective.Values.Should().NotContainKey(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        effective.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        effective.Maturity.MissingFields.Should().Contain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        readiness.CanBuild.Should().BeTrue();
        readiness.ResolvedFields.Should().Contain([
            VisionAgentPlanAnswerFields.InspectionObject,
            VisionAgentPlanAnswerFields.TaskType
        ]);
        readiness.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.AcceptanceCriteria);
        readiness.RemainingFields.Should().BeEquivalentTo([
            VisionAgentPlanAnswerFields.AcceptanceCriteria,
            VisionAgentPlanAnswerFields.AlgorithmStrategy,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.OutputTarget
        ]);
        readiness.Blockers.Should().ContainSingle(blocker =>
            blocker.Resource != null &&
            blocker.Resource.ResourceType == "camera_binding" &&
            blocker.Resource.DraftPolicy == VisionAgentResourceDraftPolicies.DraftAllowed &&
            blocker.Resource.BlockingScope == VisionAgentResourceBlockingScopes.DeployRun &&
            blocker.BlocksBuild == false);
        readiness.Blockers.Should().NotContain(blocker => blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_StrawberryStrictWithIncompletePlannerRoute_ShouldRemainBlocked()
    {
        var plan = StrawberryDraftPlan();

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            requirementMode: AiRequirementModes.Strict);

        readiness.CanBuild.Should().BeFalse();
        readiness.Blockers.Should().Contain(blocker =>
            blocker.Resource != null &&
            blocker.Resource.ResourceType == "camera_binding" &&
            blocker.BlocksBuild);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_NestedCanonicalLocalOutputResource_ShouldNotCreateABogusBuildRequiredResource()
    {
        var plan = Plan([
            "resource_pending:resource:v1|resource|output_target_pending:local_output_interface_or_format|output_target"
        ]) with
        {
            Goal = "Inspect packaging damage and keep output local.",
            OriginalUserPrompt = "Inspect packaging damage and keep output local.",
            SemanticExtraction = Plan([]).SemanticExtraction! with { OutputTarget = "local_structured_result" }
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Draft);

        readiness.CanBuild.Should().BeTrue();
        readiness.Blockers.Should().NotContain(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending &&
            blocker.Field == VisionAgentPlanAnswerFields.OutputTarget);
        readiness.Blockers.Should().Contain(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ContractWarning &&
            blocker.BlocksBuild == false);
        AssertCanBuildInvariant(readiness);
    }

    [Fact]
    public void Evaluate_ChineseMissingCameraOrSample_ShouldUseOneStableCameraBinding()
    {
        var baseline = Plan(["resource_pending:未提供现场相机或代表性图像样本"]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };

        var strict = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            requirementMode: AiRequirementModes.Strict);
        var draft = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            requirementMode: AiRequirementModes.Draft);

        strict.CanBuild.Should().BeFalse();
        draft.CanBuild.Should().BeTrue();
        strict.MissingResources.Should().ContainSingle();
        draft.MissingResources.Should().ContainSingle();
        strict.MissingResources.Single().Should().Match<VisionAgentResourceRequirement>(resource =>
            resource.ResourceType == "camera_binding" &&
            resource.DraftPolicy == VisionAgentResourceDraftPolicies.DraftAllowed &&
            resource.ResolutionTarget == VisionAgentResourceResolutionTargets.CameraSettings);
        draft.MissingResources.Single().CanonicalId.Should().Be(strict.MissingResources.Single().CanonicalId);
        strict.MissingResources.Should().NotContain(resource => resource.ResourceType == "resource");
        draft.MissingResources.Should().NotContain(resource => resource.ResourceType == "resource");
    }

    [Fact]
    public void CanonicalIdentity_ShouldBeIdempotentAcrossRoundTripAndRepeatedEvaluation()
    {
        const string canonical = "resource:v1|camera_binding|imageacquisition#1|camera_binding_id";
        VisionAgentResourceIdentity.CreateCanonicalId(canonical, string.Empty, string.Empty)
            .Should().Be(canonical);

        var baseline = Plan(["resource_pending:未提供现场相机或代表性图像样本"]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };

        var identities = new List<string>();
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Draft);
            readiness.MissingResources.Should().ContainSingle();
            identities.Add(readiness.MissingResources.Single().CanonicalId);
            plan = plan with { BlockingReasons = readiness.Blockers.Select(blocker => blocker.Id).ToList() };
        }

        identities.Distinct(StringComparer.OrdinalIgnoreCase).Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_PollutedLegacyCanonicalId_ShouldMergeAndCameraBindingShouldReleaseAllGenerations()
    {
        const string canonical = "resource:v1|camera_binding|imageacquisition#1|camera_binding_id";
        var baseline = Plan([
            "resource_pending:resource:v1|resource|resourcev1resourceglobalresource|resource",
            $"resource_pending:{canonical}"
        ]);
        var plan = baseline with
        {
            SemanticExtraction = baseline.SemanticExtraction! with { ImageSource = string.Empty },
            RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource],
            RequirementMaturity = baseline.RequirementMaturity! with
            {
                CanPlan = true,
                CanBuild = false,
                MissingFields = [VisionAgentPlanAnswerFields.ImageSource],
                BlockingReasons = ["image_source_missing"]
            }
        };

        var pending = VisionAgentPlanReadinessEvaluator.Evaluate(plan, requirementMode: AiRequirementModes.Strict);
        var bound = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            requirementMode: AiRequirementModes.Strict,
            resourceDecisions:
            [
                new VisionAgentResourceDecision
                {
                    CanonicalId = canonical,
                    ResourceType = "camera_binding",
                    OperatorKey = "imageacquisition#1",
                    ParameterName = "CameraBindingId",
                    Status = VisionAgentResourceStatuses.Bound,
                    Source = "test"
                }
            ]);

        pending.MissingResources.Should().ContainSingle(resource =>
            resource.CanonicalId == canonical && resource.ResourceType == "camera_binding");
        bound.MissingResources.Should().BeEmpty();
        bound.Blockers.Should().NotContain(blocker =>
            blocker.Category == VisionAgentBuildBlockerCategories.ResourcePending);
        bound.CanBuild.Should().BeTrue();
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

    private static VisionAgentPlanModeResult StrawberryDraftPlan()
    {
        return new VisionAgentPlanModeResult
        {
            PlanId = "plan_strawberry_draft",
            OriginalUserPrompt = "为我构建一个检测果园里草莓是否成熟的视觉检测应用。",
            Goal = "构建一个检测果园中草莓成熟度的视觉检测应用。",
            Intent = AiVisionTaskTypes.AttributeClassification,
            Confidence = "medium",
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "strawberry_maturity_attribute_classification",
                Operators = ["imageacquisition", "colorconversion", "roimanager", "thresholding", "blobanalysis"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions =
            [
                DeferredQuestion("classification_strategy", VisionAgentPlanAnswerFields.AlgorithmStrategy, "model_strategy", "strategy_pending"),
                DeferredQuestion("ok_ng_rule", VisionAgentPlanAnswerFields.AcceptanceCriteria, "use_extracted_conditions", "threshold_pending"),
                DeferredQuestion("q_fallback_image_source", VisionAgentPlanAnswerFields.ImageSource, "station_camera", "camera_pending")
            ],
            ConfirmedPlanAnswers =
            [
                new VisionAgentPlanAnswer
                {
                    Field = VisionAgentPlanAnswerFields.InspectionObject,
                    Value = "草莓",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                },
                new VisionAgentPlanAnswer
                {
                    QuestionId = "classification_strategy",
                    Field = VisionAgentPlanAnswerFields.AlgorithmStrategy,
                    Value = "model_strategy",
                    Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection
                }
            ],
            ResolvedPlanFields = [VisionAgentPlanAnswerFields.InspectionObject],
            RemainingPlanFields =
            [
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.TaskType,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                VisionAgentPlanAnswerFields.OutputTarget,
                VisionAgentPlanAnswerFields.AlgorithmStrategy
            ],
            BlockingReasons = ["resource_pending:image_source_missing"],
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                TaskType = AiVisionTaskTypes.AttributeClassification,
                InspectionObject = "草莓",
                TargetAttribute = "成熟度",
                OkCondition = "草莓已成熟",
                NgCondition = "草莓未成熟",
                CanPlanCandidate = false,
                CanBuildCandidate = false,
                ObjectSignals = ["果园环境", "草莓果实", "草莓"],
                TaskSignals = ["成熟度判断", "视觉检测", "OK/NG分类", "成熟度"],
                Source = VisionAgentSemanticSources.Model,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.AttributeClassification,
                CanPlan = true,
                CanBuild = false,
                ObjectSignals = ["果园环境", "草莓果实", "草莓"],
                TaskSignals = ["成熟度判断", "视觉检测", "OK/NG分类", "成熟度"],
                MissingFields =
                [
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.TaskType,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria,
                    VisionAgentPlanAnswerFields.OutputTarget,
                    VisionAgentPlanAnswerFields.AlgorithmStrategy
                ],
                MetadataOnly = true
            },
            MetadataOnly = true
        };
    }

    private static VisionAgentClarificationQuestion DeferredQuestion(
        string id,
        string field,
        string resolvedValue,
        string deferredValue)
    {
        return new VisionAgentClarificationQuestion
        {
            Id = id,
            Field = field,
            Options =
            [
                new VisionAgentClarificationOption
                {
                    Value = resolvedValue,
                    Label = resolvedValue,
                    AnswerEffect = VisionAgentClarificationAnswerEffects.ResolveField
                },
                new VisionAgentClarificationOption
                {
                    Value = deferredValue,
                    Label = deferredValue,
                    AnswerEffect = VisionAgentClarificationAnswerEffects.Defer
                }
            ]
        };
    }

    private static VisionAgentClarificationQuestion Question(
        string id,
        string field,
        string recommendedValue,
        string label,
        string answerEffect = "")
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
                    AnswerEffect = answerEffect,
                    Description = label,
                    Impact = "Editable draft can continue."
                }
            ]
        };
    }
}
