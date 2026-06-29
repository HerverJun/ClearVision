using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.AgentRun;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.AI;

public class GenerateFlowMessageHandlerTests
{
    [Fact(DisplayName = "GenerateFlowMessageHandler should pass attachments to generation request")]
    public async Task HandleAsync_ShouldForwardAttachments()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);
        var attachments = new List<string>
        {
            @"C:\temp\template.png",
            @"C:\temp\target.png"
        };

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() },
                TurnIntent = AiTurnIntents.ModifyFlow,
                InteractionState = AiInteractionStates.Completed,
                RouterConfidence = AiRouterConfidence.High,
                BlockingClarificationFields = ["object_type"],
                NonBlockingMissingFields = ["model_path"]
            }));

        // Act
        var resultJson = await handler.HandleAsync(
            description: "tune template matching parameters",
            sessionId: "session-1",
            existingFlowJson: """{"operators":[]}""",
            hint: "template matching",
            attachments: attachments);

        // Assert
        await generationService.Received(1).GenerateFlowAsync(
            Arg.Is<AiFlowGenerationRequest>(request =>
                request.Attachments != null &&
                request.Attachments.SequenceEqual(attachments) &&
                request.Description == "tune template matching parameters"),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());

        using var doc = JsonDocument.Parse(resultJson);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("turnIntent").GetString().Should().Be(AiTurnIntents.ModifyFlow);
        doc.RootElement.GetProperty("interactionState").GetString().Should().Be(AiInteractionStates.Completed);
        doc.RootElement.GetProperty("routerConfidence").GetString().Should().Be(AiRouterConfidence.High);
        doc.RootElement.GetProperty("blockingClarificationFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("object_type");
        doc.RootElement.GetProperty("nonBlockingMissingFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("model_path");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should forward mode and debugPrompt")]
    public async Task HandleAsync_ShouldForwardModeAndDebugPrompt()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);
        var templateSelection = new AiTemplateSelectionInfo
        {
            Mode = "template_adapt",
            ScenarioKey = "carton-appearance-inspection"
        };

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() }
            }));

        await handler.HandleAsync(
            description: "review pending parameters",
            mode: GenerateFlowMode.ReviewPendingParameters,
            debugPrompt: true,
            requirementMode: "draft",
            templateSelection: templateSelection);

        await generationService.Received(1).GenerateFlowAsync(
            Arg.Is<AiFlowGenerationRequest>(request =>
                request.Mode == GenerateFlowMode.ReviewPendingParameters &&
                request.DebugPrompt &&
                request.RequirementMode == "draft" &&
                request.TemplateSelection != null &&
                request.TemplateSelection.Mode == "template_adapt" &&
                request.TemplateSelection.ScenarioKey == "carton-appearance-inspection"),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should forward attachment report message")]
    public async Task HandleAsync_ShouldForwardAttachmentReport()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);
        var receivedMessages = new List<(string Type, string Payload)>();

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(callInfo =>
            {
                var reportCallback = callInfo.ArgAt<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>(4);
                reportCallback(new ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport
                {
                    Sent =
                    [
                        new ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentSentItem
                        {
                            Path = @"C:\temp\template.png",
                            Name = "template.png"
                        }
                    ],
                    Skipped =
                    [
                        new ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentSkippedItem
                        {
                            Path = @"C:\temp\bad.txt",
                            Name = "bad.txt",
                            Reason = "unsupported_format"
                        }
                    ]
                });

                return Task.FromResult(new AiFlowGenerationResult
                {
                    Success = true,
                    Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() }
                });
            });

        // Act
        _ = await handler.HandleAsync(
            description: "demo",
            attachments: [@"C:\temp\template.png", @"C:\temp\bad.txt"],
            requestId: "req-attachment-1",
            onMessage: (type, payload) => receivedMessages.Add((type, payload)));

        // Assert
        receivedMessages.Should().Contain(message => message.Type == "GenerateFlowAttachmentReport");
        var payloadJson = receivedMessages.First(message => message.Type == "GenerateFlowAttachmentReport").Payload;
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        payloadDoc.RootElement.GetProperty("requestId").GetString().Should().Be("req-attachment-1");
        payloadDoc.RootElement.GetProperty("sent").GetArrayLength().Should().Be(1);
        payloadDoc.RootElement.GetProperty("skipped").GetArrayLength().Should().Be(1);
        payloadDoc.RootElement.GetProperty("skipped")[0].GetProperty("reason").GetString()
            .Should().Be("unsupported_format");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should include requestId in progress and stream callbacks")]
    public async Task HandleAsync_ShouldIncludeRequestIdInRealtimeMessages()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);
        var receivedMessages = new List<(string Type, string Payload)>();

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(callInfo =>
            {
                var progressCallback = callInfo.ArgAt<Action<string>>(1);
                var chunkCallback = callInfo.ArgAt<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(2);

                progressCallback("正在分析需求...");
                chunkCallback(new ClearVision.Product.Contracts.Messages.AiStreamChunk("thinking", "step-1"));

                return Task.FromResult(new AiFlowGenerationResult
                {
                    Success = true,
                    Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() }
                });
            });

        // Act
        _ = await handler.HandleAsync(
            description: "demo",
            requestId: "req-stream-1",
            onMessage: (type, payload) => receivedMessages.Add((type, payload)));

        // Assert
        receivedMessages.Should().Contain(message => message.Type == "GenerateFlowProgress");
        receivedMessages.Should().Contain(message => message.Type == "GenerateFlowStreamChunk");

        var progressPayload = receivedMessages.Last(message => message.Type == "GenerateFlowProgress").Payload;
        using var progressDoc = JsonDocument.Parse(progressPayload);
        progressDoc.RootElement.GetProperty("requestId").GetString().Should().Be("req-stream-1");
        progressDoc.RootElement.GetProperty("phase").GetString().Should().Be("prompt_context");

        var streamPayload = receivedMessages.Single(message => message.Type == "GenerateFlowStreamChunk").Payload;
        using var streamDoc = JsonDocument.Parse(streamPayload);
        streamDoc.RootElement.GetProperty("requestId").GetString().Should().Be("req-stream-1");
        streamDoc.RootElement.GetProperty("chunkType").GetString().Should().Be("thinking");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should include performance budget summary")]
    public async Task HandleAsync_ShouldIncludePerformanceBudgetSummary()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() },
                RetryCount = 1,
                PromptTrace = new AiPromptTrace
                {
                    EstimatedInputTokens = 19_000,
                    EstimatedOutputTokens = 7_000
                },
                StageTimeline =
                [
                    new AiGenerationStageDiagnostic { Stage = "prompt_context", DurationMs = 120 },
                    new AiGenerationStageDiagnostic { Stage = "llm", DurationMs = 46_000 }
                ]
            }));

        var resultJson = await handler.HandleAsync("demo");

        using var doc = JsonDocument.Parse(resultJson);
        var budget = doc.RootElement.GetProperty("performanceBudget");
        budget.GetProperty("budgetStatus").GetString().Should().Be("warning");
        budget.GetProperty("totalDurationMs").GetInt64().Should().Be(46_120);
        budget.GetProperty("retryCount").GetInt32().Should().Be(1);
        budget.GetProperty("estimatedInputTokens").GetInt32().Should().Be(19_000);
        budget.GetProperty("estimatedOutputTokens").GetInt32().Should().Be(7_000);
        budget.GetProperty("slowestStage").GetString().Should().Be("llm");
        budget.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("auto_retry_used");
        budget.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("token_estimate_over_24k");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize OperatorType as string")]
    public async Task HandleAsync_ShouldSerializeOperatorTypeAsString()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new OperatorFlowDto
                {
                    Operators = new List<OperatorDto>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            Name = "华睿相机采集",
                            Type = OperatorType.ImageAcquisition
                        }
                    }
                }
            }));

        // Act
        var resultJson = await handler.HandleAsync("用华睿相机做缺陷检测");

        // Assert
        using var doc = JsonDocument.Parse(resultJson);
        var firstOp = doc.RootElement
            .GetProperty("flow")
            .GetProperty("operators")[0];

        firstOp.GetProperty("type").ValueKind.Should().Be(JsonValueKind.String);
        firstOp.GetProperty("type").GetString().Should().Be("ImageAcquisition");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should include template-first structured fields")]
    public async Task HandleAsync_ShouldSerializeTemplateFirstStructuredPayload()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() },
                GenerationMode = "template_fill",
                TemplateLockLevel = "strict",
                RecommendedTemplate = new AiRecommendedTemplateInfo
                {
                    TemplateId = Guid.NewGuid().ToString(),
                    TemplateName = "端子线序检测",
                    TemplateVersion = "1.0.0",
                    ScenarioKey = "wire-sequence-terminal",
                    Industry = "线束装配",
                    MatchReason = "命中关键词：线序、端子",
                    MatchMode = "template-first",
                    Confidence = 0.91,
                    MatchedFields = ["keywords", "objectTypes"]
                },
                TemplateCandidates =
                [
                    new AiTemplateCandidateInfo
                    {
                        TemplateName = "端子线序检测",
                        TemplateVersion = "1.0.0",
                        ScenarioKey = "wire-sequence-terminal",
                        Industry = "线束装配",
                        Confidence = 0.91,
                        MatchReason = "命中关键词：线序、端子",
                        MatchedFields = ["keywords", "objectTypes"]
                    },
                    new AiTemplateCandidateInfo
                    {
                        TemplateName = "包装箱外观检测",
                        TemplateVersion = "1.0.0",
                        ScenarioKey = "carton-appearance-inspection",
                        Industry = "包装终检",
                        Confidence = 0.18,
                        MatchReason = "弱匹配"
                    }
                ],
                PendingParameters =
                [
                    new AiPendingParameterInfo
                    {
                        OperatorId = "op_3",
                        ParameterNames = ["ModelPath", "Confidence"]
                    }
                ],
                MissingResources =
                [
                    new AiMissingResourceInfo
                    {
                        ResourceType = "Model",
                        ResourceKey = "DeepLearning.ModelPath",
                        Description = "缺少模型文件路径"
                    }
                ]
            }));

        // Act
        var resultJson = await handler.HandleAsync("线序检测");

        // Assert
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        root.GetProperty("recommendedTemplate").GetProperty("templateName").GetString()
            .Should().Be("端子线序检测");
        root.GetProperty("recommendedTemplate").GetProperty("templateVersion").GetString()
            .Should().Be("1.0.0");
        root.GetProperty("recommendedTemplate").GetProperty("scenarioKey").GetString()
            .Should().Be("wire-sequence-terminal");
        root.GetProperty("recommendedTemplate").GetProperty("matchMode").GetString()
            .Should().Be("template-first");
        root.GetProperty("generationMode").GetString().Should().Be("template_fill");
        root.GetProperty("templateLockLevel").GetString().Should().Be("strict");
        root.GetProperty("templateCandidates").GetArrayLength().Should().Be(2);
        root.GetProperty("templateCandidates")[0].GetProperty("scenarioKey").GetString()
            .Should().Be("wire-sequence-terminal");
        root.GetProperty("pendingParameters").GetArrayLength().Should().Be(1);
        root.GetProperty("pendingParameters")[0].GetProperty("operatorId").GetString().Should().Be("op_3");
        root.GetProperty("missingResources").GetArrayLength().Should().Be(1);
        root.GetProperty("missingResources")[0].GetProperty("resourceKey").GetString()
            .Should().Be("DeepLearning.ModelPath");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize prompt trace when returned")]
    public async Task HandleAsync_ShouldSerializePromptTrace()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = true,
                Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() },
                PromptTrace = new AiPromptTrace
                {
                    Mode = "modify",
                    Provider = "OpenAI Compatible",
                    Model = "gpt-5.4",
                    BaseUrl = "https://example.invalid/v1?token=secret",
                    SystemPrompt = "system",
                    UserPrompt = "user"
                }
            }));

        var resultJson = await handler.HandleAsync("trace");

        using var doc = JsonDocument.Parse(resultJson);
        var promptTrace = doc.RootElement.GetProperty("promptTrace");
        promptTrace.GetProperty("mode").GetString().Should().Be("modify");
        promptTrace.GetProperty("model").GetString().Should().Be("gpt-5.4");
        promptTrace.GetProperty("baseUrl").GetString().Should().Be("[hidden]");
        promptTrace.GetProperty("systemPrompt").GetString().Should().Be("[hidden]");
        promptTrace.GetProperty("userPrompt").GetString().Should().Be("[hidden]");
        resultJson.Should().NotContain("\"system\"");
        resultJson.Should().NotContain("\"user\"");
        resultJson.Should().NotContain("example.invalid");
        resultJson.Should().NotContain("secret");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize clarification payload")]
    public async Task HandleAsync_ShouldSerializeClarificationPayload()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = false,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
                ClarificationRequired = true,
                ErrorMessage = "当前需求还需要澄清 1 项关键信息。",
                TurnIntent = AiTurnIntents.NewFlow,
                InteractionState = AiInteractionStates.Clarifying,
                RouterConfidence = AiRouterConfidence.High,
                BlockingClarificationFields = ["object_type"],
                NonBlockingMissingFields = ["model_path"],
                RequirementBrief = new AiRequirementBrief
                {
                    ScenarioName = "缺陷检测",
                    ClarificationRequired = true,
                    BlockingClarificationFields = ["object_type"],
                    NonBlockingMissingFields = ["model_path"],
                    ClarificationQuestions =
                    [
                        new AiClarificationQuestion
                        {
                            Field = "object_type",
                            Question = "请确认检测对象是什么？",
                            Required = true,
                            Priority = "high",
                            Options = ["金属件", "包装箱"]
                        }
                    ]
                }
            }));

        var resultJson = await handler.HandleAsync("clarification");

        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        root.GetProperty("clarificationRequired").GetBoolean().Should().BeTrue();
        root.GetProperty("turnIntent").GetString().Should().Be(AiTurnIntents.NewFlow);
        root.GetProperty("interactionState").GetString().Should().Be(AiInteractionStates.Clarifying);
        root.GetProperty("routerConfidence").GetString().Should().Be(AiRouterConfidence.High);
        root.GetProperty("blockingClarificationFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("object_type");
        root.GetProperty("nonBlockingMissingFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("model_path");
        root.GetProperty("requirementBrief").GetProperty("scenarioName").GetString().Should().Be("缺陷检测");
        root.GetProperty("requirementBrief").GetProperty("clarificationQuestions").GetArrayLength().Should().Be(1);
        root.GetProperty("requirementBrief").GetProperty("blockingClarificationFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("object_type");
        root.GetProperty("requirementBrief").GetProperty("nonBlockingMissingFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("model_path");
        root.GetProperty("requirementBrief").GetProperty("clarificationQuestions")[0]
            .GetProperty("options").EnumerateArray()
            .Select(item => item.GetString()).Should().Contain("金属件").And.Contain("包装箱");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize cancelled completion status")]
    public async Task HandleAsync_ShouldSerializeCancelledCompletionStatus()
    {
        // Arrange
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = false,
                ErrorMessage = "用户已取消本次生成。",
                CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
                FailureType = AiFlowGenerationResult.FailureTypeUserCancelled,
                FailureSummary = new AiFailureSummary
                {
                    Category = "execution",
                    Code = "user_cancelled",
                    Message = "用户已取消本次生成。"
                },
                LastAttemptDiagnostics =
                [
                    new AiAttemptDiagnostic
                    {
                        AttemptNumber = 1,
                        Stage = "execution",
                        Summary = "用户主动取消",
                        Issues =
                        [
                            new AiValidationDiagnostic
                            {
                                Severity = AiValidationSeverity.Error,
                                Code = "user_cancelled",
                                Category = "execution",
                                Message = "用户主动取消"
                            }
                        ]
                    }
                ]
            }));

        // Act
        var resultJson = await handler.HandleAsync("取消测试", requestId: "req-cancel-1");

        // Assert
        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusCancelled);
        root.GetProperty("failureType").GetString().Should().Be(AiFlowGenerationResult.FailureTypeUserCancelled);
        root.GetProperty("failureSummary").GetString().Should().Be("用户已取消本次生成。");
        root.GetProperty("lastAttemptDiagnostics").GetArrayLength().Should().Be(1);
        root.GetProperty("requestId").GetString().Should().Be("req-cancel-1");
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler should serialize manual retry payload")]
    public async Task HandleAsync_ShouldSerializeManualRetryPayload()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var handler = new GenerateFlowMessageHandler(generationService, logger);

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(Task.FromResult(new AiFlowGenerationResult
            {
                Success = false,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusFailed,
                FailureType = AiFlowGenerationResult.FailureTypeManualRetryRequired,
                FailureSummary = new AiFailureSummary
                {
                    Category = "validation",
                    Code = "missing_parameter",
                    Message = "缺少关键参数"
                },
                LastAttemptDiagnostics =
                [
                    new AiAttemptDiagnostic
                    {
                        AttemptNumber = 1,
                        Stage = "validation",
                        Summary = "缺少关键参数",
                        Issues =
                        [
                            new AiValidationDiagnostic
                            {
                                Severity = AiValidationSeverity.Error,
                                Code = "missing_parameter",
                                Category = "validation",
                                Message = "缺少关键参数"
                            }
                        ]
                    }
                ],
                ManualRetry = new AiManualRetryInfo
                {
                    Required = true,
                    Stage = "validation",
                    Draft = "请仅补齐缺失参数后返回 JSON。",
                    Summary = "缺少关键参数",
                    RepairTarget = "补齐 ResultOutput 的输入参数",
                    LastOutputSummary = "最近一次输出缺少 ResultOutput 参数",
                    Diagnostics =
                    [
                        new AiAttemptDiagnostic
                        {
                            AttemptNumber = 1,
                            Stage = "validation",
                            Summary = "缺少关键参数"
                        }
                    ]
                }
            }));

        var resultJson = await handler.HandleAsync("修复参数", requestId: "req-manual-retry-1");

        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("failureType").GetString().Should().Be(AiFlowGenerationResult.FailureTypeManualRetryRequired);
        root.GetProperty("manualRetry").GetProperty("required").GetBoolean().Should().BeTrue();
        root.GetProperty("manualRetry").GetProperty("stage").GetString().Should().Be("validation");
        root.GetProperty("manualRetry").GetProperty("draft").GetString().Should().Be("请仅补齐缺失参数后返回 JSON。");
        root.GetProperty("manualRetry").GetProperty("diagnostics").GetArrayLength().Should().Be(1);
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler BuildFromPlan should enter generation with old blocked Plan and confirmed answers")]
    public async Task HandleAsync_BuildFromPlanWithOldBlockedPlanAndConfirmedAnswers_ShouldEnterGeneration()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var buildRunService = new CapturingBuildRunService(command => new AiFlowGenerationResult
                {
                    Success = true,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusCompleted,
                    GenerationMode = "build_from_plan_entry_reached",
                    Flow = new { operators = Array.Empty<object>(), connections = Array.Empty<object>() },
                    PlanId = command.Request.BuildFromPlan?.PlanSnapshot?.PlanId ?? string.Empty,
                    PlanHash = command.Request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty
                });
        var streamService = new AgentRunEventStreamService();
        var handler = new GenerateFlowMessageHandler(generationService, logger, buildRunService, streamService);
        var messages = new List<(string Type, string Payload)>();
        string? createdRunId = null;

        var plan = LegacyBlockedBuildFromPlanSnapshot();
        var resultJson = await handler.HandleAsync(
            description: "start build from confirmed plan",
            mode: GenerateFlowMode.New,
            requirementMode: AiRequirementModes.Strict,
            buildFromPlan: new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = ConfirmedRequirementAnswers(),
                OriginalUserPrompt = "start build from confirmed plan",
                MetadataOnly = true
            },
            useVisionAgentGenerateFlow: true,
            agentGenerateFlowMode: AiAgentGenerateFlowModes.Scripted,
            onMessage: (type, payload) => messages.Add((type, payload)),
            onAgentRunCreated: runId => createdRunId = runId);

        using var doc = JsonDocument.Parse(resultJson);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusCompleted);
        doc.RootElement.GetProperty("generationMode").GetString().Should().Be("build_from_plan_entry_reached");
        doc.RootElement.GetProperty("planId").GetString().Should().Be(plan.PlanId);
        doc.RootElement.GetProperty("planHash").GetString().Should().Be(plan.PlanHash);
        createdRunId.Should().StartWith("ar_");
        messages.Should().Contain(message => message.Type == "GenerateFlowAgentRunCreated");
        buildRunService.LastCommand.Should().NotBeNull();
        buildRunService.LastCommand!.Transport.Should().Be(BuildCommandTransports.WebMessage);
        buildRunService.LastCommand.RunId.Should().Be(createdRunId);
        buildRunService.LastCommand.PersistResult.Should().BeFalse();
        buildRunService.LastCommand.Request.AgentRunId.Should().Be(createdRunId);
        buildRunService.LastCommand.Request.BuildFromPlan.Should().NotBeNull();
        buildRunService.LastCommand.Request.BuildFromPlan!.PlanSnapshot!.CanBuild.Should().BeFalse();
        buildRunService.LastCommand.Request.BuildFromPlan.ConfirmedAnswers.Should().HaveCount(4);
        await generationService.DidNotReceive().GenerateFlowAsync(
            Arg.Any<AiFlowGenerationRequest>(),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());
    }

    [Fact(DisplayName = "GenerateFlowMessageHandler BuildFromPlan failure should serialize canonical BuildReadiness")]
    public async Task HandleAsync_BuildFromPlanFailure_ShouldSerializeBuildReadiness()
    {
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var readiness = new VisionAgentBuildReadinessSnapshot
        {
            CanBuild = false,
            RemainingFields = ["image_source", "acceptance_criteria"],
            ResolvedFields = ["inspection_object", "task_type"],
            Blockers =
            [
                new VisionAgentBuildBlocker
                {
                    Id = "hard_requirement:image_source",
                    Category = VisionAgentBuildBlockerCategories.HardRequirement,
                    Field = "image_source",
                    BlocksBuild = true,
                    ResolutionMode = VisionAgentBuildBlockerResolutionModes.AnswerQuestion
                }
            ],
            PrimaryMessage = "Need canonical fields before Build.",
            ContractVersion = VisionAgentPlanContractVersions.V2
        };
        var buildRunService = new CapturingBuildRunService(command => new AiFlowGenerationResult
            {
                Success = false,
                CompletionStatus = AiFlowGenerationResult.CompletionStatusClarificationRequired,
                FailureType = AiFlowGenerationResult.FailureTypeClarificationRequired,
                ClarificationRequired = true,
                BuildReadiness = readiness,
                BlockingClarificationFields = ["image_source", "acceptance_criteria"],
                RequirementMaturity = new AiRequirementMaturityResult
                {
                    CanPlan = true,
                    CanBuild = false,
                    MissingFields = ["image_source", "acceptance_criteria"],
                    PublicReason = "Need canonical fields before Build."
                },
                PlanId = command.Request.BuildFromPlan?.PlanSnapshot?.PlanId ?? string.Empty,
                PlanHash = command.Request.BuildFromPlan?.PlanSnapshot?.PlanHash ?? string.Empty
            });
        var streamService = new AgentRunEventStreamService();
        var handler = new GenerateFlowMessageHandler(generationService, logger, buildRunService, streamService);
        string? createdRunId = null;

        var plan = LegacyBlockedBuildFromPlanSnapshot();
        var resultJson = await handler.HandleAsync(
            description: "start build from confirmed plan",
            mode: GenerateFlowMode.New,
            requirementMode: AiRequirementModes.Strict,
            buildFromPlan: new VisionAgentBuildFromPlanRequest
            {
                PlanId = plan.PlanId,
                PlanHash = plan.PlanHash,
                PlanSnapshot = plan,
                ConfirmedAnswers = ConfirmedRequirementAnswers(),
                OriginalUserPrompt = "start build from confirmed plan",
                MetadataOnly = true
            },
            useVisionAgentGenerateFlow: false,
            onAgentRunCreated: runId => createdRunId = runId);

        using var doc = JsonDocument.Parse(resultJson);
        var root = doc.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusClarificationRequired);
        root.GetProperty("planId").GetString().Should().Be(plan.PlanId);
        root.GetProperty("planHash").GetString().Should().Be(plan.PlanHash);
        var serializedReadiness = root.GetProperty("buildReadiness");
        serializedReadiness.GetProperty("canBuild").GetBoolean().Should().BeFalse();
        serializedReadiness.GetProperty("remainingFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("image_source", "acceptance_criteria");
        root.GetProperty("blockingClarificationFields").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("image_source", "acceptance_criteria");
        createdRunId.Should().StartWith("ar_");
        buildRunService.LastCommand.Should().NotBeNull();
        buildRunService.LastCommand!.Transport.Should().Be(BuildCommandTransports.WebMessage);
        buildRunService.LastCommand.Request.AgentRunId.Should().Be(createdRunId);
        await generationService.DidNotReceive().GenerateFlowAsync(
            Arg.Any<AiFlowGenerationRequest>(),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());
    }

    private sealed class CapturingBuildRunService : IVisionAgentBuildRunService
    {
        private readonly Func<BuildCommand, AiFlowGenerationResult> _resultFactory;

        public CapturingBuildRunService(Func<BuildCommand, AiFlowGenerationResult> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public BuildCommand? LastCommand { get; private set; }

        public VisionAgentWorkspaceSnapshotMutationResult PrepareBuildAssociation(BuildCommand command) =>
            new() { Success = true };

        public Task<VisionAgentBuildRunResult> RunAsync(
            BuildCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            var result = _resultFactory(command);
            var outcome = new CanonicalBuildOutcome
            {
                Result = result,
                RunId = command.RunId ?? command.Request.AgentRunId ?? string.Empty,
                RequestId = command.RequestId ?? string.Empty,
                Transport = command.Transport,
                CompletionStatus = result.CompletionStatus,
                FailureType = result.FailureType ?? string.Empty,
                FailureCode = result.FailureSummary?.Code ?? string.Empty,
                PlanId = result.PlanId,
                PlanHash = result.PlanHash,
                ContractVersion = result.ContractVersion,
                AnswerSetFingerprint = result.AnswerSetFingerprint,
                RequestedMode = result.RequestedMode,
                EffectiveMode = result.EffectiveMode,
                ToolLoopEntered = result.ToolLoopEntered,
                FallbackReason = result.FallbackReason,
                BuildReadiness = result.BuildReadiness,
                WorkflowDiff = result.BuildResult?.WorkflowDiff,
                ApplyGate = result.BuildResult?.ApplyGate,
                Persisted = true
            };

            return Task.FromResult(new VisionAgentBuildRunResult(outcome, null));
        }
    }

    private static VisionAgentPlanModeResult LegacyBlockedBuildFromPlanSnapshot()
    {
        var result = new VisionAgentPlanModeResult
        {
            PlanId = "plan-entry",
            OriginalUserPrompt = "start build from confirmed plan",
            Goal = "logo surface defect inspection",
            Intent = AiVisionTaskTypes.Unknown,
            Confidence = "low",
            RequirementUnderstanding = ["Legacy snapshot was captured before confirmed answers were applied."],
            RecommendedRoute = new VisionAgentRecommendedRoute
            {
                RouteId = "surface_defect_route",
                Title = "Surface defect route",
                Summary = "Acquisition, defect detection, judgment, and output.",
                Operators = ["ImageAcquisition", "SurfaceDefectDetection", "ResultJudgment", "ResultOutput"],
                TemplateDecision = "planner_route"
            },
            ClarificationQuestions = [],
            BlockingReasons =
            [
                "hard_requirement:inspection_object_missing",
                "hard_requirement:task_type_missing",
                "hard_requirement:image_source_missing",
                "hard_requirement:acceptance_criteria_missing"
            ],
            CanBuild = false,
            SemanticExtraction = new VisionAgentSemanticExtractionResult
            {
                IsVisionRequest = true,
                Intent = "new_flow",
                TaskType = AiVisionTaskTypes.Unknown,
                Source = VisionAgentSemanticSources.RuleFallback,
                MetadataOnly = true
            },
            RequirementMaturity = new AiRequirementMaturityResult
            {
                Maturity = AiRequirementMaturity.Ambiguous,
                TaskType = AiVisionTaskTypes.Unknown,
                CanPlan = false,
                CanBuild = false,
                MissingFields = ["inspection_object", "task_type", "image_source", "acceptance_criteria"],
                BlockingReasons = ["inspection_object_missing", "task_type_missing", "image_source_missing", "acceptance_criteria_missing"],
                PublicReason = "Legacy snapshot was not buildable before answers."
            },
            MetadataOnly = true
        };

        return result with
        {
            PlanHash = VisionAgentOrchestrator.ComputePlanHash(result)
        };
    }

    private static List<VisionAgentPlanAnswer> ConfirmedRequirementAnswers()
    {
        return
        [
            TextAnswer(VisionAgentPlanAnswerFields.InspectionObject, "logo area"),
            TextAnswer(VisionAgentPlanAnswerFields.TaskType, AiVisionTaskTypes.SurfaceDefect),
            TextAnswer(VisionAgentPlanAnswerFields.ImageSource, "camera"),
            TextAnswer(VisionAgentPlanAnswerFields.AcceptanceCriteria, "scratch is NG")
        ];
    }

    private static VisionAgentPlanAnswer TextAnswer(string field, string value)
    {
        return new VisionAgentPlanAnswer
        {
            Field = field,
            Value = value,
            Origin = VisionAgentPlanAnswerOrigins.ExplicitUserText
        };
    }

}
