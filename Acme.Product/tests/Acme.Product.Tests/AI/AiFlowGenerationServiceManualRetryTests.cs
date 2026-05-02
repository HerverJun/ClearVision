using Acme.Product.Contracts.Messages;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.AI;
using Acme.Product.Infrastructure.AI.DryRun;
using Acme.Product.Infrastructure.AI.Runtime;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Acme.Product.Tests.AI;

public class AiFlowGenerationServiceManualRetryTests : IDisposable
{
    private readonly string _tempRoot;

    public AiFlowGenerationServiceManualRetryTests()
    {
        _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "clearvision-ai-manual-retry-test-" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task GenerateFlowAsync_InvalidJson_ShouldReturnManualRetryWithoutRetryingModel()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = "this is not json"
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "请生成一个基础检测流程",
            SessionId: "parse-manual-retry"));

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        result.ManualRetry.Should().NotBeNull();
        result.ManualRetry!.Required.Should().BeTrue();
        result.ManualRetry.Stage.Should().Be("parse");
        result.ManualRetry.Draft.Should().Contain("请只返回一个完整且可解析的 JSON 对象");
        result.LastAttemptDiagnostics.Should().ContainSingle();
        result.LastAttemptDiagnostics[0].Stage.Should().Be("parse");

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.DidNotReceive().Validate(Arg.Any<AiGeneratedFlowJson>());

        var session = conversationService.GetSession("parse-manual-retry");
        session.Should().NotBeNull();
        session!.History.Last().Payload.Should().NotBeNull();
        session.History.Last().Payload!.ManualRetry.Should().NotBeNull();
        session.History.Last().Payload!.ManualRetry!.Stage.Should().Be("parse");
    }

    [Fact]
    public async Task GenerateFlowAsync_InvalidStructure_ShouldReturnManualRetryWithoutRetryingModel()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = "{}"
            }));

        var validation = new AiValidationResult();
        validation.AddError(
            "缺少 ResultOutput 的必填输入参数",
            code: "missing_parameter",
            category: "validation",
            relatedFields: ["operators[0].parameters.Result"],
            repairHint: "请补齐 ResultOutput 的输入参数。");

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(validation);

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "请修正当前流程参数",
            SessionId: "validation-manual-retry"));

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        result.ManualRetry.Should().NotBeNull();
        result.ManualRetry!.Stage.Should().Be("validation");
        result.LastAttemptDiagnostics.Should().ContainSingle();
        result.LastAttemptDiagnostics[0].Stage.Should().Be("validation");
        result.LastAttemptDiagnostics[0].Issues.Should().ContainSingle();
        result.LastAttemptDiagnostics[0].Issues[0].Code.Should().Be("missing_parameter");
        result.ManualRetry.Diagnostics.Should().ContainSingle();
        result.ManualRetry.RepairTarget.Should().Contain("ResultOutput");

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(1).Validate(Arg.Any<AiGeneratedFlowJson>());

        var session = conversationService.GetSession("validation-manual-retry");
        session.Should().NotBeNull();
        session!.History.Last().Payload.Should().NotBeNull();
        session.History.Last().Payload!.Failure.Should().NotBeNull();
        session.History.Last().Payload!.Failure!.Diagnostics.Should().ContainSingle();
        session.History.Last().Payload!.ManualRetry.Should().NotBeNull();
        session.History.Last().Payload!.ManualRetry!.Stage.Should().Be("validation");
    }

    [Fact]
    public async Task GenerateFlowAsync_ClarificationRequired_ShouldReturnClarificationWithoutCallingModel()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                Confidence = 0.2,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                MissingFacts = ["需要确认对象", "需要确认输出目标"],
                ClarificationQuestions =
                [
                    new AiClarificationQuestion
                    {
                        Field = "object_type",
                        Question = "请确认检测对象是什么？",
                        Required = true,
                        Priority = "high",
                        Reason = "对象未明确时无法安全生成流程。"
                    }
                ]
            });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "请帮我生成一个视觉检测流程",
            SessionId: "clarification-short-circuit"));

        result.Success.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeClarificationRequired);
        result.ClarificationRequired.Should().BeTrue();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.MissingFacts.Should().Contain("需要确认对象");
        result.RequirementBrief!.ClarificationQuestions.Should().ContainSingle();

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());

        validator.DidNotReceive().Validate(Arg.Any<AiGeneratedFlowJson>());

        var session = conversationService.GetSession("clarification-short-circuit");
        session.Should().NotBeNull();
        session!.History.Last().Payload.Should().NotBeNull();
        session.History.Last().Payload!.ClarificationRequired.Should().BeTrue();
        session.History.Last().Payload!.RequirementBrief.Should().NotBeNull();
        session.History.Last().Payload!.RequirementBrief!.MissingFacts.Should().Contain("需要确认对象");
    }

    private static AiFlowGenerationService CreateService(
        IAiConnector connector,
        IAiFlowValidator validator,
        IConversationalFlowService conversationService,
        AiRequirementBrief? requirementBrief = null)
    {
        var modelSelector = Substitute.For<IAiModelSelector>();
        modelSelector.SelectGenerationModel().Returns(new AiModelConfig
        {
            Name = "Test Model",
            Provider = "OpenAI Compatible",
            Model = "test-model",
            TimeoutMs = 30_000
        });

        var connectorFactory = Substitute.For<IAiConnectorFactory>();
        connectorFactory.CreateConnector(Arg.Any<AiModelConfig>()).Returns(connector);

        var operatorFactory = Substitute.For<IOperatorFactory>();
        operatorFactory.GetAllMetadata().Returns(Array.Empty<OperatorMetadata>());

        var templateService = Substitute.For<IFlowTemplateService>();
        var scenarioMatcher = Substitute.For<IScenarioMatcher>();
        scenarioMatcher.MatchAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScenarioMatchResult>>(Array.Empty<ScenarioMatchResult>()));

        var requirementBriefExtractor = Substitute.For<IRequirementBriefExtractor>();
        requirementBriefExtractor.Extract(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ScenarioMatchResult?>())
            .Returns(requirementBrief ?? new AiRequirementBrief
            {
                Confidence = 0.9,
                CanGenerateDraftNow = true,
                DraftRiskLevel = "low"
            });

        var templateConstraintValidator = Substitute.For<ITemplateConstraintValidator>();
        templateConstraintValidator.Validate(
                Arg.Any<AiGeneratedFlowJson>(),
                Arg.Any<FlowTemplate?>(),
                Arg.Any<bool>())
            .Returns(new AiValidationResult());

        var flowExecutionService = Substitute.For<IFlowExecutionService>();
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns("Production");

        return new AiFlowGenerationService(
            new AiGenerationOrchestrator(modelSelector, connectorFactory),
            new PromptBuilder(operatorFactory),
            conversationService,
            validator,
            new AutoLayoutService(),
            operatorFactory,
            templateService,
            scenarioMatcher,
            requirementBriefExtractor,
            templateConstraintValidator,
            new DryRunService(flowExecutionService),
            hostEnvironment,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
