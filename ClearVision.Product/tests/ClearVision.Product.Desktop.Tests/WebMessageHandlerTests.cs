using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
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
            response.Error.Should().Contain("Legacy WebMessage");
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
        response.Error.Should().Contain("Legacy WebMessage");
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
        response.Error.Should().Contain("Legacy WebMessage");
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
        response.Error.Should().Contain("Legacy WebMessage");
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
            var enqueued = (bool)enqueueMethod!.Invoke(handler, [$"message-{index}"])!;
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
        var queue = queueField!.GetValue(handler).Should().BeOfType<ConcurrentQueue<string>>().Subject;
        queue.Should().HaveCount(512);
        queue.First().Should().Be("message-8");
        queue.Last().Should().Be("message-519");
    }

    [Fact]
    public async Task CancelGenerateFlow_ShouldCancelActiveGenerateToken()
    {
        var operatorFactory = new OperatorFactory();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;

        generationService.GenerateFlowAsync(
                Arg.Any<AiFlowGenerationRequest>(),
                Arg.Any<Action<string>>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>())
            .Returns(async callInfo =>
            {
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
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });

        var handler = CreateHandler(serviceProvider, operatorFactory);
        const string requestId = "req-active-1";
        const string sessionId = "session-active-1";
        var generateJson = $$"""
        { "payload": { "description": "生成流程", "sessionId": "{{sessionId}}", "requestId": "{{requestId}}" } }
        """;
        var cancelJson = $$"""
        { "payload": { "sessionId": "{{sessionId}}", "requestId": "{{requestId}}" } }
        """;

        var handleGenerateMethod = typeof(WebMessageHandler)
            .GetMethod("HandleGenerateFlowCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handleCancelMethod = typeof(WebMessageHandler)
            .GetMethod("HandleCancelGenerateFlowCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var generationTask = (Task)handleGenerateMethod.Invoke(handler, [generateJson])!;
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handleCancelMethod.Invoke(handler, [cancelJson]);
        await generationTask.WaitAsync(TimeSpan.FromSeconds(5));

        capturedToken.IsCancellationRequested.Should().BeTrue();

        var activeRequestsField = typeof(WebMessageHandler)
            .GetField("_activeGenerateFlowRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var activeRequests = activeRequestsField.GetValue(handler)!;
        var count = (int)activeRequests.GetType().GetProperty("Count")!.GetValue(activeRequests)!;
        count.Should().Be(0);
    }

    [Fact]
    public async Task HandleGenerateFlowCommand_MalformedBuildFromPlan_ShouldReturnControlledFailureWithoutGeneration()
    {
        var operatorFactory = new OperatorFactory();
        var generationService = Substitute.For<IAiFlowGenerationService>();
        var generationLogger = Substitute.For<Microsoft.Extensions.Logging.ILogger<GenerateFlowMessageHandler>>();
        await using var serviceProvider = BuildServiceProvider(services =>
        {
            services.AddScoped(_ => generationService);
            services.AddScoped(_ => generationLogger);
            services.AddScoped<GenerateFlowMessageHandler>();
        });
        var handler = CreateHandler(serviceProvider, operatorFactory);
        const string requestId = "req-malformed-build";
        const string sessionId = "session-malformed-build";
        var messageJson = $$"""
        {
          "payload": {
            "description": "start build",
            "sessionId": "{{sessionId}}",
            "requestId": "{{requestId}}",
            "buildFromPlan": {
              "planId": "plan-malformed",
              "planSnapshot": { "planId": "plan-malformed" },
              "confirmedAnswers": [
                { "field": { "not": "a-string" }, "value": "camera" }
              ]
            }
          }
        }
        """;
        var handleGenerateMethod = typeof(WebMessageHandler)
            .GetMethod("HandleGenerateFlowCommand", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await ((Task)handleGenerateMethod.Invoke(handler, [messageJson])!).WaitAsync(TimeSpan.FromSeconds(5));

        await generationService.DidNotReceiveWithAnyArgs().GenerateFlowAsync(
            Arg.Any<AiFlowGenerationRequest>(),
            Arg.Any<Action<string>>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.AiStreamChunk>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<ClearVision.Product.Contracts.Messages.GenerateFlowAttachmentReport>>());
        var queueField = typeof(WebMessageHandler).GetField(
            "_pendingWebMessages",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = queueField.GetValue(handler).Should().BeOfType<ConcurrentQueue<string>>().Subject;
        queue.Should().NotBeEmpty();
        var responseJson = queue.Last();
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
