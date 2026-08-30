using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Handlers;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Events;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
public class WebMessageHandlerTests
{
    [Fact]
    public async Task HandleAsync_UpdateFlowCommand_ShouldBlockLegacyExecutionBridge()
    {
        var operatorFactory = new OperatorFactory();
        var projectRepository = Substitute.For<IProjectRepository>();
        var flowStorageRoot = Path.Combine(Path.GetTempPath(), "ClearVision.WebMessageHandlerTests", Guid.NewGuid().ToString("N"));
        var flowStorage = new JsonFileProjectFlowStorage(flowStorageRoot);
        var project = new Project("WebMessage Flow");

        projectRepository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        projectRepository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        projectRepository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);

        try
        {
            await using var serviceProvider = BuildServiceProvider(services =>
            {
                services.AddSingleton(projectRepository);
                services.AddSingleton<IProjectFlowStorage>(flowStorage);
                services.AddSingleton<IOperatorFactory>(operatorFactory);
                services.AddScoped<ProjectService>();
            });

            var handler = CreateHandler(serviceProvider, operatorFactory);
            var payload = JsonSerializer.Serialize(new UpdateFlowCommand
            {
                ProjectId = project.Id,
                Flow = new FlowData
                {
                    Operators =
                    [
                        new OperatorData
                        {
                            Id = Guid.NewGuid(),
                            Name = "ResultOutput",
                            Type = nameof(OperatorType.ResultOutput),
                            X = 120,
                            Y = 80
                        }
                    ],
                    Connections = []
                }
            });

            var response = await handler.HandleAsync(new WebMessage
            {
                Type = nameof(UpdateFlowCommand),
                Id = "req-update-flow",
                Payload = payload
            });

            response.Success.Should().BeFalse();
            response.Error.Should().Contain("Legacy execution WebMessage");
            var savedFlow = await flowStorage.LoadFlowJsonAsync(project.Id);
            savedFlow.Should().BeNull();
            (await flowStorage.LoadMetadataAsync(project.Id)).Should().BeNull();
            await projectRepository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
        }
        finally
        {
            if (Directory.Exists(flowStorageRoot))
            {
                Directory.Delete(flowStorageRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HandleAsync_ExecuteOperatorCommand_ShouldBlockLegacyExecutionBridge()
    {
        var operatorFactory = new OperatorFactory();
        var projectRepository = Substitute.For<IProjectRepository>();
        var flowStorage = Substitute.For<IProjectFlowStorage>();
        var flowExecutionService = Substitute.For<IFlowExecutionService>();
        var project = new Project("Stored Flow");
        var operatorId = Guid.NewGuid();
        var operatorDto = CreateOperatorDto(operatorFactory, OperatorType.ResultOutput, "ResultOutput", operatorId);
        var flowDto = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "MainFlow",
            Operators = [operatorDto],
            Connections = []
        };

        projectRepository.GetAllAsync().Returns(Task.FromResult<IEnumerable<Project>>(new[] { project }));
        projectRepository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        flowStorage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(JsonSerializer.Serialize(flowDto)));
        flowExecutionService.ExecuteOperatorAsync(
                Arg.Any<GovernedOperatorExecutionContext>(),
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OperatorExecutionResult
            {
                OperatorId = operatorId,
                OperatorName = "ResultOutput",
                IsSuccess = true,
                OutputData = new Dictionary<string, object> { ["Status"] = "OK" },
                ExecutionTimeMs = 8
            }));

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(projectRepository);
            services.AddSingleton(flowStorage);
            services.AddSingleton(flowExecutionService);
            services.AddSingleton<IOperatorFactory>(operatorFactory);
            services.AddScoped<ProjectService>();
        });

        var handler = CreateHandler(serviceProvider, operatorFactory);
        var payload = JsonSerializer.Serialize(new ExecuteOperatorCommand
        {
            OperatorId = operatorId,
            Inputs = new Dictionary<string, object> { ["Value"] = 42 }
        });

        var response = await handler.HandleAsync(new WebMessage
        {
            Type = nameof(ExecuteOperatorCommand),
            Id = "req-execute-operator",
            Payload = payload
        });

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Legacy execution WebMessage");
        await flowExecutionService.DidNotReceiveWithAnyArgs().ExecuteOperatorAsync(
            Arg.Any<GovernedOperatorExecutionContext>(),
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_StopInspectionCommand_ShouldReturnExplicitFailure()
    {
        var operatorFactory = new OperatorFactory();

        await using var serviceProvider = BuildServiceProvider(_ => { });
        var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.HandleAsync(new WebMessage
        {
            Type = nameof(StopInspectionCommand),
            Id = "req-stop-inspection",
            Payload = "{}"
        });

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Legacy execution WebMessage");
    }

    [Fact]
    public async Task HandleAsync_StartInspectionCommand_ShouldBlockLegacyExecutionBridge()
    {
        var operatorFactory = new OperatorFactory();
        var inspectionService = Substitute.For<IInspectionService>();

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(inspectionService);
        });
        var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.HandleAsync(new WebMessage
        {
            Type = nameof(StartInspectionCommand),
            Id = "req-start-inspection",
            Payload = JsonSerializer.Serialize(new StartInspectionCommand
            {
                ProjectId = Guid.NewGuid(),
                CameraId = "camera-1"
            })
        });

        response.Success.Should().BeFalse();
        response.Error.Should().Contain("Legacy execution WebMessage");
        await inspectionService.DidNotReceiveWithAnyArgs().ExecuteSingleAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateInspectionCompletedMessage_ShouldAvoidDuplicatingRawJsonPayloads()
    {
        var projectId = Guid.NewGuid();
        var result = new InspectionResult(projectId);
        result.SetResult(InspectionStatus.OK, processingTimeMs: 42);
        result.SetOutputDataJson("{\"BlobCount\":3}");
        result.SetAnalysisDataJson("{\"Cards\":[{\"Title\":\"ok\"}]}");
        result.SetOutputImage(new byte[] { 1, 2, 3 });

        var outputData = new Dictionary<string, object> { ["BlobCount"] = 3 };
        var analysisData = new Dictionary<string, object> { ["Summary"] = "OK" };

        var message = WebMessageHandler.CreateInspectionCompletedMessage(
            result,
            projectId,
            outputData,
            analysisData);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
        var root = doc.RootElement;

        root.GetProperty("messageType").GetString().Should().Be("inspectionCompleted");
        root.GetProperty("outputImage").GetString().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        root.GetProperty("outputData").GetProperty("BlobCount").GetInt32().Should().Be(3);
        root.GetProperty("analysisData").GetProperty("Summary").GetString().Should().Be("OK");
        root.TryGetProperty("outputDataJson", out _).Should().BeFalse();
        root.TryGetProperty("analysisDataJson", out _).Should().BeFalse();
    }

    [Fact]
    public void CreateInspectionCompletedMessage_WithImageId_ShouldOmitInlineOutputImage()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var result = new InspectionResult(projectId, imageId);
        result.SetResult(InspectionStatus.OK, processingTimeMs: 42);
        result.SetOutputImage(new byte[] { 1, 2, 3 });

        var message = WebMessageHandler.CreateInspectionCompletedMessage(
            result,
            projectId,
            outputData: null,
            analysisData: null);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
        var root = doc.RootElement;

        root.GetProperty("imageId").GetGuid().Should().Be(imageId);
        root.GetProperty("outputImage").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void PendingWebMessageQueue_ShouldDropOldestMessagesWhenCapacityIsExceeded()
    {
        var operatorFactory = new OperatorFactory();
        using var serviceProvider = BuildServiceProvider(_ => { });
        var handler = CreateHandler(serviceProvider, operatorFactory);

        var enqueueMethod = typeof(WebMessageHandler).GetMethod(
            "TryEnqueuePendingWebMessage",
            BindingFlags.Instance | BindingFlags.NonPublic);
        enqueueMethod.Should().NotBeNull();

        for (var index = 0; index < 520; index++)
        {
            var enqueued = (bool)enqueueMethod!.Invoke(handler, [$"message-{index}", null])!;
            enqueued.Should().BeTrue();
        }

        var countField = typeof(WebMessageHandler).GetField(
            "_pendingWebMessageCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var droppedField = typeof(WebMessageHandler).GetField(
            "_droppedWebMessageCount",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var queueField = typeof(WebMessageHandler).GetField(
            "_pendingWebMessages",
            BindingFlags.Instance | BindingFlags.NonPublic);

        countField.Should().NotBeNull();
        droppedField.Should().NotBeNull();
        queueField.Should().NotBeNull();

        countField!.GetValue(handler).Should().Be(512);
        droppedField!.GetValue(handler).Should().Be(8L);
        var queue = queueField!.GetValue(handler).Should().BeOfType<ConcurrentQueue<PendingWebMessage>>().Subject;
        queue.Should().HaveCount(512);
        queue.First().Json.Should().Be("message-8");
        queue.Last().Json.Should().Be("message-519");
    }

    [Fact]
    public async Task CancelGenerateFlow_ShouldCancelActiveGenerateToken()
    {
        var operatorFactory = new OperatorFactory();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Engineer.ToString()));
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        AiFlowGenerationRequest? capturedRequest = null;

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(async callInfo =>
            {
                capturedRequest = callInfo.ArgAt<AiFlowGenerationRequest>(0);
                capturedToken = callInfo.ArgAt<CancellationToken>(3);
                started.TrySetResult(true);

                try
                {
                    await Task.Delay(Timeout.Infinite, capturedToken);
                }
                catch (OperationCanceledException) when (capturedToken.IsCancellationRequested)
                {
                }

                return new AiFlowGenerationResult
                {
                    Success = false,
                    ErrorMessage = "用户已取消本次生成。",
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
                    FailureType = AiFlowGenerationResult.FailureTypeUserCancelled
                };
            });

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(authService);
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });

        var handler = CreateHandler(serviceProvider, operatorFactory);
        const string requestId = "req-active-1";
        const string sessionId = "session-active-1";
        var generateJson = CreateAuthenticatedEnvelope(
            "GenerateFlow",
            "token-a",
            new { description = "生成流程", sessionId, requestId });
        var cancelJson = CreateAuthenticatedEnvelope(
            "CancelGenerateFlow",
            "token-a",
            new { sessionId, requestId });

        var generationTask = handler.DispatchWebMessageAsync(generateJson, WebMessageAdmissionService.TrustedOrigin);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancelResponse = await handler.DispatchWebMessageAsync(
            cancelJson,
            WebMessageAdmissionService.TrustedOrigin);
        await generationTask.WaitAsync(TimeSpan.FromSeconds(5));

        cancelResponse.Success.Should().BeTrue();
        capturedToken.IsCancellationRequested.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.OwnerHash.Should().Be(AuthenticatedOwnerResolver.ResolveOwnerHash("user-a"));

        var activeRequestsField = typeof(WebMessageHandler)
            .GetField("_activeGenerateFlowRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var activeRequests = activeRequestsField.GetValue(handler)!;
        var count = (int)activeRequests.GetType().GetProperty("Count")!.GetValue(activeRequests)!;
        count.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GenerateFlow_DuplicateRequestOrOwnerSession_ShouldReturnConflictWithoutReplacingActiveTask(
        bool reuseRequestId)
    {
        var operatorFactory = new OperatorFactory();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Engineer.ToString()));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken activeToken = default;

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>>())
            .Returns(async callInfo =>
            {
                activeToken = callInfo.ArgAt<CancellationToken>(3);
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, activeToken);
                }
                catch (OperationCanceledException) when (activeToken.IsCancellationRequested)
                {
                }

                return new AiFlowGenerationResult
                {
                    Success = false,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
                    FailureType = AiFlowGenerationResult.FailureTypeUserCancelled
                };
            });

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(authService);
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        const string activeRequestId = "request-active";
        const string activeSessionId = "session-active";
        var conflictingRequestId = reuseRequestId ? activeRequestId : "request-conflict";
        var conflictingSessionId = reuseRequestId ? "session-conflict" : activeSessionId;
        var activeTask = handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "GenerateFlow",
                "token-a",
                new { description = "active", requestId = activeRequestId, sessionId = activeSessionId }),
            WebMessageAdmissionService.TrustedOrigin);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var conflictDispatch = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "GenerateFlow",
                "token-a",
                new
                {
                    description = "must not replace active generation",
                    requestId = conflictingRequestId,
                    sessionId = conflictingSessionId
                }),
            WebMessageAdmissionService.TrustedOrigin);

        conflictDispatch.Success.Should().BeTrue();
        using (var conflictDocument = JsonDocument.Parse(GetPendingMessages(handler).Last()))
        {
            var conflict = conflictDocument.RootElement;
            conflict.GetProperty("success").GetBoolean().Should().BeFalse();
            conflict.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.Conflict);
            conflict.GetProperty("requestId").GetString().Should().Be(conflictingRequestId);
            conflict.GetProperty("sessionId").GetString().Should().Be(conflictingSessionId);
        }
        generationService.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(IAiFlowGenerationService.GenerateFlowAsync))
            .Should()
            .Be(1);

        await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "CancelGenerateFlow",
                "token-a",
                new { requestId = activeRequestId, sessionId = activeSessionId }),
            WebMessageAdmissionService.TrustedOrigin);
        await activeTask.WaitAsync(TimeSpan.FromSeconds(5));
        activeToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task HandleGenerateFlowCommand_MalformedBuildFromPlan_ShouldReturnControlledFailureWithoutGeneration()
    {
        var operatorFactory = new OperatorFactory();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Engineer.ToString()));
        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(authService);
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });
        var handler = CreateHandler(serviceProvider, operatorFactory);
        const string requestId = "req-malformed-build";
        const string sessionId = "session-malformed-build";
        var messageJson = CreateAuthenticatedEnvelope(
            "GenerateFlow",
            "token-a",
            new
            {
                description = "start build",
                sessionId,
                requestId,
                buildFromPlan = new
                {
                    planId = "plan-malformed",
                    planSnapshot = new { planId = "plan-malformed" },
                    confirmedAnswers = new[]
                    {
                        new { field = (object)new { not = "a-string" }, value = "camera" }
                    }
                }
            });

        var dispatchResponse = await handler.DispatchWebMessageAsync(
            messageJson,
            WebMessageAdmissionService.TrustedOrigin);
        dispatchResponse.Success.Should().BeTrue();

        await generationService.DidNotReceiveWithAnyArgs().GenerateFlowAsync(
            Arg.Any<AiFlowGenerationRequest>(),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());
        var responseJson = GetPendingMessages(handler).Last();
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("requestId").GetString().Should().Be(requestId);
        root.GetProperty("sessionId").GetString().Should().Be(sessionId);
        root.GetProperty("status").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        root.GetProperty("completionStatus").GetString().Should().Be(AiFlowGenerationResult.CompletionStatusFailed);
        root.GetProperty("failureType").GetString().Should().Be(AiFlowGenerationResult.FailureTypeSystemError);
        root.GetProperty("interactionState").GetString().Should().Be(AiInteractionStates.Failed);
        root.GetProperty("clarificationRequired").GetBoolean().Should().BeFalse();
        root.GetProperty("errorMessage").GetString().Should().Be("BuildFromPlan payload is invalid.");
        root.GetProperty("failureSummary").GetString().Should().Contain("build_from_plan_payload_invalid");
        root.GetProperty("firstFixRecommendation").GetString().Should().NotBeNullOrWhiteSpace();
        responseJson.Should().NotContain("JsonException");
        responseJson.Should().NotContain("System.Text.Json");
        responseJson.Should().NotContain(" at ");
        responseJson.Should().NotContain("C:\\");
        responseJson.Should().NotContain("$.payload");
        responseJson.Should().NotContain("not-a-string");
    }

    [Theory]
    [InlineData("http://app.local")]
    [InlineData("https://app.local:444")]
    [InlineData("https://app.local.evil.example")]
    [InlineData("https://localhost")]
    [InlineData("file:///index.html")]
    public async Task DispatchWebMessageAsync_WrongOrigin_ShouldRejectBeforeTokenValidation(string source)
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Engineer.ToString()));
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope("ListAiSessions", "token-a", new { }),
            source);

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.Forbidden);
        await authService.DidNotReceive().GetSessionAsync(Arg.Any<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-token")]
    [InlineData("logged-out-token")]
    public async Task DispatchWebMessageAsync_MissingInvalidOrLoggedOutToken_ShouldReturnAuthRequired(string token)
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService();
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope("ListAiSessions", token, new { }),
            WebMessageAdmissionService.TrustedOrigin);

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.AuthRequired);
        if (!string.IsNullOrEmpty(token))
        {
            GetPendingMessages(handler).Should().OnlyContain(message =>
                !message.Contains(token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task DispatchWebMessageAsync_InsufficientPolicy_ShouldNotInvokeFilePickerBusinessHandler()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("operator-token", "operator-user", UserRole.Operator.ToString()));
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                nameof(PickFileCommand),
                "operator-token",
                new { parameterName = "modelPath", filter = "All Files|*.*" }),
            WebMessageAdmissionService.TrustedOrigin);

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.Forbidden);
        var pending = GetPendingMessages(handler);
        pending.Should().ContainSingle();
        pending.Single().Should().Contain("WebMessageRejected");
        pending.Single().Should().NotContain(nameof(FilePickedEvent));
    }

    [Fact]
    public async Task HandleAsync_ActiveCommand_ShouldNotBypassOriginTokenAdmission()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Engineer.ToString()));
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.HandleAsync(new WebMessage
        {
            Type = "ListAiSessions",
            Id = "alternate-entry",
            Payload = JsonSerializer.Serialize(new
            {
                bridge = new { token = "token-a", bindingId = "forged-binding", navigationEpoch = 1 }
            })
        });

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.Forbidden);
        await authService.DidNotReceive().GetSessionAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task DispatchWebMessageAsync_UnknownCommand_ShouldDefaultDenyWithoutValidatingToken()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Admin.ToString()));
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope("UnlistedDangerousCommand", "token-a", new { }),
            WebMessageAdmissionService.TrustedOrigin);

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.Forbidden);
        await authService.DidNotReceive().GetSessionAsync(Arg.Any<string>());
    }

    [Theory]
    [InlineData(nameof(ExecuteOperatorCommand))]
    [InlineData(nameof(UpdateFlowCommand))]
    [InlineData(nameof(StartInspectionCommand))]
    [InlineData(nameof(StopInspectionCommand))]
    public async Task DispatchWebMessageAsync_LegacyExecutionCommands_ShouldRemainStableForbidden(string messageType)
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("token-a", "user-a", UserRole.Admin.ToString()));
        await using var serviceProvider = BuildServiceProvider(services => services.AddSingleton(authService));
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(messageType, "token-a", new { }),
            WebMessageAdmissionService.TrustedOrigin);

        response.Success.Should().BeFalse();
        GetResponseCode(response).Should().Be(WebMessageErrorCodes.Forbidden);
        response.Error.Should().Be("Legacy execution WebMessage is disabled.");
        await authService.DidNotReceive().GetSessionAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task AiSessionCommands_ShouldListOnlyCurrentOwnerAndNeverProjectOwnerHash()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(
            ("token-a", "user-a", UserRole.Operator.ToString()),
            ("token-b", "user-b", UserRole.Operator.ToString()));
        var conversationRoot = Path.Combine(Path.GetTempPath(), "cv-webmessage-owner-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var conversations = new ConversationalFlowService(conversationRoot);
            var ownerA = AuthenticatedOwnerResolver.ResolveOwnerHash("user-a");
            var ownerB = AuthenticatedOwnerResolver.ResolveOwnerHash("user-b");
            conversations.PrepareContext(ownerA, new AiFlowGenerationRequest("owner A", SessionId: "session-a")
            {
                OwnerHash = ownerA
            });
            conversations.PrepareContext(ownerB, new AiFlowGenerationRequest("owner B", SessionId: "session-b")
            {
                OwnerHash = ownerB
            });

            await using var serviceProvider = BuildServiceProvider(services =>
            {
                services.AddSingleton(authService);
                services.AddSingleton<IConversationalFlowService>(conversations);
            });
            using var handler = CreateHandler(serviceProvider, operatorFactory);

            await handler.DispatchWebMessageAsync(
                CreateAuthenticatedEnvelope("ListAiSessions", "token-a", new { }, bindingId: "binding-a"),
                WebMessageAdmissionService.TrustedOrigin);
            var listJson = GetPendingMessages(handler).Last();
            using (var listDocument = JsonDocument.Parse(listJson))
            {
                var sessions = listDocument.RootElement.GetProperty("payload").GetProperty("sessions");
                sessions.GetArrayLength().Should().Be(1);
                sessions[0].GetProperty("sessionId").GetString().Should().Be("session-a");
            }
            listJson.ToLowerInvariant().Should().NotContain("ownerhash");

            await handler.DispatchWebMessageAsync(
                CreateAuthenticatedEnvelope(
                    "GetAiSession",
                    "token-a",
                    new { sessionId = "session-a" },
                    bindingId: "binding-a"),
                WebMessageAdmissionService.TrustedOrigin);
            var ownGetJson = GetPendingMessages(handler).Last();
            ownGetJson.Should().Contain("session-a");
            ownGetJson.ToLowerInvariant().Should().NotContain("ownerhash");

            await handler.DispatchWebMessageAsync(
                CreateAuthenticatedEnvelope(
                    "GetAiSession",
                    "token-b",
                    new { sessionId = "session-a" },
                    bindingId: "binding-b"),
                WebMessageAdmissionService.TrustedOrigin);
            using var wrongOwnerDocument = JsonDocument.Parse(GetPendingMessages(handler).Last());
            var wrongOwnerPayload = wrongOwnerDocument.RootElement.GetProperty("payload");
            wrongOwnerPayload.GetProperty("success").GetBoolean().Should().BeFalse();
            wrongOwnerPayload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.NotFound);
            wrongOwnerPayload.GetProperty("session").ValueKind.Should().Be(JsonValueKind.Null);

            await handler.DispatchWebMessageAsync(
                CreateAuthenticatedEnvelope(
                    "DeleteAiSession",
                    "token-b",
                    new { sessionId = "session-a" },
                    bindingId: "binding-b"),
                WebMessageAdmissionService.TrustedOrigin);
            using var wrongOwnerDeleteDocument = JsonDocument.Parse(GetPendingMessages(handler).Last());
            var wrongOwnerDeletePayload = wrongOwnerDeleteDocument.RootElement.GetProperty("payload");
            wrongOwnerDeletePayload.GetProperty("success").GetBoolean().Should().BeFalse();
            wrongOwnerDeletePayload.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.NotFound);
            conversations.GetSession(ownerA, "session-a").Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(conversationRoot))
            {
                Directory.Delete(conversationRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateFlow_WrongOwnerSession_ShouldReturnOpaqueNotFound()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(("token-b", "user-b", UserRole.Engineer.ToString()));
        var generationService = Substitute.For<IAiFlowGenerationService>();
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>>())
            .Returns(Task.FromException<AiFlowGenerationResult>(new ConversationSessionAccessException()));

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(authService);
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>());
            services.AddScoped<GenerateFlowMessageHandler>();
        });
        using var handler = CreateHandler(serviceProvider, operatorFactory);

        var response = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "GenerateFlow",
                "token-b",
                new
                {
                    description = "forge another owner's continuation",
                    sessionId = "owner-a-session",
                    requestId = "wrong-owner-generate"
                },
                bindingId: "binding-b"),
            WebMessageAdmissionService.TrustedOrigin);

        response.Success.Should().BeTrue();
        var resultJson = GetPendingMessages(handler).Last();
        using var resultDocument = JsonDocument.Parse(resultJson);
        resultDocument.RootElement.GetProperty("type").GetString().Should().Be("GenerateFlowResult");
        resultDocument.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        resultDocument.RootElement.GetProperty("code").GetString().Should().Be(WebMessageErrorCodes.NotFound);
        resultJson.ToLowerInvariant().Should().NotContain("ownerhash");
        resultJson.Should().NotContain("user-a");
    }

    [Fact]
    public async Task GenerateFlow_RebindToAnotherOwner_ShouldCancelAndSuppressOldOwnerDelivery()
    {
        var operatorFactory = new OperatorFactory();
        var authService = CreateAuthService(
            ("token-a", "user-a", UserRole.Engineer.ToString()),
            ("token-b", "user-b", UserRole.Engineer.ToString()));
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken ownerAToken = default;
        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<GenerateFlowAttachmentReport>>())
            .Returns(async callInfo =>
            {
                ownerAToken = callInfo.ArgAt<CancellationToken>(3);
                callInfo.ArgAt<Action<string>>(1)("owner-a-secret-progress");
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ownerAToken);
                }
                catch (OperationCanceledException) when (ownerAToken.IsCancellationRequested)
                {
                }

                return new AiFlowGenerationResult
                {
                    Success = false,
                    CompletionStatus = AiFlowGenerationResult.CompletionStatusCancelled,
                    ErrorMessage = "owner-a-secret-result"
                };
            });

        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddSingleton(authService);
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });
        using var handler = CreateHandler(serviceProvider, operatorFactory);
        const string requestId = "owner-a-request";
        const string sessionId = "owner-a-session";
        var ownerAGeneration = handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "GenerateFlow",
                "token-a",
                new { description = "owner A generation", sessionId, requestId },
                bindingId: "binding-a"),
            WebMessageAdmissionService.TrustedOrigin);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                WebMessageAdmissionService.BindingChangedMessageType,
                "token-b",
                new { reason = "login" },
                bindingId: "binding-b"),
            WebMessageAdmissionService.TrustedOrigin);
        await ownerAGeneration.WaitAsync(TimeSpan.FromSeconds(5));
        ownerAToken.IsCancellationRequested.Should().BeTrue();

        var cancelResponse = await handler.DispatchWebMessageAsync(
            CreateAuthenticatedEnvelope(
                "CancelGenerateFlow",
                "token-b",
                new { sessionId, requestId },
                bindingId: "binding-b"),
            WebMessageAdmissionService.TrustedOrigin);
        cancelResponse.Success.Should().BeTrue();

        var pendingJson = string.Join("\n", GetPendingMessages(handler));
        pendingJson.Should().NotContain("owner-a-secret-progress");
        pendingJson.Should().NotContain("owner-a-secret-result");
        pendingJson.Should().Contain(WebMessageErrorCodes.NotFound);
        pendingJson.Should().Contain("BridgeBindingChangedResult");
    }

    private static IAuthService CreateAuthService(
        params (string Token, string UserId, string Role)[] sessions)
    {
        var authService = Substitute.For<IAuthService>();
        authService.GetSessionAsync(Arg.Any<string>())
            .Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(null));
        foreach (var session in sessions)
        {
            authService.GetSessionAsync(session.Token)
                .Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new()
                {
                    UserId = session.UserId,
                    Username = session.UserId,
                    Role = session.Role
                }));
        }

        return authService;
    }

    private static string CreateAuthenticatedEnvelope(
        string messageType,
        string token,
        object payload,
        string bindingId = "binding-test",
        long navigationEpoch = 1) =>
        JsonSerializer.Serialize(new
        {
            messageType,
            requestId = $"wm-{Guid.NewGuid():N}",
            payload,
            bridge = new
            {
                token,
                bindingId,
                navigationEpoch
            }
        });

    private static string GetResponseCode(WebMessageResponse response)
    {
        response.Data.Should().NotBeNullOrWhiteSpace();
        using var document = JsonDocument.Parse(response.Data!);
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static IReadOnlyList<string> GetPendingMessages(WebMessageHandler handler)
    {
        var queueField = typeof(WebMessageHandler).GetField(
            "_pendingWebMessages",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = queueField.GetValue(handler)
            .Should()
            .BeOfType<ConcurrentQueue<PendingWebMessage>>()
            .Subject;
        return queue.Select(message => message.Json).ToList();
    }

    private static WebMessageHandler CreateHandler(ServiceProvider serviceProvider, OperatorFactory operatorFactory)
    {
        var eventStore = new InMemoryEventStore(NullLogger<InMemoryEventStore>.Instance);
        var eventBus = new InMemoryInspectionEventBus(NullLogger<InMemoryInspectionEventBus>.Instance, eventStore);

        return new WebMessageHandler(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            operatorFactory,
            eventBus,
            NullLogger<WebMessageHandler>.Instance,
            NullLoggerFactory.Instance);
    }

    private static ServiceProvider BuildServiceProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static OperatorDto CreateOperatorDto(
        OperatorFactory operatorFactory,
        OperatorType operatorType,
        string name,
        Guid operatorId)
    {
        var @operator = operatorFactory.CreateOperator(operatorType, name, 0, 0);
        typeof(Operator).GetProperty(nameof(Operator.Id))?.SetValue(@operator, operatorId);

        return new OperatorDto
        {
            Id = @operator.Id,
            Name = @operator.Name,
            Type = @operator.Type,
            X = 0,
            Y = 0,
            InputPorts = @operator.InputPorts.Select(port => new PortDto
            {
                Id = port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = @operator.OutputPorts.Select(port => new PortDto
            {
                Id = port.Id,
                Name = port.Name,
                Direction = port.Direction,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            Parameters = @operator.Parameters.Select(parameter => new ParameterDto
            {
                Id = parameter.Id,
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.Value,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList()
        };
    }
}
