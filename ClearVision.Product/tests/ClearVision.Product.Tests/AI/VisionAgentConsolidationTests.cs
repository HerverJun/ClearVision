using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Xunit;

namespace ClearVision.Product.Tests.AI;

public sealed class VisionAgentConsolidationTests
{
    // 1. 病灶检测
    [Fact]
    public void Evaluate_LesionDetection_ShouldEnterPlan()
    {
        // 语义模型成功 -> 直接进入 Plan
        var request = new VisionAgentRequirementMaturityRequest
        {
            Description = "为我构建一个病灶检测的视觉流程",
            RequirementMode = AiRequirementModes.Strict
        };
        var semantic = new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            InspectionObject = "病灶",
            TaskType = AiVisionTaskTypes.PresenceAbsence,
            CanPlanCandidate = true
        };

        var result = VisionAgentRequirementMaturityGate.Evaluate(request, semantic);
        result.CanPlan.Should().BeTrue();

        // 语义模型失败但规则抽取到明确对象/任务 -> 进入低置信 Plan
        // "病灶检测" through our regex will extract "病灶" as object
        var request2 = new VisionAgentRequirementMaturityRequest
        {
            Description = "病灶检测",
            RequirementMode = AiRequirementModes.Strict
        };
        var result2 = VisionAgentRequirementMaturityGate.Evaluate(request2, null);
        result2.CanPlan.Should().BeTrue();
        result2.ObjectSignals.Should().Contain("病灶");

