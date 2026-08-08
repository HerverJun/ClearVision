using System.Net;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentIntentRouter;

public sealed class VisionAgentIntentRouterTests
{
    [Fact(DisplayName = "Intent Router should keep casual chat out of Plan")]
    public async Task RouteAsync_ShouldKeepCasualChatOutOfPlan()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "casual_chat",
            confidence = "high",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = false,
            publicReason = "这是普通寒暄，不需要进入规划。",
            assistantReply = "我在。你可以描述要做的视觉检测、测量、识别或输出流程。",
            clarificationQuestions = Array.Empty<string>(),
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "hi" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentCasualChat);
        result.ShouldOpenPlan.Should().BeFalse();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RouterSource.Should().Be("model_router");
    }

    [Fact(DisplayName = "Intent Router should polish diagnostic assistant replies into natural chat")]
    public async Task RouteAsync_ShouldPolishDiagnosticAssistantReplyForCasualChat()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "casual_chat",
            confidence = "high",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = false,
            publicReason = "这是普通寒暄，不需要进入规划。",
            assistantReply = "这是普通寒暄，不需要进入规划。",
            clarificationQuestions = Array.Empty<string>(),
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "hi" },
            CancellationToken.None);

        result.AssistantReply.Should().Be("在的。你可以直接描述检测目标、缺陷类型、测量项或流程修改需求，我会先帮你规划方案。");
        result.AssistantReply.Should().NotContain("普通寒暄");
        result.AssistantReply.Should().NotContain("不需要进入规划");
        result.PublicReason.Should().Contain("普通寒暄");
    }

    [Fact(DisplayName = "Intent Router should promote slot-known ambiguous input to Plan without Build")]
    public async Task RouteAsync_ShouldKeepAmbiguousVisionRequirementNotBuildable()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "ambiguous_vision_requirement",
            confidence = "medium",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = true,
            publicReason = "需求信息不足，暂不可构建。",
            assistantReply = "请补充检测目标、缺陷、输入来源、OK/NG 规则。",
            clarificationQuestions = new[]
            {
                "检测目标是什么？",
                "需要判断哪些缺陷？",
                "输入来源和 OK/NG 规则是什么？"
            },
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "包装箱" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.CanPlan.Should().BeTrue();
    }

    [Fact(DisplayName = "Intent Router should not expose clarification questions for unknown semantic vision tasks")]
    public async Task RouteAsync_UnknownSemanticVisionTask_ShouldOpenPlanWithoutRouterClarification()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "ambiguous_vision_requirement",
            confidence = "medium",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = true,
            publicReason = "Need more details.",
            assistantReply = "Which robot guidance task?",
            clarificationQuestions = new[] { "What should the robot do?" },
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "为我构建一个视觉引导机械臂打螺钉的视觉项目。",
                SemanticExtraction = new VisionAgentSemanticExtractionResult
                {
                    IsVisionRequest = true,
                    Intent = "new_flow",
                    TaskType = "robot_guidance",
                    CanPlanCandidate = true,
                    CanBuildCandidate = false,
                    Source = VisionAgentSemanticSources.Model,
                    MetadataOnly = true
                }
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.CanPlan.Should().BeTrue();
        result.RequirementMaturity.CanBuild.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router should open Plan for abstract visual engineering goals")]
    public async Task RouteAsync_AbstractVisualGoal_ShouldOpenPlanWithoutRouterClarification()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "ambiguous_vision_requirement",
            confidence = "medium",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = true,
            publicReason = "Need requirement decomposition.",
            assistantReply = "I will organize the vision plan first.",
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "\u505a\u4e00\u4e2a\u5b8c\u6574\u89c6\u89c9\u68c0\u6d4b\u65b9\u6848"
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeFalse();
        result.RequirementMaturity.Should().NotBeNull();
        result.RequirementMaturity!.Maturity.Should().Be(AiRequirementMaturity.AbstractGoal);
        result.RequirementMaturity.TaskType.Should().Be(AiVisionTaskTypes.AbstractGoal);
        result.RequirementMaturity.CanPlan.Should().BeFalse();
        result.RequirementMaturity.CanBuild.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router should polish slot-known ambiguous diagnostic reply into Plan reply")]
    public async Task RouteAsync_ShouldPolishDiagnosticAssistantReplyForAmbiguousVision()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "ambiguous_vision_requirement",
            confidence = "medium",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = true,
            publicReason = "需求信息不足，暂不可构建。",
            assistantReply = "需求信息不足，暂不可构建。",
            clarificationQuestions = new[] { "检测目标是什么？" },
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "包装箱" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.AssistantReply.Should().Be("我先帮你整理规划方案。");
        result.AssistantReply.Should().NotContain("需求信息不足");
        result.AssistantReply.Should().NotContain("暂不可构建");
        result.CanBuild.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router should open Plan for actionable vision requirements")]
    public async Task RouteAsync_ShouldOpenPlanForActionableVisionRequirement()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "actionable_vision_plan",
            confidence = "high",
            shouldOpenPlan = true,
            shouldBuildDirectly = false,
            canBuild = true,
            needsClarification = false,
            publicReason = "输入包含可规划的视觉检测目标。",
            assistantReply = "已识别为可规划的视觉需求，将先进入 Plan 规划。",
            clarificationQuestions = Array.Empty<string>(),
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "为我构建包装箱外观视觉检测流程",
                Mode = "tool_loop"
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.RouterSource.Should().Be("model_router");
    }

    [Fact(DisplayName = "Intent Router should repair invalid JSON once")]
    public async Task RouteAsync_ShouldRepairInvalidJsonOnce()
    {
        var replies = new Queue<string>([
            "not-json",
            Json(new
            {
                intent = "help",
                confidence = "high",
                shouldOpenPlan = false,
                shouldBuildDirectly = false,
                canBuild = false,
                needsClarification = false,
                publicReason = "这是能力咨询。",
                assistantReply = "我可以先帮你梳理视觉检测需求。",
                clarificationQuestions = Array.Empty<string>(),
                fallbackAllowed = true
            })
        ]);
        var service = CreateService(_ => replies.Dequeue());

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "你好，你能做什么" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentHelp);
        result.RouterSource.Should().Be("model_router_repaired");
        result.FallbackReason.Should().Be("router_json_repaired");
        replies.Should().BeEmpty();
    }

    [Fact(DisplayName = "Intent Router Unauthorized should keep hi out of Plan by safe rule fallback")]
    public async Task RouteAsync_ShouldFallbackCasualWhenRouterUnauthorized()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized",
            null,
            HttpStatusCode.Unauthorized));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "hi" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentCasualChat);
        result.ShouldOpenPlan.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.RouterSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("router_unauthorized");
    }

    [Fact(DisplayName = "Intent Router Unauthorized should allow obvious vision requirement rule fallback Plan")]
    public async Task RouteAsync_ShouldFallbackActionableVisionWhenRouterUnauthorized()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized",
            null,
            HttpStatusCode.Unauthorized));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "为我构建包装箱外观视觉检测流程" },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentActionableVisionPlan);
        result.ShouldOpenPlan.Should().BeTrue();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.RouterSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Contain("router_unauthorized");
        result.FallbackReason.Should().Contain("planning_allowed_build_blocked");
    }

    [Fact(DisplayName = "Intent Router draft pending fields should not block confirmed Plan Build")]
    public async Task RouteAsync_DraftPendingFields_ShouldBuildFromConfirmedPlan()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "build_from_confirmed_plan",
            confidence = "high",
            shouldOpenPlan = false,
            shouldBuildDirectly = true,
            canBuild = true,
            needsClarification = false,
            publicReason = "Pending draft fields are allowed in draft mode.",
            assistantReply = "进入构建。",
            clarificationQuestions = Array.Empty<string>(),
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "开始 build",
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Draft,
                ResolvedPlanFields = [VisionAgentPlanAnswerFields.InspectionObject, VisionAgentPlanAnswerFields.TaskType],
                RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource, VisionAgentPlanAnswerFields.AcceptanceCriteria],
                SemanticExtraction = PendingDraftSemantic()
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentBuildFromConfirmedPlan);
        result.ShouldBuildDirectly.Should().BeTrue();
        result.NeedsClarification.Should().BeFalse();
        result.ShouldResetPendingPlan.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router draft confirmed Plan should build when only one hard fact is resolved and Planner route is legal")]
    public async Task RouteAsync_DraftSingleResolvedFieldWithPlannerRoute_ShouldBuildFromConfirmedPlan()
    {
        var service = CreateService(_ => Json(new
        {
            intent = "build_from_confirmed_plan",
            confidence = "high",
            shouldOpenPlan = false,
            shouldBuildDirectly = true,
            canBuild = true,
            needsClarification = false,
            publicReason = "Draft route has enough information to start Build.",
            assistantReply = "Starting Build.",
            clarificationQuestions = Array.Empty<string>(),
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "start build",
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Draft,
                PendingPlanSummary = Json(new
                {
                    route = new
                    {
                        operators = new[] { "ImageAcquisition", "SurfaceDefectDetection", "ResultOutput" }
                    }
                }),
                ConfirmedPlanAnswers =
                [
                    new VisionAgentPlanAnswer
                    {
                        Field = VisionAgentPlanAnswerFields.TaskType,
                        Value = AiVisionTaskTypes.SurfaceDefect,
                        Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
                    }
                ],
                ResolvedPlanFields = [VisionAgentPlanAnswerFields.TaskType],
                RemainingPlanFields =
                [
                    VisionAgentPlanAnswerFields.InspectionObject,
                    VisionAgentPlanAnswerFields.ImageSource,
                    VisionAgentPlanAnswerFields.AcceptanceCriteria
                ],
                SemanticExtraction = new VisionAgentSemanticExtractionResult
                {
                    IsVisionRequest = true,
                    Intent = "new_flow",
                    TaskType = AiVisionTaskTypes.SurfaceDefect,
                    Source = VisionAgentSemanticSources.RuleFallback,
                    MetadataOnly = true
                }
            },
            CancellationToken.None);

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentBuildFromConfirmedPlan);
        result.ShouldBuildDirectly.Should().BeTrue();
        result.NeedsClarification.Should().BeFalse();
        result.ShouldResetPendingPlan.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router HTTP fallback should preserve Pending Plan")]
    public async Task RouteAsync_HttpFallbackWithPendingPlan_ShouldNotResetPlan()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized",
            null,
            HttpStatusCode.Unauthorized));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "开始 build",
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Draft,
                PendingPlanHash = "sha256:pending",
                ResolvedPlanFields = [VisionAgentPlanAnswerFields.InspectionObject, VisionAgentPlanAnswerFields.TaskType],
                RemainingPlanFields = [VisionAgentPlanAnswerFields.ImageSource],
                SemanticExtraction = PendingDraftSemantic()
            },
            CancellationToken.None);

        result.RouterSource.Should().Be("rule_fallback");
        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentBuildFromConfirmedPlan);
        result.ShouldResetPendingPlan.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router should not trust stale resolved fields without value evidence")]
    public async Task RouteAsync_StaleResolvedFieldsWithoutEvidence_ShouldReturnRemainingBlockers()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized",
            null,
            HttpStatusCode.Unauthorized));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "\u75c5\u7076\u68c0\u6d4b",
                HasPendingPlan = true,
                RequirementMode = AiRequirementModes.Strict,
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

        result.RouterSource.Should().Be("rule_fallback");
        result.ResolvedPlanFields.Should().BeEquivalentTo([VisionAgentPlanAnswerFields.InspectionObject]);
        result.RemainingPlanFields.Should().Contain([
            VisionAgentPlanAnswerFields.TaskType,
            VisionAgentPlanAnswerFields.ImageSource,
            VisionAgentPlanAnswerFields.AcceptanceCriteria
        ]);
        result.CanBuild.Should().BeFalse();
        result.ShouldBuildDirectly.Should().BeFalse();
    }

    [Fact(DisplayName = "Intent Router should redact unsafe public text")]
    public async Task RouteAsync_ShouldRedactUnsafePublicText()
    {
        var unsafeText = "rawPrompt: hidden systemPrompt: hidden chain-of-thought " +
                         "C:\\factory\\image.png sk-secret-token token=abc headers=Authorization baseUrl=http://10.1.2.3 " +
                         "data:image/png;base64," + new string('A', 128) + " 192.168.1.9 DB1.DBX0.0";
        var service = CreateService(_ => Json(new
        {
            intent = "ambiguous_vision_requirement",
            confidence = "low",
            shouldOpenPlan = false,
            shouldBuildDirectly = false,
            canBuild = false,
            needsClarification = true,
            publicReason = unsafeText,
            assistantReply = unsafeText,
            clarificationQuestions = new[] { unsafeText },
            fallbackAllowed = true
        }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest { Description = "包装箱" },
            CancellationToken.None);
        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain("rawPrompt");
        json.Should().NotContain("systemPrompt");
        json.Should().NotContain("chain-of-thought");
        json.Should().NotContain("C:\\factory");
        json.Should().NotContain("sk-secret-token");
        json.Should().NotContain("token=");
        json.Should().NotContain("headers=");
        json.Should().NotContain("baseUrl=");
        json.Should().NotContain("data:image");
        json.Should().NotContain("192.168.1.9");
        json.Should().NotContain("DB1.DBX0.0");
    }

    [Fact(DisplayName = "Intent Router stage timeout should be distinct from caller abort")]
    public async Task RouteAsync_RouterTimeout_ShouldReturnExplicitTimeoutFallback()
    {
        var service = new VisionAgentIntentRouterService(
            new DelegateCompletionSource(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }),
            Options.Create(new VisionAgentIntentRouterOptions { Enabled = true, TimeoutSeconds = 1 }),
            NullLogger<VisionAgentIntentRouterService>.Instance);

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "检测包装表面划痕",
                SemanticExtraction = PendingDraftSemantic()
            },
            CancellationToken.None);

        result.RouterSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Contain("router_timeout");
    }

    [Fact(DisplayName = "Intent Router caller abort should propagate without becoming timeout fallback")]
    public async Task RouteAsync_CallerAbort_ShouldPropagateCancellation()
    {
        var service = new VisionAgentIntentRouterService(
            new DelegateCompletionSource(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }),
            Options.Create(new VisionAgentIntentRouterOptions { Enabled = true, TimeoutSeconds = 5 }),
            NullLogger<VisionAgentIntentRouterService>.Instance);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "检测包装表面划痕",
                SemanticExtraction = PendingDraftSemantic()
            },
            caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact(DisplayName = "Intent Router total planning budget should throw an explicit deadline exception")]
    public async Task RouteAsync_TotalBudgetExceeded_ShouldThrowPlanningDeadlineException()
    {
        var service = new VisionAgentIntentRouterService(
            new DelegateCompletionSource(async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return string.Empty;
            }),
            Options.Create(new VisionAgentIntentRouterOptions { Enabled = true, TimeoutSeconds = 2 }),
            NullLogger<VisionAgentIntentRouterService>.Instance,
            deadlineOptions: Options.Create(new VisionAgentPlanningDeadlineOptions
            {
                TotalBudgetMs = 120_000,
                ClientNetworkMarginMs = 1_000,
                MinimumRepairBudgetMs = 500
            }));

        var request = JsonSerializer.Deserialize<VisionAgentIntentRouterRequest>(JsonSerializer.Serialize(new
        {
            Description = "检测包装表面划痕",
            SemanticExtraction = PendingDraftSemantic(),
            PlanningBudgetMs = 1_000
        }))!;

        var act = () => service.RouteAsync(
            request,
            CancellationToken.None);

        var error = await act.Should().ThrowAsync<VisionAgentPlanningDeadlineExceededException>();
        error.Which.Stage.Should().Be("intent_router");
    }

    [Fact(DisplayName = "Intent Router repair should be skipped when total budget is below repair minimum")]
    public async Task RouteAsync_RepairBudgetInsufficient_ShouldSkipSecondCompletion()
    {
        var completionCalls = 0;
        var service = new VisionAgentIntentRouterService(
            new DelegateCompletionSource(async (_, cancellationToken) =>
            {
                completionCalls++;
                await Task.Delay(650, cancellationToken);
                return "not-json";
            }),
            Options.Create(new VisionAgentIntentRouterOptions { Enabled = true, TimeoutSeconds = 5 }),
            NullLogger<VisionAgentIntentRouterService>.Instance,
            deadlineOptions: Options.Create(new VisionAgentPlanningDeadlineOptions
            {
                TotalBudgetMs = 1_000,
                ClientNetworkMarginMs = 1_000,
                MinimumRepairBudgetMs = 500
            }));

        var result = await service.RouteAsync(
            new VisionAgentIntentRouterRequest
            {
                Description = "检测包装表面划痕",
                SemanticExtraction = PendingDraftSemantic()
            },
            CancellationToken.None);

        completionCalls.Should().Be(1);
        result.RouterSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().StartWith("router_repair_skipped_budget");
    }

    private static VisionAgentIntentRouterService CreateService(
        Func<VisionAgentIntentRouterCompletionRequest, string> completion)
    {
        return new VisionAgentIntentRouterService(
            new DelegateCompletionSource((request, _) => Task.FromResult(completion(request))),
            Options.Create(new VisionAgentIntentRouterOptions { Enabled = true, TimeoutSeconds = 5 }),
            NullLogger<VisionAgentIntentRouterService>.Instance);
    }

    private static string Json(object value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static VisionAgentSemanticExtractionResult PendingDraftSemantic()
    {
        return new VisionAgentSemanticExtractionResult
        {
            IsVisionRequest = true,
            Intent = "new_flow",
            TaskType = AiVisionTaskTypes.SurfaceDefect,
            Confidence = 0.82,
            TaskTypeConfidence = 0.8,
            InspectionObject = "logo area",
            DefectType = "appearance defect",
            ImageSource = string.Empty,
            OkCondition = string.Empty,
            NgCondition = string.Empty,
            OutputTarget = string.Empty,
            CanPlanCandidate = true,
            CanBuildCandidate = false,
            ObjectSignals = ["logo area"],
            TaskSignals = ["appearance defect"],
            Source = VisionAgentSemanticSources.Model,
            MetadataOnly = true
        };
    }

    private sealed class DelegateCompletionSource : IVisionAgentIntentRouterCompletionSource
    {
        private readonly Func<VisionAgentIntentRouterCompletionRequest, CancellationToken, Task<string>> _completion;

        public DelegateCompletionSource(
            Func<VisionAgentIntentRouterCompletionRequest, CancellationToken, Task<string>> completion)
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
