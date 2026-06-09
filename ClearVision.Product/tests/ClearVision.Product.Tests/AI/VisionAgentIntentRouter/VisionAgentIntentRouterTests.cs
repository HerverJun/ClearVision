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

    [Fact(DisplayName = "Intent Router should classify ambiguous vision input as not buildable")]
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

        result.Intent.Should().Be(VisionAgentIntentRouterService.IntentAmbiguousVisionRequirement);
        result.ShouldOpenPlan.Should().BeFalse();
        result.ShouldBuildDirectly.Should().BeFalse();
        result.CanBuild.Should().BeFalse();
        result.NeedsClarification.Should().BeTrue();
        result.ClarificationQuestions.Should().Contain(question => question.Contains("OK/NG", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Intent Router should polish ambiguous diagnostic reply into user clarification")]
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

        result.AssistantReply.Should().Contain("你想检测包装箱的哪一类问题");
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
        result.CanBuild.Should().BeTrue();
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
        result.CanBuild.Should().BeTrue();
        result.RouterSource.Should().Be("rule_fallback");
        result.FallbackReason.Should().Be("router_unauthorized");
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