        // 不出现固定病灶/包装箱/零件候选 in BuildMaturityQuestions/BuildFallbackQuestionsForRemaining
        var maturity = new AiRequirementMaturityResult
        {
            MissingFields = ["inspection_object", "task_type"],
            CanPlan = true
        };
        var questions = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(maturity.MissingFields);
        questions.Should().NotBeEmpty();
        foreach (var q in questions)
        {
            q.Options.Should().NotBeEmpty();
            q.Options.Should().Contain(option => option.Recommended && option.Value.EndsWith("_pending"));
            q.DefaultValue.Should().EndWith("_pending");
            q.DefaultAssumption.Should().Contain("\u6682\u65e0\u5b89\u5168\u9ed8\u8ba4");
            q.Options.Any(opt => opt.Label.Contains("病灶") || opt.Label.Contains("包装箱") || opt.Label.Contains("零件")).Should().BeFalse();
        }
    }

    // 2. 帮我识别一下问题、做个检测
    [Fact]
    public void Evaluate_AmbiguousRequests_ShouldNotEnterPlan()
    {
        var request1 = new VisionAgentRequirementMaturityRequest
        {
            Description = "帮我识别一下问题",
            RequirementMode = AiRequirementModes.Strict
        };
        var result1 = VisionAgentRequirementMaturityGate.Evaluate(request1, null);
        result1.CanPlan.Should().BeFalse();

        var request2 = new VisionAgentRequirementMaturityRequest
        {
            Description = "做个检测",
            RequirementMode = AiRequirementModes.Strict
        };
        var result2 = VisionAgentRequirementMaturityGate.Evaluate(request2, null);
        result2.CanPlan.Should().BeFalse();
    }

    // 3. Readiness
    [Fact]
    public void Readiness_ResolvedFieldsCheck()
    {
        var baselinePlan = new VisionAgentPlanModeResult
        {
            PlanId = "test_plan",
            Goal = "检测零件",
            Intent = "presence_absence",
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "test_route",
                Operators = ["ImageAcquisition", "ResultJudgment", "ResultOutput"] // contains task route
            },
            AcceptanceCriteria = ["流程包含采集、判定、输出"], // contains engineering criteria
            ClarificationQuestions = [],
            RemainingPlanFields = ["task_type", "acceptance_criteria"]
        };

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(baselinePlan);

        // 仅存在 RecommendedRoute 不得解决 task_type
        readiness.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.TaskType);

        // 仅存在 Plan 工程 AcceptanceCriteria 不得解决业务 acceptance_criteria
        readiness.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.AcceptanceCriteria);

        // strict blocking RemainingFields 存在时 CanBuild=false
        readiness.CanBuild.Should().BeFalse();
    }

    // 4. RemainingFields 为空
    [Fact]
    public void RemainingFieldsEmpty_ShouldReturnZeroQuestions()
    {
        // remaining fields 为空，NormalizeQuestions 必须返回零问题
        var questions = new List<VisionAgentClarificationQuestion>
        {
            new() { Id = "q1", Title = "问题1", Field = "inspection_object" }
        };
        var normalized = VisionAgentPlanFieldPolicy.NormalizeQuestions(
            questions,
            new List<string>(), // remaining empty
            new List<string>(),
            new List<VisionAgentPlanAnswer>());

        normalized.Should().BeEmpty();

        // rule_fallback returning zero questions when remaining empty
        var maturity = new AiRequirementMaturityResult
        {
            MissingFields = new List<string>(),
            CanPlan = true
        };
        var fallbackQuestions = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(maturity.MissingFields);
        fallbackQuestions.Should().BeEmpty();
    }

    // 5. rule_fallback
    [Fact]
    public void RuleFallback_ShouldOnlyAskRemainingFields()
    {
        // 只有 remaining fields 且非 resolved/confirmed 才能被询问
        var remaining = new List<string> { "image_source", "acceptance_criteria" };
        var resolved = new List<string> { "inspection_object" };
        var confirmed = new List<VisionAgentPlanAnswer>
        {
            new() { Field = "task_type", Value = "presence_absence" }
        };

        var allowed = remaining.Except(resolved).Except(confirmed.Select(c => c.Field)).ToList();
        var questions = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(allowed);

        questions.Select(q => q.Field).Should().BeEquivalentTo(allowed);
        questions.Any(q => q.Field == "inspection_object" || q.Field == "task_type").Should().BeFalse();

        // fallback 只提供自由输入，不包含推荐占位选项。
        foreach (var q in questions)
        {
            q.Options.Should().NotBeEmpty();
            q.Options.Should().Contain(option => option.Recommended && option.Value.EndsWith("_pending"));
            q.DefaultValue.Should().EndWith("_pending");
            q.DefaultAssumption.Should().Contain("\u6682\u65e0\u5b89\u5168\u9ed8\u8ba4");
        }
    }

    [Fact]
    public void PlaceholderValues_ShouldNotResolveAnswersOrRecommendedFallback()
    {
        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = "v2",
            PlanId = "placeholder_plan",
            ClarificationQuestions =
            [
                new()
                {
                    Id = "q_strategy",
                    Field = "algorithm_strategy",
                    Title = "strategy",
                    DefaultValue = "custom_input",
                    Options =
                    [
                        new()
                        {
                            Value = "custom_input",
                            Label = "custom",
                            Recommended = true
                        }
                    ]
                }
            ],
            RemainingPlanFields = ["algorithm_strategy"],
            BlockingReasons = ["strategy_confirmation:algorithm_strategy_missing"]
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [new() { QuestionId = "q_strategy", Field = "algorithm_strategy", Value = "custom_input", Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection }],
            new Dictionary<string, string> { ["q_strategy"] = "metadata_only" },
            acceptedRecommendedDefaults: true);

        validation.AcceptedAnswers.Should().BeEmpty();
        validation.ResolvedFields.Should().BeEmpty();
        validation.InvalidValues.Should().Contain(item => item.Contains("placeholder_value"));

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            acceptedRecommendedDefaults: true,
            validatedAnswers: validation);

        readiness.CanBuild.Should().BeFalse();
        readiness.ResolvedFields.Should().NotContain("algorithm_strategy");
        readiness.Blockers.Should().Contain(blocker =>
            blocker.Field == "algorithm_strategy" &&
            blocker.BlocksBuild);
    }

    // 6. Planner 越权过滤
    [Fact]
    public void ModelInferredAnswer_ShouldRemainInferenceAndNeverResolveReadiness()
    {
        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = "v2",
            PlanId = "model_inference_plan",
            ClarificationQuestions =
            [
                new()
                {
                    Id = "q_image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Title = "image source",
                    Options =
                    [
                        new() { Value = "station_camera", Label = "camera" },
                        new() { Value = "file_sample", Label = "file" }
                    ]
                }
            ],
            RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource],
            BlockingReasons = ["hard_requirement:image_source_missing"]
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            [
                new()
                {
                    QuestionId = "q_image_source",
                    Field = VisionAgentPlanAnswerFields.ImageSource,
                    Value = "station_camera",
                    Origin = VisionAgentPlanAnswerOrigins.ModelInferred
                }
            ],
            null,
            acceptedRecommendedDefaults: false);

        validation.AcceptedAnswers.Should().BeEmpty();
        validation.ResolvedFields.Should().BeEmpty();
        validation.Warnings.Should().Contain(item => item.Contains("non_authoritative_answer_ignored"));

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            acceptedRecommendedDefaults: false,
            validatedAnswers: validation);

        readiness.CanBuild.Should().BeFalse();
        readiness.ResolvedFields.Should().NotContain(VisionAgentPlanAnswerFields.ImageSource);
        readiness.Blockers.Should().Contain(blocker =>
            blocker.Field == VisionAgentPlanAnswerFields.ImageSource && blocker.BlocksBuild);
    }

    [Fact]
    public void FallbackPendingRecommendedOptions_ShouldStayVisibleButNotUnblockBuild()
    {
        var questions = VisionAgentPlanFieldPolicy.BuildFallbackQuestionsForRemaining(
            [
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                VisionAgentPlanAnswerFields.AlgorithmStrategy
            ]);

        questions.Select(question => question.Field).Should().BeEquivalentTo(
            [
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                VisionAgentPlanAnswerFields.AlgorithmStrategy
            ]);
        questions.Should().AllSatisfy(question =>
        {
            var recommended = question.Options.Single(option => option.Recommended);
            recommended.Value.Should().EndWith("_pending");
            recommended.Label.Should().MatchRegex("[^\\u0000-\\u007F]");
            recommended.Description.Should().MatchRegex("[^\\u0000-\\u007F]");
            recommended.Impact.Should().MatchRegex("[^\\u0000-\\u007F]");
            question.DefaultValue.Should().Be(recommended.Value);
            question.DefaultAssumption.Should().Contain("\u6682\u65e0\u5b89\u5168\u9ed8\u8ba4");
        });
        questions.Single(question => question.Field == VisionAgentPlanAnswerFields.ImageSource)
            .Options.Single(option => option.Recommended).Value.Should().Be("camera_pending");
        questions.Single(question => question.Field == VisionAgentPlanAnswerFields.AcceptanceCriteria)
            .Options.Single(option => option.Recommended).Value.Should().Be("ok_ng_pending");
        questions.Single(question => question.Field == VisionAgentPlanAnswerFields.AlgorithmStrategy)
            .Options.Single(option => option.Recommended).Value.Should().Be("strategy_pending");

        var plan = new VisionAgentPlanModeResult
        {
            PlanContractVersion = "v2",
            PlanId = "fallback_pending_plan",
            ClarificationQuestions = questions,
            RemainingPlanFields =
            [
                VisionAgentPlanAnswerFields.ImageSource,
                VisionAgentPlanAnswerFields.AcceptanceCriteria,
                VisionAgentPlanAnswerFields.AlgorithmStrategy
            ],
            BlockingReasons =
            [
                "hard_requirement:image_source_missing",
                "hard_requirement:acceptance_criteria_missing",
                "strategy_confirmation:algorithm_strategy_missing"
            ]
        };

        var validation = new VisionAgentPlanAnswerValidator().Validate(
            plan,
            questions.Select(question => new VisionAgentPlanAnswer
            {
                QuestionId = question.Id,
                Field = question.Field,
                Value = question.DefaultValue,
                Origin = VisionAgentPlanAnswerOrigins.AcceptedRecommendedDefault
            }).ToList(),
            new Dictionary<string, string>(),
            acceptedRecommendedDefaults: true);

        validation.AcceptedAnswers.Should().BeEmpty();
        validation.ResolvedFields.Should().BeEmpty();
        validation.InvalidValues.Should().Contain(value => value.Contains("placeholder_value"));

        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            acceptedRecommendedDefaults: true,
            validatedAnswers: validation);

        readiness.CanBuild.Should().BeFalse();
        readiness.ResolvedFields.Should().BeEmpty();
        readiness.Blockers.Should().Contain(blocker => blocker.BlocksBuild);
    }

    [Fact]
    public void NormalizeQuestions_ShouldFilterInvalidQuestions()
    {
        var remaining = new List<string> { "image_source" };
        var resolved = new List<string> { "inspection_object" };
        var confirmed = new List<VisionAgentPlanAnswer>
        {
            new() { Field = "task_type", Value = "presence_absence" }
        };

        var candidateQuestions = new List<VisionAgentClarificationQuestion>
        {
            new() { Id = "q1", Title = "已解决", Field = "inspection_object" }, // resolved
            new() { Id = "q2", Title = "非Remaining", Field = "output_target" }, // not in remaining
            new() { Id = "q3", Title = "有效", Field = "image_source" }, // valid
            new() { Id = "q4", Title = "重复有效", Field = "image_source" } // duplicate field
        };

        var result = VisionAgentPlanFieldPolicy.NormalizeQuestions(candidateQuestions, remaining, resolved, confirmed);
        result.Should().ContainSingle();
        result[0].Field.Should().Be("image_source");
    }

    // 9. Hash 兼容性
    [Fact]
    public void Hash_CompatibilityAndOrderIndependence()
    {
        var plan1 = new VisionAgentPlanModeResult
        {
            PlanId = "p1",
            Goal = "goal",
            Intent = "intent",
            PlanContractVersion = "v2",
            ResolvedPlanFields = ["inspection_object", "task_type"],
            ConfirmedPlanAnswers = [
                new() { Field = "image_source", Value = "camera" },
                new() { Field = "output_target", Value = "local" }
            ]
        };

        var plan2 = plan1 with
        {
            // 改变顺序
            ResolvedPlanFields = ["task_type", "inspection_object"],
            ConfirmedPlanAnswers = [
                new() { Field = "output_target", Value = "local" },
                new() { Field = "image_source", Value = "camera" }
            ]
        };

        var hash1 = VisionAgentOrchestrator.ComputePlanHash(plan1);
        var hash2 = VisionAgentOrchestrator.ComputePlanHash(plan2);
        hash1.Should().Be(hash2);

        // 答案改变
        var plan3 = plan1 with
        {
            ConfirmedPlanAnswers = [
                new() { Field = "image_source", Value = "file" },
                new() { Field = "output_target", Value = "local" }
            ]
        };
        var hash3 = VisionAgentOrchestrator.ComputePlanHash(plan3);
        hash1.Should().NotBe(hash3);

        var explicitV2Empty = new VisionAgentPlanModeResult
        {
            PlanContractVersion = "v2",
            PlanId = "p_v2_empty",
            Goal = "goal",
            Intent = "intent",
            ResolvedPlanFields = [],
            ConfirmedPlanAnswers = [],
            RemainingPlanFields = []
        };
        VisionAgentOrchestrator.ComputePlanHash(explicitV2Empty)
            .Should().NotBe(VisionAgentOrchestrator.ComputePlanHash(explicitV2Empty with { PlanContractVersion = "v1" }));

        var legacyJson = """
            {
              "planId": "p_legacy",
              "goal": "goal",
              "intent": "intent",
              "clarificationQuestions": [
                { "id": "q1", "title": "Q1" }
              ]
            }
            """;
        var planLegacy = JsonSerializer.Deserialize<VisionAgentPlanModeResult>(
            legacyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        planLegacy.PlanContractVersion.Should().BeEmpty();
        var hashLegacy = VisionAgentOrchestrator.ComputePlanHash(planLegacy);

        var planV1 = planLegacy with { PlanContractVersion = "v1" };
        var hashV1 = VisionAgentOrchestrator.ComputePlanHash(planV1);
        hashLegacy.Should().Be(hashV1);
    }

    // 10. 完整病灶补充
    [Fact]
    public void CompleteLesionFlow()
    {
        var plan = new VisionAgentPlanModeResult
        {
            PlanId = "lesion_plan",
            Goal = "病灶检测",
            Intent = "presence_absence",
            ResolvedPlanFields = ["inspection_object"], // object is resolved ("病灶")
            RemainingPlanFields = ["image_source", "task_type", "acceptance_criteria"],
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                InspectionObject = "病灶"
            },
            ConfirmedPlanAnswers = [],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "test_route",
                Operators = ["ImageAcquisition", "ResultJudgment", "ResultOutput"]
            }
        };

        // 10.1 用户提供 image_source=camera
        var answer1 = new VisionAgentPlanAnswer { Field = "image_source", Value = "camera", Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection };
        // 10.2 用户提供 task_type=presence_absence
        var answer2 = new VisionAgentPlanAnswer { Field = "task_type", Value = "presence_absence", Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection };
        // 10.3 用户提供 acceptance_criteria
        var answer3 = new VisionAgentPlanAnswer { Field = "acceptance_criteria", Value = "OK: 未检测到病灶；NG: 检测到病灶", Origin = VisionAgentPlanAnswerOrigins.ExplicitUserSelection };

        var validation = new VisionAgentPlanAnswerValidator().Validate(plan, [answer1, answer2, answer3], null, false);
        validation.ResolvedFields.Should().Contain(new[] { "image_source", "task_type", "acceptance_criteria" });

        var resolved = plan.ResolvedPlanFields.Concat(validation.ResolvedFields).ToList();
        var remaining = plan.RemainingPlanFields.Except(resolved).ToList();

        // RemainingFields 中不再包含这些字段
        remaining.Should().NotContain(new[] { "image_source", "task_type", "acceptance_criteria" });

        // Readiness 仍根据真实剩余阻断决定 CanBuild
        var readiness = VisionAgentPlanReadinessEvaluator.Evaluate(plan, validatedAnswers: validation);
        readiness.CanBuild.Should().BeFalse();
        var cameraResource = readiness.MissingResources.Should()
            .ContainSingle(resource => resource.ResourceType == "camera_binding")
            .Subject;

        var boundReadiness = VisionAgentPlanReadinessEvaluator.Evaluate(
            plan,
            validatedAnswers: validation,
            resourceDecisions:
            [
                new VisionAgentResourceDecision
                {
                    CanonicalId = cameraResource.CanonicalId,
                    Status = VisionAgentResourceStatuses.Bound,
                    ResourceType = cameraResource.ResourceType,
                    OperatorKey = cameraResource.OperatorKey,
                    ParameterName = cameraResource.ParameterName,
                    Source = "test_fixture"
                }
            ]);
        boundReadiness.CanBuild.Should().BeTrue();
    }
}
