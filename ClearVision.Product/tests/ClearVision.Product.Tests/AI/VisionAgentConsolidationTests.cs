using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using System.Collections.Generic;
using System.Linq;
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
            q.Options.Should().ContainSingle(opt => opt.Value == "custom_input" && opt.Label == "自定义输入");
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
        
        // options 不包含固定业务枚举（仅包含 custom_input）
        foreach (var q in questions)
        {
            q.Options.Should().ContainSingle(opt => opt.Value == "custom_input");
        }
    }

    // 6. Planner 越权过滤
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
            PlanId = "p1", Goal = "goal", Intent = "intent", PlanContractVersion = "v2",
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

        // 无版本旧快照 (PlanContractVersion=V2 默认)
        var planLegacy = new VisionAgentPlanModeResult
        {
            PlanId = "p_legacy", Goal = "goal", Intent = "intent",
            PlanContractVersion = "v2", // defaults to V2
            // No V2 fields
            ResolvedPlanFields = [], ConfirmedPlanAnswers = [], RemainingPlanFields = [],
            ClarificationQuestions = [
                new() { Id = "q1", Title = "Q1", Field = "" } // questions have no fields
            ]
        };
        var hashLegacy = VisionAgentOrchestrator.ComputePlanHash(planLegacy);

        var planV1 = planLegacy with { PlanContractVersion = "v1" };
        var hashV1 = VisionAgentOrchestrator.ComputePlanHash(planV1);
        hashLegacy.Should().Be(hashV1); // must fall back to V1 hash because there are no V2 fields
    }

    // 10. 完整病灶补充
    [Fact]
    public void CompleteLesionFlow()
    {
        var plan = new VisionAgentPlanModeResult
        {
            PlanId = "lesion_plan", Goal = "病灶检测", Intent = "presence_absence",
            ResolvedPlanFields = ["inspection_object"], // object is resolved ("病灶")
            RemainingPlanFields = ["image_source", "task_type", "acceptance_criteria"],
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
        readiness.CanBuild.Should().BeTrue();
    }
}
