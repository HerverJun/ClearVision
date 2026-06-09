using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
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
                RequirementBrief = new AiRequirementBrief
                {
                    ScenarioName = "缺陷检测",
                    ClarificationRequired = true,
                    ClarificationQuestions =
                    [
                        new AiClarificationQuestion
                        {
                            Field = "object_type",
                            Question = "请确认检测对象是什么？",
                            Required = true,
                            Priority = "high"
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
        root.GetProperty("requirementBrief").GetProperty("scenarioName").GetString().Should().Be("缺陷检测");
        root.GetProperty("requirementBrief").GetProperty("clarificationQuestions").GetArrayLength().Should().Be(1);
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

}
