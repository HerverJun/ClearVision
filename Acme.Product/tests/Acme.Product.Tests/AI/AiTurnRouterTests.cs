using Acme.Product.Core.DTOs;
using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public class AiTurnRouterTests
{
    private readonly AiTurnRouter _router = new();

    [Theory]
    [InlineData("hi")]
    [InlineData("你好")]
    [InlineData("在吗")]
    [InlineData("帮我做什么")]
    public void Route_WithHighConfidenceChat_ShouldShortCircuit(string text)
    {
        var route = _router.Route(new AiTurnRouteRequest(
            text,
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);
        route.ShouldShortCircuit.Should().BeTrue();
        route.Confidence.Should().Be(AiRouterConfidence.High);
    }

    [Fact]
    public void Route_WithGreetingAndBusinessRequest_ShouldCreateNewFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "你好，帮我做缺陷检测",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        route.ShouldShortCircuit.Should().BeFalse();
    }

    [Fact]
    public void Route_WithExistingFlowAndChineseDisplayRequest_ShouldModifyFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "把算子名称改成中文",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ModifyFlow);
        route.ShouldBypassClarification.Should().BeTrue();
    }

    [Fact]
    public void Route_WithExistingFlowAndExplicitNewMode_ShouldCreateNewFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "新增一个缺陷检测流程",
            null,
            GenerateFlowMode.New,
            Session: null,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        route.ShouldBypassClarification.Should().BeFalse();
    }

    [Fact]
    public void Route_WithExistingFlowAndExplicitNewFlowText_ShouldCreateNewFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "新增一个缺陷检测流程",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
    }

    [Fact]
    public void Route_WithExistingFlowAndOperatorAddText_ShouldModifyFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "新增一个过滤算子到当前流程",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ModifyFlow);
        route.ShouldBypassClarification.Should().BeTrue();
    }

    [Fact]
    public void Route_WithExistingFlowAndRestartWordingForCurrentFlowEdit_ShouldModifyFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "重新调整当前流程阈值",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ModifyFlow);
        route.ShouldBypassClarification.Should().BeTrue();
    }

    [Fact]
    public void Route_WithNoFlowAndExplicitNewFlowText_ShouldCreateNewFlow()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "新增一个缺陷检测流程",
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        route.ShouldShortCircuit.Should().BeFalse();
    }

    [Fact]
    public void Route_WithNoFlowAndExplicitExplainMode_ShouldGuideInsteadOfBypassingClarification()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "解释当前流程",
            null,
            GenerateFlowMode.Explain,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.Unknown);
        route.ShouldShortCircuit.Should().BeTrue();
        route.ShouldBypassClarification.Should().BeFalse();
        route.Reply.Should().Contain("当前没有可解释");
    }

    [Fact]
    public void Route_WithPendingClarificationAnswer_ShouldClassifyClarificationAnswer()
    {
        var session = BuildPendingClarificationSession();

        var route = _router.Route(new AiTurnRouteRequest(
            "检测对象：包装箱",
            null,
            GenerateFlowMode.Auto,
            session,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ClarificationAnswer);
    }

    [Fact]
    public void Route_WithChatAfterPendingClarification_ShouldKeepPreviousClarificationAnswerable()
    {
        var session = BuildPendingClarificationSession();
        session.History.Add(new ConversationTurn
        {
            Role = "assistant",
            Payload = new ConversationTurnPayload
            {
                Kind = "assistant_interaction",
                TurnIntent = AiTurnIntents.ChatOrHelp,
                InteractionState = AiInteractionStates.Idle,
                Reply = "我在。"
            }
        });

        var route = _router.Route(new AiTurnRouteRequest(
            "检测对象：包装箱",
            null,
            GenerateFlowMode.Auto,
            session,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ClarificationAnswer);
    }

    [Fact]
    public void Route_WithQueuedHintAndGreeting_ShouldStillClassifyChat()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "你好",
            "需求澄清上下文：检测对象：包装箱；缺陷类别：破损；请不要把示例当作用户答案。",
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);
        route.ShouldShortCircuit.Should().BeTrue();
    }

    [Fact]
    public void Route_WithQueuedHintAndVagueText_ShouldContinueGeneration()
    {
        var route = _router.Route(new AiTurnRouteRequest(
            "继续",
            "需求澄清上下文：检测对象：包装箱；缺陷类别：破损；请不要把示例当作用户答案。",
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.Unknown);
        route.InteractionState.Should().Be(AiInteractionStates.Generating);
        route.ShouldShortCircuit.Should().BeFalse();
    }

    [Theory]
    [InlineData("审核待确认参数")]
    [InlineData("把算子名称改成中文")]
    public void Route_WithReviewOrModifySignalsWithoutFlow_ShouldGuideInsteadOfBypassingClarification(string text)
    {
        var route = _router.Route(new AiTurnRouteRequest(
            text,
            null,
            GenerateFlowMode.Auto,
            Session: null,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.Unknown);
        route.ShouldShortCircuit.Should().BeTrue();
        route.ShouldBypassClarification.Should().BeFalse();
        route.Reply.Should().Contain("当前没有");
    }

    [Fact]
    public void Route_WithPendingClarificationAndNewFullRequirement_ShouldResetToNewFlow()
    {
        var session = new ConversationSession { SessionId = "s1" };
        session.History.Add(new ConversationTurn
        {
            Role = "assistant",
            Payload = new ConversationTurnPayload
            {
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationQuestions =
                    [
                        new AiClarificationQuestion
                        {
                            Field = "object_type",
                            Question = "请补充检测对象是什么。",
                            Required = true
                        }
                    ]
                }
            }
        });

        var route = _router.Route(new AiTurnRouteRequest(
            "重新做一个连接器线序检测流程",
            null,
            GenerateFlowMode.Auto,
            session,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
    }

    [Fact]
    public void Route_WithPendingClarificationAndVagueNewFlowRequest_ShouldResetToNewFlow()
    {
        var session = BuildPendingClarificationSession();

        var route = _router.Route(new AiTurnRouteRequest(
            "帮我构建一个流程",
            null,
            GenerateFlowMode.Auto,
            session,
            HasExistingFlow: false,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        route.ShouldShortCircuit.Should().BeFalse();
    }

    [Fact]
    public void Route_WithPendingManualRetryDraft_ShouldClassifyManualRetryRepair()
    {
        var session = new ConversationSession { SessionId = "s1" };
        session.History.Add(new ConversationTurn
        {
            Role = "assistant",
            Payload = new ConversationTurnPayload
            {
                Status = AiFlowGenerationResult.FailureTypeManualRetryRequired,
                ManualRetry = new AiManualRetryInfo { Required = true }
            }
        });

        var route = _router.Route(new AiTurnRouteRequest(
            "请基于上一轮需求继续修正工作流 JSON",
            null,
            GenerateFlowMode.Auto,
            session,
            HasExistingFlow: true,
            Attachments: null));

        route.TurnIntent.Should().Be(AiTurnIntents.ManualRetryRepair);
        route.ShouldBypassClarification.Should().BeTrue();
    }

    private static ConversationSession BuildPendingClarificationSession()
    {
        var session = new ConversationSession { SessionId = "s1" };
        session.History.Add(new ConversationTurn
        {
            Role = "assistant",
            Payload = new ConversationTurnPayload
            {
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationQuestions =
                    [
                        new AiClarificationQuestion
                        {
                            Field = "object_type",
                            Question = "请补充检测对象是什么。",
                            Required = true,
                            Options = ["包装箱/纸箱"]
                        }
                    ]
                }
            }
        });

        return session;
    }
}
