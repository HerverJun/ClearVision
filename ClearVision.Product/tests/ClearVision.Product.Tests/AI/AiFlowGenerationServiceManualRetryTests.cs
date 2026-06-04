using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.DryRun;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

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
    public async Task GenerateFlowAsync_InvalidJson_ShouldReturnManualRetryAfterAutoRepairRetries()
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
        result.RetryCount.Should().Be(2);
        result.LastAttemptDiagnostics.Should().HaveCount(3);
        result.LastAttemptDiagnostics.Should().OnlyContain(item => item.Stage == "parse");

        await connector.Received(3).StreamCompleteAsync(
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

    [Fact(DisplayName = "GenerateFlowAsync should automatically repair parse failures before manual retry")]
    public async Task GenerateFlowAsync_InvalidJsonThenValidJson_ShouldRepairAutomatically()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new AiCompletionResult
                {
                    Content = "not json"
                }),
                Task.FromResult(new AiCompletionResult
                {
                    Content = BuildSuccessfulFlowJson()
                }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "Generate a basic inspection flow.",
            SessionId: "parse-auto-repair"));

        result.Success.Should().BeTrue();
        result.RetryCount.Should().Be(1);
        result.ManualRetry.Should().BeNull();

        await connector.Received(2).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(1).Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should extract a complete JSON object with trailing text")]
    public async Task GenerateFlowAsync_ResponseWithTrailingText_ShouldExtractCompleteJsonObject()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson() + "\n\n说明：请现场复核 {阈值}"
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "生成一个基础检测流程",
            SessionId: "parse-trailing-text"));

        result.Success.Should().BeTrue();
        result.FailureType.Should().BeNull();

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(1).Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should unwrap a workflow envelope before validation")]
    public async Task GenerateFlowAsync_ResponseWrappedInWorkflowEnvelope_ShouldUnwrapBeforeValidation()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = "{\"parametersNeedingReview\":{\"op_1\":[\"FilePath\"]},\"workflow\":" + BuildSuccessfulFlowJson() + "}"
            }));

        AiGeneratedFlowJson? validatedFlow = null;
        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Do<AiGeneratedFlowJson>(flow => validatedFlow = flow))
            .Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "Generate a basic inspection flow.",
            SessionId: "parse-workflow-envelope"));

        result.Success.Should().BeTrue();
        validatedFlow.Should().NotBeNull();
        validatedFlow!.Operators.Should().HaveCount(2);
        validatedFlow.Connections.Should().BeEmpty();
        validatedFlow.ParametersNeedingReview["op_1"].Should().ContainSingle("FilePath");
    }

    [Fact(DisplayName = "GenerateFlowAsync should normalize common workflow field aliases before validation")]
    public async Task GenerateFlowAsync_ResponseWithCommonAliases_ShouldNormalizeBeforeValidation()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = """
                          {
                            "summary": "Alias shaped response.",
                            "nodes": [
                              {
                                "id": "op_1",
                                "type": "ImageAcquisition",
                                "name": "Camera",
                                "params": {
                                  "SourceType": "File",
                                  "FilePath": "data/input.png",
                                  "CameraId": "cam_1"
                                }
                              },
                              {
                                "id": "op_2",
                                "operator_id": "ResultOutput",
                                "label": "Output",
                                "settings": {
                                  "Format": "JSON",
                                  "SaveToFile": false
                                }
                              }
                            ],
                            "edges": [
                              {
                                "from": "op_1.Image",
                                "to": "op_2.Result"
                              }
                            ],
                            "parameters_to_review": [
                              {
                                "operatorId": "op_1",
                                "parameters": ["FilePath"]
                              },
                              "op_2.Format"
                            ]
                          }
                          """
            }));

        AiGeneratedFlowJson? validatedFlow = null;
        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Do<AiGeneratedFlowJson>(flow => validatedFlow = flow))
            .Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "Generate a basic inspection flow.",
            SessionId: "parse-common-aliases"));

        result.Success.Should().BeTrue();
        validatedFlow.Should().NotBeNull();
        validatedFlow!.Explanation.Should().Be("Alias shaped response.");
        validatedFlow.Operators.Should().HaveCount(2);
        validatedFlow.Operators[0].TempId.Should().Be("op_1");
        validatedFlow.Operators[0].OperatorType.Should().Be("ImageAcquisition");
        validatedFlow.Operators[1].Parameters["SaveToFile"].Should().Be("false");
        validatedFlow.Connections.Should().ContainSingle(connection =>
            connection.SourceTempId == "op_1" &&
            connection.SourcePortName == "Image" &&
            connection.TargetTempId == "op_2" &&
            connection.TargetPortName == "Result");
        validatedFlow.ParametersNeedingReview["op_1"].Should().ContainSingle("FilePath");
        validatedFlow.ParametersNeedingReview["op_2"].Should().ContainSingle("Format");
    }

    [Fact(DisplayName = "GenerateFlowAsync should collapse redundant BoxNms after model-embedded NMS")]
    public async Task GenerateFlowAsync_EndToEndNmsDeepLearning_ShouldCollapseRedundantBoxNms()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildEndToEndNmsFlowWithBoxNmsJson()
            }));

        var validator = new AiFlowValidator(new OperatorFactory());
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "用 ONNX 模型做目标检测，模型已内置 NMS",
            SessionId: "normalize-onnx-nms"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        var flow = result.Flow.Should().BeOfType<OperatorFlowDto>().Subject;
        flow.Operators.Select(op => op.Type.ToString()).Should().NotContain("BoxNms");

        var deepLearning = flow.Operators.Single(op => op.Type.ToString() == "DeepLearning");
        deepLearning.Parameters.Single(param => param.Name == "OutputFormat").Value?.ToString()
            .Should().Be("EndToEndNms");
        deepLearning.Parameters.Single(param => param.Name == "EnableInternalNms").Value?.ToString()
            .Should().Be("true");
    }

    [Fact(DisplayName = "GenerateFlowAsync should route truncated JSON to manual retry without converter exceptions")]
    public async Task GenerateFlowAsync_TruncatedJsonParameterValue_ShouldReturnManualRetry()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = """
                          {
                            "explanation": "broken",
                            "operators": [
                              {
                                "tempId": "op_1",
                                "operatorType": "Thresholding",
                                "parameters": {
                                  "Threshold": 0.
                          """
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "生成一个基础检测流程",
            SessionId: "parse-truncated-json-value"));

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        result.ManualRetry.Should().NotBeNull();
        result.ManualRetry!.Stage.Should().Be("parse");
        result.RetryCount.Should().Be(2);
        result.LastAttemptDiagnostics.Should().HaveCount(3);
        result.LastAttemptDiagnostics.Should().OnlyContain(item => item.Stage == "parse");
        validator.DidNotReceive().Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should prefer the later workflow JSON when old output is echoed")]
    public async Task GenerateFlowAsync_ResponseEchoesOldFlowBeforeCorrectedFlow_ShouldUseCorrectedFlow()
    {
        var oldFlowJson = BuildSuccessfulFlowJson().Replace("模板策略测试流程。", "旧的错误流程。", StringComparison.Ordinal);
        var correctedFlowJson = BuildSuccessfulFlowJson().Replace("模板策略测试流程。", "修复后的流程。", StringComparison.Ordinal);

        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = oldFlowJson + "\n\n以上是上一轮输出，下面是修复后的完整 JSON：\n\n" + correctedFlowJson
            }));

        AiGeneratedFlowJson? validatedFlow = null;
        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Do<AiGeneratedFlowJson>(flow => validatedFlow = flow))
            .Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "修复上一轮失败的流程 JSON",
            SessionId: "parse-prefers-later-flow"));

        result.Success.Should().BeTrue();
        validatedFlow.Should().NotBeNull();
        validatedFlow!.Explanation.Should().Be("修复后的流程。");
    }

    [Fact(DisplayName = "GenerateFlowAsync should repair manual retry without re-entering clarification")]
    public async Task GenerateFlowAsync_ManualRetryRepairRequest_ShouldBypassClarification()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new AiCompletionResult
                {
                    Content = "not json"
                }),
                Task.FromResult(new AiCompletionResult
                {
                    Content = "not json"
                }),
                Task.FromResult(new AiCompletionResult
                {
                    Content = "not json"
                }),
                Task.FromResult(new AiCompletionResult
                {
                    Content = BuildSuccessfulFlowJson()
                }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var extractionCount = 0;
        var requirementBriefExtractor = Substitute.For<IRequirementBriefExtractor>();
        requirementBriefExtractor.Extract(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ScenarioMatchResult?>())
            .Returns(_ =>
            {
                extractionCount++;
                return extractionCount == 1
                    ? new AiRequirementBrief
                    {
                        ScenarioKey = "hole-spacing-measurement",
                        ScenarioName = "圆孔间距测量",
                        IntentType = "measurement",
                        Confidence = 0.9,
                        CanGenerateDraftNow = true,
                        DraftRiskLevel = "low",
                        ObjectName = "圆孔/孔位",
                        MeasurementTargets = ["孔距/圆心距离"],
                        OutputTarget = "UI",
                        RoiRequirement = "region",
                        CalibrationRequirement = "pixel_to_world",
                        KnownFacts =
                        [
                            "测量目标：两个圆形孔位的圆心距离",
                            "输出目标：界面显示",
                            "ROI：固定ROI"
                        ]
                    }
                    : new AiRequirementBrief
                    {
                        Confidence = 0.2,
                        CanGenerateDraftNow = false,
                        DraftRiskLevel = "high",
                        RequiredFields = ["scene", "object_type"],
                        MissingFacts = ["需要确认具体场景", "需要确认检测对象"],
                        ClarificationQuestions = [BuildQuestion("scene"), BuildQuestion("object_type")],
                        ClarificationRequired = true
                    };
            });

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: requirementBriefExtractor,
            useRealOperatorFactory: true);

        var first = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "测量两个圆形孔位的圆心距离，输出到界面，固定 ROI，需要像素到物理单位换算",
            SessionId: "manual-retry-no-clarification"));

        first.Success.Should().BeFalse();
        first.ManualRetry.Should().NotBeNull();
        first.ManualRetry!.Draft.Should().Contain("上一轮已确认的需求上下文");
        first.ManualRetry.Draft.Should().Contain("上一轮模型原始输出");

        var repairRequest = "标定：像素到物理单位换算 输出目标：界面显示 ROI：固定ROI" +
                            Environment.NewLine +
                            Environment.NewLine +
                            first.ManualRetry.Draft;

        var second = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            repairRequest,
            SessionId: "manual-retry-no-clarification"));

        second.Success.Should().BeTrue();
        second.ClarificationRequired.Should().BeFalse();
        second.RequirementBrief.Should().NotBeNull();
        second.RequirementBrief!.ClarificationRequired.Should().BeFalse();
        second.RequirementBrief.KnownFacts.Should().Contain("本轮是上一轮格式/结构失败后的手动修复，不重新进入需求澄清。");

        await connector.Received(4).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_InvalidStructure_ShouldReturnManualRetryAfterAutoRepairRetries()
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
            "请生成一个基础检测流程",
            SessionId: "validation-manual-retry"));

        result.Success.Should().BeFalse();
        result.FailureType.Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        result.ManualRetry.Should().NotBeNull();
        result.ManualRetry!.Stage.Should().Be("validation");
        result.RetryCount.Should().Be(2);
        result.LastAttemptDiagnostics.Should().HaveCount(3);
        result.LastAttemptDiagnostics.Should().OnlyContain(item => item.Stage == "validation");
        result.LastAttemptDiagnostics.Last().Issues.Should().ContainSingle();
        result.LastAttemptDiagnostics.Last().Issues[0].Code.Should().Be("missing_parameter");
        result.ManualRetry.Diagnostics.Should().HaveCount(3);
        result.ManualRetry.RepairTarget.Should().Contain("ResultOutput");

        await connector.Received(3).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(3).Validate(Arg.Any<AiGeneratedFlowJson>());

        var session = conversationService.GetSession("validation-manual-retry");
        session.Should().NotBeNull();
        session!.History.Last().Payload.Should().NotBeNull();
        session.History.Last().Payload!.Failure.Should().NotBeNull();
        session.History.Last().Payload!.Failure!.Diagnostics.Should().HaveCount(3);
        session.History.Last().Payload!.ManualRetry.Should().NotBeNull();
        session.History.Last().Payload!.ManualRetry!.Stage.Should().Be("validation");
    }

    [Fact(DisplayName = "GenerateFlowAsync should automatically repair validation failures before manual retry")]
    public async Task GenerateFlowAsync_InvalidStructureThenValidStructure_ShouldRepairAutomatically()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new AiCompletionResult
                {
                    Content = BuildSuccessfulFlowJson()
                }),
                Task.FromResult(new AiCompletionResult
                {
                    Content = BuildSuccessfulFlowJson()
                }));

        var validation = new AiValidationResult();
        validation.AddError(
            "缺少 ResultOutput 的必填输入参数",
            code: "missing_parameter",
            category: "validation",
            relatedFields: ["operators[0].parameters.Result"],
            repairHint: "请补齐 ResultOutput 的输入参数。");

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>())
            .Returns(validation, new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(connector, validator, conversationService, useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "Generate a basic inspection flow.",
            SessionId: "validation-auto-repair"));

        result.Success.Should().BeTrue();
        result.RetryCount.Should().Be(1);
        result.ManualRetry.Should().BeNull();

        await connector.Received(2).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(2).Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Fact]
    public async Task GenerateFlowAsync_ChatGreeting_ShouldReturnReplyWithoutCallingModel()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var requirementBriefExtractor = Substitute.For<IRequirementBriefExtractor>();
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: requirementBriefExtractor);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "你好",
            SessionId: "chat-short-circuit"));

        result.Success.Should().BeTrue();
        result.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);
        result.InteractionState.Should().Be(AiInteractionStates.Idle);
        result.Flow.Should().BeNull();
        result.AiExplanation.Should().Contain("我在");
        var persistedPayload = conversationService.GetSession("chat-short-circuit")!.History.Last().Payload;
        persistedPayload.Should().NotBeNull();
        persistedPayload!.RouterConfidence.Should().Be(AiRouterConfidence.High);
        persistedPayload.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);

        requirementBriefExtractor.DidNotReceive().Extract(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<ScenarioMatchResult?>());
        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.DidNotReceive().Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Fact]
    public async Task GenerateFlowAsync_GreetingDuringClarification_ShouldNotClearPendingQuestions()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("clarification-chat-lifecycle");
        conversationService.RecordAssistantResponse(
            "clarification-chat-lifecycle",
            "please clarify object and defect",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("object_type"),
                        BuildQuestion("defect_type")
                    ]
                }
            });

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                IntentType = "defect_detection",
                Confidence = 0.2,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                RequiredFields = ["object_type", "defect_type"],
                MissingFacts = ["需要确认检测对象", "需要确认缺陷类别"],
                ClarificationQuestions =
                [
                    BuildQuestion("object_type"),
                    BuildQuestion("defect_type")
                ],
                ClarificationRequired = true
            });

        var greeting = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "在吗",
            SessionId: "clarification-chat-lifecycle"));

        greeting.Success.Should().BeTrue();
        greeting.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);

        var answer = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "检测对象：包装箱",
            SessionId: "clarification-chat-lifecycle"));

        answer.Success.Should().BeFalse();
        answer.TurnIntent.Should().Be(AiTurnIntents.ClarificationAnswer);
        answer.ClarificationRequired.Should().BeTrue();
        answer.RequirementBrief.Should().NotBeNull();
        answer.RequirementBrief!.ClarificationQuestions.Should().ContainSingle();
        answer.RequirementBrief.ClarificationQuestions[0].Field.Should().Be("defect_type");

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_VagueNewRequestAfterClarification_ShouldClarifyInsteadOfForcingDraft()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("vague-new-request-after-clarification");
        conversationService.RecordAssistantResponse(
            "vague-new-request-after-clarification",
            "please clarify scene and object",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("scene"),
                        BuildQuestion("object_type")
                    ]
                }
            });
        conversationService.RecordAssistantResponse(
            "vague-new-request-after-clarification",
            "please clarify again",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("scene"),
                        BuildQuestion("object_type")
                    ]
                }
            });

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                Confidence = 0,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                RequiredFields = ["scene", "object_type"],
                MissingFacts = ["需要确认具体场景", "需要确认检测对象"],
                ClarificationQuestions =
                [
                    BuildQuestion("scene"),
                    BuildQuestion("object_type")
                ],
                ClarificationRequired = true
            });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "帮我构建一个流程",
            SessionId: "vague-new-request-after-clarification"));

        result.Success.Should().BeFalse();
        result.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        result.ClarificationRequired.Should().BeTrue();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationQuestions.Should().NotBeEmpty();
        result.RequirementBrief.KnownFacts.Should().NotContain(fact =>
            fact.Contains("已避免重复澄清同一字段", StringComparison.Ordinal));

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_ClarificationAnswerWithExistingFlow_ShouldKeepOriginalNewMode()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("clarification-new-with-existing-flow");
        conversationService.RecordAssistantResponse(
            "clarification-new-with-existing-flow",
            "please clarify object",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                TurnIntent = AiTurnIntents.NewFlow,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions = [BuildQuestion("object_type")]
                }
            });

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                IntentType = "defect_detection",
                Confidence = 0.85,
                CanGenerateDraftNow = true,
                DraftRiskLevel = "medium",
                ObjectName = "包装箱",
                ObjectTypes = ["包装箱"],
                DefectTypes = ["破损"],
                OutputTarget = "UI"
            },
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "检测对象：包装箱",
            SessionId: "clarification-new-with-existing-flow",
            ExistingFlowJson: BuildSuccessfulFlowJson(),
            DebugPrompt: true));

        result.Success.Should().BeTrue();
        result.TurnIntent.Should().Be(AiTurnIntents.ClarificationAnswer);
        var promptTrace = result.PromptTrace.Should().BeOfType<AiPromptTrace>().Subject;
        promptTrace.Mode.Should().Be("new");
        promptTrace.UsedReferenceFlowSummary.Should().BeEmpty();

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task GenerateFlowAsync_GenericDefectDetection_ShouldNotAskWhetherItIsDefectDetection()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: new RequirementBriefExtractor());

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "缺陷检测",
            SessionId: "generic-defect-clarification"));

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.IntentType.Should().Be("defect_detection");
        result.RequirementBrief.ClarificationQuestions.Should().NotContain(question => question.Field == "scene");
        result.RequirementBrief.ClarificationQuestions.Select(question => question.Field)
            .Should().OnlyContain(field => field == "object_type" || field == "defect_type");
        result.RequirementBrief.NonBlockingMissingFields.Should().Contain("scene");

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_GenericWorkflowRequestAfterGreeting_ShouldClarifyWithoutCallingModel()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: new RequirementBriefExtractor());

        var greeting = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "hi",
            SessionId: "generic-workflow-after-greeting"));
        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "帮我构建一个流程",
            SessionId: "generic-workflow-after-greeting"));

        greeting.Success.Should().BeTrue();
        greeting.TurnIntent.Should().Be(AiTurnIntents.ChatOrHelp);
        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.BlockingClarificationFields.Should().Contain(["scene", "object_type"]);
        result.RequirementBrief.ClarificationQuestions.Select(question => question.Field)
            .Should().Contain(["scene", "object_type"]);

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AiRequirementModes.Strict)]
    [InlineData(AiRequirementModes.Draft)]
    public async Task GenerateFlowAsync_GenericWorkflowRequest_ShouldClarifyBeforeModel(string requirementMode)
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: new RequirementBriefExtractor());

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "帮我构建一个流程",
            SessionId: $"generic-workflow-direct-{requirementMode}")
        {
            RequirementMode = requirementMode
        });

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.InteractionState.Should().Be(AiInteractionStates.Clarifying);
        result.TurnIntent.Should().Be(AiTurnIntents.NewFlow);
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.BlockingClarificationFields.Should().Contain(["scene", "object_type"]);
        result.RequirementBrief.ClarificationQuestions.Select(question => question.Field)
            .Should().Contain(["scene", "object_type"]);

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_GenericWorkflowRequestAfterStaleClarification_ShouldResetContextAndClarify()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("generic-workflow-stale-clarification");
        conversationService.RecordAssistantResponse(
            "generic-workflow-stale-clarification",
            "please clarify scene and object",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                InteractionState = AiInteractionStates.Clarifying,
                TurnIntent = AiTurnIntents.NewFlow,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("scene"),
                        BuildQuestion("object_type")
                    ]
                }
            });
        conversationService.RecordAssistantResponse(
            "generic-workflow-stale-clarification",
            "please clarify scene and object again",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                InteractionState = AiInteractionStates.Clarifying,
                TurnIntent = AiTurnIntents.ClarificationAnswer,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("scene"),
                        BuildQuestion("object_type")
                    ]
                }
            });
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBriefExtractor: new RequirementBriefExtractor());

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "帮我构建一个流程",
            SessionId: "generic-workflow-stale-clarification")
        {
            RequirementMode = AiRequirementModes.Draft
        });

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.InteractionState.Should().Be(AiInteractionStates.Clarifying);
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationQuestions.Should().NotBeEmpty();
        result.RequirementBrief.KnownFacts.Should().NotContain(fact =>
            fact.Contains("已避免重复澄清同一字段", StringComparison.Ordinal));

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should stop clarification after two rounds")]
    public async Task GenerateFlowAsync_AfterTwoClarificationRounds_ShouldForceDraftGeneration()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("clarification-max-rounds");
        conversationService.RecordAssistantResponse(
            "clarification-max-rounds",
            "please clarify object",
            null,
            payload: BuildClarificationPayload("object_type"));
        conversationService.RecordAssistantResponse(
            "clarification-max-rounds",
            "please clarify defect",
            null,
            payload: BuildClarificationPayload("defect_type"));

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                Confidence = 0.2,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                MissingFacts = ["need object", "need defect"],
                ClarificationQuestions =
                [
                    BuildQuestion("object_type"),
                    BuildQuestion("defect_type")
                ],
                ClarificationRequired = true
            },
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "generate a rough inspection flow",
            SessionId: "clarification-max-rounds"));

        result.Success.Should().BeTrue();
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.RequirementMode.Should().Be(AiRequirementModes.Draft);
        result.RequirementBrief.DraftRiskLevel.Should().Be("high");

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateFlowAsync_ModifyExistingFlow_ShouldBypassBlockingClarification()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());
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
                MissingFacts = ["需要确认具体场景"],
                ClarificationQuestions =
                [
                    new AiClarificationQuestion
                    {
                        Field = "scene",
                        Question = "请确认这是外观缺陷、漏装有无、线序判定还是尺寸测量场景。",
                        Required = true,
                        Priority = "high"
                    }
                ],
                ClarificationRequired = true
            },
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "把算子名称改成中文",
            SessionId: "modify-bypass-clarification",
            ExistingFlowJson: BuildSuccessfulFlowJson()));

        result.Success.Should().BeTrue();
        result.TurnIntent.Should().Be(AiTurnIntents.ModifyFlow);
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.KnownFacts.Should().Contain(fact => fact.Contains("增量修改", StringComparison.Ordinal));

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should reset clarification rounds for a new self-contained request")]
    public async Task GenerateFlowAsync_NewSelfContainedRequest_WithOnlyCalibrationMissing_ShouldGenerateDraft()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("clarification-new-request");
        conversationService.RecordAssistantResponse(
            "clarification-new-request",
            "please clarify object",
            null,
            payload: BuildClarificationPayload("object_type"));
        conversationService.RecordAssistantResponse(
            "clarification-new-request",
            "please clarify defect",
            null,
            payload: BuildClarificationPayload("defect_type"));

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                ScenarioKey = "copper-hole-spacing-measurement",
                ScenarioName = "两器铜孔间距检测",
                IntentType = "measurement",
                Confidence = 0.9,
                CanGenerateDraftNow = true,
                DraftRiskLevel = "high",
                MissingFacts = ["需要确认标定或像素转物理单位换算"],
                ClarificationQuestions =
                [
                    new AiClarificationQuestion
                    {
                        Field = "calibration",
                        Question = "是否需要像素到物理单位换算或标定？",
                        Required = true,
                        Priority = "high",
                        Options = ["像素到物理单位换算", "手眼标定", "不需要"]
                    }
                ],
                ClarificationRequired = true
            },
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "测量两个圆形孔位的圆心距离。",
            SessionId: "clarification-new-request"));

        result.Success.Should().BeTrue();
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationQuestions.Should().NotContain(question => question.Field == "calibration");
        result.RequirementBrief.NonBlockingMissingFields.Should().Contain("calibration");

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should not repeat answered clarification fields")]
    public async Task GenerateFlowAsync_WithAnsweredClarificationField_ShouldOnlyAskRemainingFields()
    {
        var connector = Substitute.For<IAiConnector>();
        var validator = Substitute.For<IAiFlowValidator>();
        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("clarification-no-repeat");
        conversationService.RecordAssistantResponse(
            "clarification-no-repeat",
            "please clarify object and defect",
            null,
            payload: new ConversationTurnPayload
            {
                Kind = "assistant_clarification",
                Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                ClarificationRequired = true,
                RequirementBrief = new AiRequirementBrief
                {
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        BuildQuestion("object_type"),
                        BuildQuestion("defect_type")
                    ]
                }
            });

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                Confidence = 0.2,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                RequiredFields = ["object_type", "defect_type"],
                MissingFacts = ["需要确认检测对象", "需要确认缺陷类别"],
                ClarificationQuestions =
                [
                    BuildQuestion("object_type"),
                    BuildQuestion("defect_type")
                ],
                ClarificationRequired = true
            });

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "缺陷类别：破损",
            SessionId: "clarification-no-repeat"));

        result.Success.Should().BeFalse();
        result.ClarificationRequired.Should().BeTrue();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationQuestions.Should().ContainSingle();
        result.RequirementBrief.ClarificationQuestions[0].Field.Should().Be("object_type");
        result.RequirementBrief.MissingFacts.Should().Contain("需要确认检测对象");
        result.RequirementBrief.MissingFacts.Should().NotContain("需要确认缺陷类别");

        await connector.DidNotReceive().StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact(DisplayName = "GenerateFlowAsync should treat current calibration answer as resolved")]
    public async Task GenerateFlowAsync_WithCurrentCalibrationNoneAnswer_ShouldNotAskCalibrationAgain()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var conversationService = new ConversationalFlowService(_tempRoot);
        _ = conversationService.GetOrCreateSession("calibration-no-repeat");
        conversationService.RecordAssistantResponse(
            "calibration-no-repeat",
            "please clarify calibration",
            null,
            payload: BuildClarificationPayload("calibration"));

        var service = CreateService(
            connector,
            validator,
            conversationService,
            new AiRequirementBrief
            {
                ScenarioKey = "copper-hole-spacing-measurement",
                ScenarioName = "两器铜孔间距检测",
                IntentType = "measurement",
                Confidence = 0.9,
                CanGenerateDraftNow = false,
                DraftRiskLevel = "high",
                RequiredFields = ["calibration"],
                MissingFacts = ["需要确认标定或像素转物理单位换算"],
                ClarificationQuestions = [BuildQuestion("calibration")],
                ClarificationRequired = true
            },
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "标定：不需要 输出目标：PLC ROI：固定ROI",
            SessionId: "calibration-no-repeat"));

        result.Success.Should().BeTrue();
        result.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.CalibrationRequirement.Should().Be("none");
        result.RequirementBrief.ClarificationQuestions.Should().NotContain(question => question.Field == "calibration");
        result.RequirementBrief.MissingFacts.Should().NotContain(fact => fact.Contains("标定", StringComparison.OrdinalIgnoreCase));

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    [Theory(DisplayName = "GenerateFlowAsync should expose template generation mode and lock level")]
    [InlineData(0.9, "包装箱外观检测", "template_fill", "strict", "检测包装箱破损、压痕、标签异常")]
    [InlineData(0.5, "包装箱外观检测", "template_adapt", "relaxed", "检测包装箱破损、压痕、标签异常")]
    [InlineData(0.9, "", "free_generate", "none", "检测包装箱破损、压痕、标签异常，不要用模板")]
    public async Task GenerateFlowAsync_TemplateMatchConfidence_ShouldExposeGenerationModeAndLockLevel(
        double confidence,
        string expectedRecommendedTemplate,
        string expectedGenerationMode,
        string expectedTemplateLockLevel,
        string description)
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var scenarioMatch = BuildPackagingScenarioMatch(confidence);
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            scenarioMatches: [scenarioMatch],
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            description,
            SessionId: $"template-mode-{expectedGenerationMode}"));

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be(expectedGenerationMode);
        result.TemplateLockLevel.Should().Be(expectedTemplateLockLevel);
        result.TemplateCandidates.Should().ContainSingle();
        result.TemplateCandidates[0].Confidence.Should().Be(confidence);

        if (string.IsNullOrWhiteSpace(expectedRecommendedTemplate))
        {
            result.RecommendedTemplate.Should().BeNull();
        }
        else
        {
            result.RecommendedTemplate.Should().NotBeNull();
            result.RecommendedTemplate!.TemplateName.Should().Be(expectedRecommendedTemplate);
            result.RecommendedTemplate.ScenarioKey.Should().Be("carton-appearance-inspection");
        }

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
        validator.Received(1).Validate(Arg.Any<AiGeneratedFlowJson>());
    }

    [Theory(DisplayName = "GenerateFlowAsync should honor explicit template selection")]
    [InlineData("template_fill", "template_fill", "strict", true)]
    [InlineData("template_adapt", "template_adapt", "relaxed", true)]
    [InlineData("free_generate", "free_generate", "none", false)]
    public async Task GenerateFlowAsync_TemplateSelection_ShouldOverrideAutomaticConfidence(
        string selectedMode,
        string expectedGenerationMode,
        string expectedTemplateLockLevel,
        bool expectRecommendedTemplate)
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var scenarioMatch = BuildPackagingScenarioMatch(0.9);
        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            scenarioMatches: [scenarioMatch],
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "carton appearance inspection",
            SessionId: $"template-selection-{selectedMode}",
            TemplateSelection: new AiTemplateSelectionInfo
            {
                Mode = selectedMode,
                ScenarioKey = "carton-appearance-inspection"
            }));

        result.Success.Should().BeTrue();
        result.GenerationMode.Should().Be(expectedGenerationMode);
        result.TemplateLockLevel.Should().Be(expectedTemplateLockLevel);
        result.TemplateCandidates.Should().ContainSingle();
        result.TemplateCandidates[0].ScenarioKey.Should().Be("carton-appearance-inspection");

        if (expectRecommendedTemplate)
        {
            result.RecommendedTemplate.Should().NotBeNull();
            result.RecommendedTemplate!.MatchMode.Should().Be("user-selected-template");
            result.RecommendedTemplate.ScenarioKey.Should().Be("carton-appearance-inspection");
        }
        else
        {
            result.RecommendedTemplate.Should().BeNull();
        }
    }

    [Fact(DisplayName = "GenerateFlowAsync should not loop clarification when high-confidence template can draft")]
    public async Task GenerateFlowAsync_TemplateFillWithMissingFacts_ShouldGenerateDraftInsteadOfClarifying()
    {
        var connector = Substitute.For<IAiConnector>();
        connector.StreamCompleteAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChatMessage>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AiCompletionResult
            {
                Content = BuildSuccessfulFlowJson()
            }));

        var validator = Substitute.For<IAiFlowValidator>();
        validator.Validate(Arg.Any<AiGeneratedFlowJson>()).Returns(new AiValidationResult());

        var requirementBrief = new AiRequirementBrief
        {
            ScenarioKey = "carton-appearance-inspection",
            ScenarioName = "包装箱外观检测",
            IntentType = "defect_detection",
            Confidence = 0.9,
            CanGenerateDraftNow = true,
            DraftRiskLevel = "high",
            ObjectTypes = ["carton"],
            RequiredFields = ["defect_type"],
            MissingFacts = ["需要确认缺陷类别"],
            ClarificationQuestions =
            [
                new AiClarificationQuestion
                {
                    Field = "defect_type",
                    Question = "请补充需要判定的缺陷类别。",
                    Required = true,
                    Priority = "high"
                }
            ],
            ClarificationRequired = true
        };

        var conversationService = new ConversationalFlowService(_tempRoot);
        var service = CreateService(
            connector,
            validator,
            conversationService,
            requirementBrief,
            scenarioMatches: [BuildPackagingScenarioMatch(0.9)],
            useRealOperatorFactory: true);

        var result = await service.GenerateFlowAsync(new AiFlowGenerationRequest(
            "检测包装箱外观缺陷",
            SessionId: "template-clarification-loop"));

        result.Success.Should().BeTrue();
        result.ClarificationRequired.Should().BeFalse();
        result.CompletionStatus.Should().Be(AiFlowGenerationResult.CompletionStatusCompleted);
        result.RequirementBrief.Should().NotBeNull();
        result.RequirementBrief!.ClarificationRequired.Should().BeFalse();
        result.RequirementBrief.RequirementMode.Should().Be(AiRequirementModes.Draft);
        result.RequirementBrief.MissingFacts.Should().NotContain("需要确认缺陷类别");
        result.RequirementBrief.NonBlockingMissingFields.Should().Contain("defect_type");

        await connector.Received(1).StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>());
    }

    private static AiFlowGenerationService CreateService(
        IAiConnector connector,
        IAiFlowValidator validator,
        IConversationalFlowService conversationService,
        AiRequirementBrief? requirementBrief = null,
        IReadOnlyList<ScenarioMatchResult>? scenarioMatches = null,
        IRequirementBriefExtractor? requirementBriefExtractor = null,
        bool useRealOperatorFactory = false)
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

        var operatorFactory = useRealOperatorFactory
            ? new OperatorFactory()
            : Substitute.For<IOperatorFactory>();
        if (!useRealOperatorFactory)
            operatorFactory.GetAllMetadata().Returns(Array.Empty<OperatorMetadata>());

        var templateService = Substitute.For<IFlowTemplateService>();
        var scenarioMatcher = Substitute.For<IScenarioMatcher>();
        scenarioMatcher.MatchAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<string>?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(scenarioMatches ?? Array.Empty<ScenarioMatchResult>()));

        if (requirementBriefExtractor == null)
        {
            requirementBriefExtractor = Substitute.For<IRequirementBriefExtractor>();
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
        }

        var templateConstraintValidator = Substitute.For<ITemplateConstraintValidator>();
        templateConstraintValidator.Validate(
                Arg.Any<AiGeneratedFlowJson>(),
                Arg.Any<FlowTemplate?>(),
                Arg.Any<bool>())
            .Returns(new AiValidationResult());

        var flowExecutionService = Substitute.For<IFlowExecutionService>();
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns("Production");

        var promptVersionManager = Substitute.For<ClearVision.Product.Infrastructure.AI.IPromptVersionManager>();
        promptVersionManager.GetActiveVersionAsync().Returns(Task.FromResult(new PromptVersion
        {
            Id = Guid.NewGuid(),
            Name = "Test Version",
            Description = "Test",
            Content = "test prompt"
        }));

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
            new AiTurnRouter(),
            templateConstraintValidator,
            new AiFlowResponseParser(),
            new DryRunService(flowExecutionService),
            hostEnvironment,
            promptVersionManager,
            Substitute.For<Microsoft.Extensions.Logging.ILogger<AiFlowGenerationService>>());
    }

    private static ScenarioMatchResult BuildPackagingScenarioMatch(double confidence)
    {
        var template = new FlowTemplate
        {
            Id = Guid.NewGuid(),
            Name = "包装箱外观检测",
            Industry = "包装终检",
            TemplateVersion = "1.0.0",
            ScenarioKey = "carton-appearance-inspection",
            FlowJson = BuildSuccessfulFlowJson()
        };

        return new ScenarioMatchResult
        {
            Template = template,
            Confidence = confidence,
            MatchReason = "Matched 包装箱, 破损",
            MatchedFields = ["keywords", "defectTypes"],
            Scenario = new ScenarioDefinition
            {
                ScenarioKey = "carton-appearance-inspection",
                ScenarioName = "包装箱外观检测",
                TemplateName = "包装箱外观检测",
                TemplateVersion = "1.0.0",
                Industry = "包装终检",
                Keywords = ["包装箱"],
                DefectTypes = ["破损"]
            }
        };
    }

    private static ConversationTurnPayload BuildClarificationPayload(string field)
    {
        return new ConversationTurnPayload
        {
            Kind = "assistant_clarification",
            Status = AiFlowGenerationResult.CompletionStatusClarificationRequired,
            ClarificationRequired = true,
            RequirementBrief = new AiRequirementBrief
            {
                ClarificationRequired = true,
                ClarificationQuestions = [BuildQuestion(field)]
            }
        };
    }

    private static AiClarificationQuestion BuildQuestion(string field)
    {
        return new AiClarificationQuestion
        {
            Field = field,
            Question = $"Please clarify {field}",
            Required = true,
            Priority = "high",
            Options = field switch
            {
                "calibration" => ["像素到物理单位换算", "手眼标定", "不需要"],
                "defect_type" => ["破损", "压痕"],
                _ => ["包装箱", "产品"]
            }
        };
    }

    private static string BuildSuccessfulFlowJson()
    {
        return """
               {
                 "explanation": "模板策略测试流程。",
                 "operators": [
                   {
                     "tempId": "op_1",
                     "operatorType": "ImageAcquisition",
                     "displayName": "图像采集",
                     "parameters": {
                       "SourceType": "File",
                       "FilePath": "data/input.png",
                       "CameraId": "cam_1"
                     }
                   },
                   {
                     "tempId": "op_2",
                     "operatorType": "ResultOutput",
                     "displayName": "结果输出",
                     "parameters": {
                       "Format": "JSON",
                       "SaveToFile": "false"
                     }
                   }
                 ],
                 "connections": [],
                 "parametersNeedingReview": {}
               }
               """;
    }

    private static string BuildEndToEndNmsFlowWithBoxNmsJson()
    {
        return """
               {
                 "explanation": "ONNX 模型已内置 NMS，平台侧不需要再做候选框抑制。",
                 "operators": [
                   {
                     "tempId": "op_1",
                     "operatorType": "ImageAcquisition",
                     "displayName": "图像采集",
                     "parameters": {
                       "SourceType": "File",
                       "FilePath": "data/input.png"
                     }
                   },
                   {
                     "tempId": "op_2",
                     "operatorType": "DeepLearning",
                     "displayName": "目标检测",
                     "parameters": {
                       "ModelPath": "models/wire-seq-yolo-nms.onnx",
                       "LabelsPath": "",
                       "Confidence": "0.05",
                       "InputSize": "640",
                       "TargetClasses": "Wire_Black,Wire_Blue",
                       "OutputFormat": "EndToEndNms",
                       "EnableInternalNms": "false",
                       "DetectionMode": "Object"
                     }
                   },
                   {
                     "tempId": "op_3",
                     "operatorType": "BoxNms",
                     "displayName": "候选框抑制",
                     "parameters": {
                       "IouThreshold": "0.45",
                       "ScoreThreshold": "0.25",
                       "MaxDetections": "20",
                       "ShowSuppressed": "false"
                     }
                   },
                   {
                     "tempId": "op_4",
                     "operatorType": "ResultJudgment",
                     "displayName": "数量判定",
                     "parameters": {
                       "Condition": "GreaterOrEqual",
                       "ExpectValue": "1",
                       "MinConfidence": "0.0"
                     }
                   },
                   {
                     "tempId": "op_5",
                     "operatorType": "ResultOutput",
                     "displayName": "结果输出",
                     "parameters": {
                       "Format": "JSON",
                       "SaveToFile": "false"
                     }
                   }
                 ],
                 "connections": [
                   {
                     "sourceTempId": "op_1",
                     "sourcePortName": "Image",
                     "targetTempId": "op_2",
                     "targetPortName": "Image"
                   },
                   {
                     "sourceTempId": "op_2",
                     "sourcePortName": "Objects",
                     "targetTempId": "op_3",
                     "targetPortName": "Detections"
                   },
                   {
                     "sourceTempId": "op_3",
                     "sourcePortName": "Count",
                     "targetTempId": "op_4",
                     "targetPortName": "Value"
                   },
                   {
                     "sourceTempId": "op_3",
                     "sourcePortName": "Image",
                     "targetTempId": "op_5",
                     "targetPortName": "Image"
                   },
                   {
                     "sourceTempId": "op_3",
                     "sourcePortName": "Diagnostics",
                     "targetTempId": "op_5",
                     "targetPortName": "Data"
                   },
                   {
                     "sourceTempId": "op_4",
                     "sourcePortName": "JudgmentResult",
                     "targetTempId": "op_5",
                     "targetPortName": "Result"
                   },
                   {
                     "sourceTempId": "op_4",
                     "sourcePortName": "Details",
                     "targetTempId": "op_5",
                     "targetPortName": "Text"
                   }
                 ],
                 "parametersNeedingReview": {
                   "op_2": ["ModelPath"]
                 }
               }
               """;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
