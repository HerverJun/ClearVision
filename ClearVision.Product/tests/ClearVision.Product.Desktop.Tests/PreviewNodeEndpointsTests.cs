using System.Net.Http.Json;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;
using DetectionResultValue = ClearVision.Product.Core.ValueObjects.DetectionResult;

namespace ClearVision.Product.Desktop.Tests;

public class PreviewNodeEndpointsTests
{
    [Fact]
    public async Task PreviewNode_UsesBreakAtOperatorAndReturnsTargetOutput()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var outputBytes = new byte[] { 1, 2, 3, 4 };
        DebugOptions? capturedOptions = null;
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedFlow = callInfo.ArgAt<OperatorFlow>(0);
                    capturedOptions = callInfo.ArgAt<DebugOptions>(1);

                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = debugSessionId,
                        ExecutionTimeMs = 12,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = outputBytes,
                                ["Score"] = 0.95
                            }
                        },
                        DebugOperatorResults = new List<OperatorDebugResult>
                        {
                            new()
                            {
                                OperatorId = targetNodeId,
                                OperatorName = "Threshold",
                                IsSuccess = true,
                                ExecutionOrder = 0,
                                ExecutionTimeMs = 12
                            }
                        }
                    });
                });
        });

        var request = new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            DebugSessionId = debugSessionId,
            ImageFormat = ".bmp",
            Parameters = new Dictionary<string, object>
            {
                ["Threshold"] = 180
            },
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    targetNodeId,
                    "Threshold",
                    OperatorType.Thresholding,
                    parameters: new Dictionary<string, object> { ["Threshold"] = 128 }))
        };

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", request);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("targetNodeId").GetGuid().Should().Be(targetNodeId);
        document.RootElement.GetProperty("debugSessionId").GetGuid().Should().Be(debugSessionId);
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(outputBytes));
        document.RootElement.GetProperty("executedOperators").GetArrayLength().Should().Be(1);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.BreakAtOperatorId.Should().Be(targetNodeId);
        capturedOptions.DebugSessionId.Should().Be(debugSessionId);
        capturedOptions.ImageFormat.Should().Be(".bmp");
        capturedOptions.EnableIntermediateCache.Should().BeTrue();

        capturedFlow.Should().NotBeNull();
        var thresholdParameter = capturedFlow!.Operators
            .Single(op => op.Id == targetNodeId)
            .Parameters
            .Single(param => param.Name == "Threshold")
            .GetValue();

        ReadIntValue(thresholdParameter).Should().Be(180);
    }

    [Fact]
    public async Task PreviewNode_WithProjectVariables_ShouldPropagateSourceToTargetInPreviewClone()
    {
        var variableId = Guid.NewGuid();
        var unrelatedVariableId = Guid.NewGuid();
        var sourcePortId = Guid.NewGuid();
        var unrelatedPortId = Guid.NewGuid();
        var targetInputPortId = Guid.NewGuid();
        var targetParameterId = Guid.NewGuid();
        var source = CreateOperatorDto(
            Guid.NewGuid(),
            "Source",
            OperatorType.Thresholding,
            outputPorts:
            [
                new PortDto
                {
                    Id = sourcePortId,
                    Name = "Count",
                    DataType = PortDataType.Integer,
                    Direction = PortDirection.Output
                }
            ]);
        var unrelated = CreateOperatorDto(
            Guid.NewGuid(),
            "Unrelated",
            OperatorType.Thresholding,
            outputPorts:
            [
                new PortDto
                {
                    Id = unrelatedPortId,
                    Name = "Count",
                    DataType = PortDataType.Integer,
                    Direction = PortDirection.Output
                }
            ]);
        var target = CreateOperatorDto(
            Guid.NewGuid(),
            "Target",
            OperatorType.ResultJudgment,
            inputPorts:
            [
                new PortDto
                {
                    Id = targetInputPortId,
                    Name = "Image",
                    DataType = PortDataType.Image,
                    Direction = PortDirection.Input
                }
            ],
            parameters: new Dictionary<string, object> { ["ExpectedCount"] = 0 });
        target.Parameters.Single().Id = targetParameterId;

        var schema = CreatePreviewVariableSchema(variableId, 0L);
        schema.Variables.Add(new ProjectGlobalVariableDefinition
        {
            Id = unrelatedVariableId,
            Name = "stats.unrelated",
            DisplayName = "Unrelated",
            ValueType = ProjectGlobalVariableValueType.Int64,
            InitialValue = JsonSerializer.SerializeToElement(0L),
            ManualWriteAllowed = true
        });
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = source.Id,
            OutputPortId = sourcePortId,
            OperatorName = source.Name,
            OutputPortName = "Count"
        });
        schema.SourceBindings.Add(new ProjectGlobalVariableSourceBinding
        {
            Id = Guid.NewGuid(),
            VariableId = unrelatedVariableId,
            OperatorId = unrelated.Id,
            OutputPortId = unrelatedPortId,
            OperatorName = unrelated.Name,
            OutputPortName = "Count"
        });
        schema.TargetBindings.Add(new ProjectGlobalVariableTargetBinding
        {
            Id = Guid.NewGuid(),
            VariableId = variableId,
            OperatorId = target.Id,
            ParameterId = targetParameterId,
            OperatorName = target.Name,
            ParameterName = "ExpectedCount"
        });
        var project = new Project("preview-project");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 2L, ProjectVariableUpdatedBy.StudioManual);

        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.Thresholding);
        sourceExecutor.ExecuteAsync(Arg.Any<Operator>(), Arg.Any<Dictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Count"] = 7L
            })));
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        var targetExecutor = Substitute.For<IOperatorExecutor>();
        targetExecutor.OperatorType.Returns(OperatorType.ResultJudgment);
        targetExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Is<Dictionary<string, object>>(inputs => Convert.ToInt64(inputs["ExpectedCount"]) == 7L),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Seen"] = 7L
            })));
        targetExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(ValidationResult.Valid());

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [sourceExecutor, targetExecutor]);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = target.Id,
            FlowData = CreateUpdateFlowRequest(
                source,
                unrelated,
                target,
                CreateConnection(source.Id, sourcePortId, target.Id, targetInputPortId))
        });

        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue(payload);
        document.RootElement.GetProperty("outputData").GetProperty("Seen").GetInt64().Should().Be(7L);
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(2L);
        formal.Version.Should().Be(1);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WithVariableIncrement_ShouldUseCurrentFormalSnapshotAndNotCommit()
    {
        var variableId = Guid.NewGuid();
        var increment = CreateOperatorDto(
            Guid.NewGuid(),
            "Increment",
            OperatorType.VariableIncrement,
            parameters: new Dictionary<string, object>
            {
                ["Scope"] = "Project",
                ["VariableId"] = variableId.ToString(),
                ["VariableName"] = "stats.count",
                ["Delta"] = 5
            });

        var schema = CreatePreviewVariableSchema(variableId, 1L);
        var project = new Project("preview-increment");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var accessor = new ProjectVariableExecutionContextAccessor();

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [
                new VariableIncrementOperator(
                    NullLogger<VariableIncrementOperator>.Instance,
                    new VariableContext(),
                    accessor)
            ],
            accessor);

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = increment.Id,
            FlowData = CreateUpdateFlowRequest(increment)
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputData").GetProperty("NewValue").GetInt64().Should().Be(9L);
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(4L);
        formal.Version.Should().Be(1);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WithVariableReadAndSameDebugSession_ShouldReturnFreshProjectVariableValue()
    {
        var variableId = Guid.NewGuid();
        var debugSessionId = Guid.NewGuid();
        var read = CreateOperatorDto(
            Guid.NewGuid(),
            "Read",
            OperatorType.VariableRead,
            parameters: new Dictionary<string, object>
            {
                ["Scope"] = "Project",
                ["VariableId"] = variableId.ToString(),
                ["VariableName"] = "stats.count",
                ["DefaultValue"] = "0",
                ["DataType"] = "Int"
            });

        var schema = CreatePreviewVariableSchema(variableId, 1L);
        var project = new Project("preview-read-cache");
        project.UpdateGlobalVariables(schema);
        var registry = new ProjectVariableSessionRegistry();
        var formalSession = registry.GetOrCreate(project.Id, schema);
        formalSession.SetValue(variableId, 4L, ProjectVariableUpdatedBy.StudioManual);
        var accessor = new ProjectVariableExecutionContextAccessor();
        var readExecutor = new CountingOperatorExecutor(new VariableReadOperator(
            NullLogger<VariableReadOperator>.Instance,
            new VariableContext(),
            accessor));

        await using var host = await PreviewNodeTestHost.CreateWithRealFlowExecutionAsync(
            project,
            registry,
            [readExecutor],
            accessor);

        var first = await PostPreviewReadAsync(host, project.Id, read, debugSessionId);
        formalSession.SetValue(variableId, 10L, ProjectVariableUpdatedBy.StudioManual);
        var second = await PostPreviewReadAsync(host, project.Id, read, debugSessionId);

        first.GetProperty("success").GetBoolean().Should().BeTrue(first.ToString());
        second.GetProperty("success").GetBoolean().Should().BeTrue(second.ToString());
        first.GetProperty("outputData").GetProperty("Value").GetInt64().Should().Be(4L);
        second.GetProperty("outputData").GetProperty("Value").GetInt64().Should().Be(10L);
        readExecutor.ExecuteCount.Should().Be(2, "Project-scope VariableRead must not reuse PreviewNode debug cache");
        formalSession.TryGetSnapshot(variableId, out var formal).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(formal.Value).Should().Be(10L);
        formal.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.StudioManual);
    }

    [Fact]
    public async Task PreviewNode_WhenProjectHasNoGlobalVariables_ShouldUseExistingExecutionPath()
    {
        var project = new Project("no-global-variables");
        var targetNodeId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(
            flowExecution =>
            {
                flowExecution.ExecuteFlowDebugAsync(
                        Arg.Any<OperatorFlow>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new() { ["Value"] = 3L }
                        }
                    }));
            },
            projectRepository => projectRepository.GetByIdAsync(project.Id).Returns(project));

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = project.Id,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(CreateOperatorDto(targetNodeId, "Target", OperatorType.ResultJudgment))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PreviewNode_ShouldPropagateRequestCancellationToFlowExecution()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var enteredExecution = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedToken = CancellationToken.None;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedToken = callInfo.ArgAt<CancellationToken>(3);
                    enteredExecution.TrySetResult(null);
                    return CompleteWhenCanceledAsync(targetNodeId, capturedToken, cancellationObserved);
                });
        });

        using var cts = new CancellationTokenSource();
        var requestTask = host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        }, cts.Token);

        await enteredExecution.Task.WaitAsync(TimeSpan.FromSeconds(5));
        capturedToken.CanBeCanceled.Should().BeTrue();

        cts.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            using var _ = await requestTask;
        }
        catch (OperationCanceledException)
        {
            // The client side is expected to observe cancellation once the request is aborted.
        }
    }

    [Fact]
    public async Task PreviewNode_ShouldPreferStoredFlow_WhenRequestOmitsInlineFlowData()
    {
        var projectId = Guid.NewGuid();
        var storedNodeId = Guid.NewGuid();
        var databaseNodeId = Guid.NewGuid();
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(
            configureFlowExecution: flowExecution =>
            {
                flowExecution.ExecuteFlowDebugAsync(
                        Arg.Any<OperatorFlow>(),
                        Arg.Any<DebugOptions>(),
                        Arg.Any<Dictionary<string, object>?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        capturedFlow = callInfo.ArgAt<OperatorFlow>(0);
                        return Task.FromResult(new FlowDebugExecutionResult
                        {
                            IsSuccess = true,
                            DebugSessionId = Guid.NewGuid(),
                            ExecutionTimeMs = 6,
                            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                            {
                                [storedNodeId] = new()
                                {
                                    ["Image"] = new byte[] { 1, 2, 3 }
                                }
                            }
                        });
                    });
            },
            configureProjectRepository: projectRepository =>
            {
                var project = new Project("preview-stored-flow");
                var databaseFlow = new OperatorFlow("DatabaseFlow");
                databaseFlow.AddOperator(new Operator(databaseNodeId, "db-node", OperatorType.ResultOutput, 0, 0));
                project.UpdateFlow(databaseFlow);
                projectRepository.GetWithFlowAsync(projectId).Returns(project);
            },
            configureFlowStorage: flowStorage =>
            {
                flowStorage.LoadFlowJsonAsync(projectId).Returns(CreateStoredFlowJson(
                    CreateOperatorDto(storedNodeId, "stored-node", OperatorType.ResultOutput)));
            });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = storedNodeId
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedFlow.Should().NotBeNull();
        capturedFlow!.Operators.Should().ContainSingle(operatorEntity => operatorEntity.Id == storedNodeId);
        capturedFlow.Operators.Should().NotContain(operatorEntity => operatorEntity.Id == databaseNodeId);
    }

    [Fact]
    public async Task PreviewNode_ReturnsMinimalFeedbackMetrics()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var previewImage = CreateBinaryPreviewImageBytes();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 24,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = previewImage,
                            ["Defects"] = new List<Dictionary<string, object>>
                            {
                                new() { ["Area"] = 4.0 },
                                new() { ["Area"] = 6.0 }
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = document.RootElement.GetProperty("metrics");
        metrics.GetProperty("blobCount").GetInt32().Should().Be(2);
        metrics.GetProperty("binaryRatio").GetDouble().Should().BeApproximately(0.5d, 0.001d);
        metrics.GetProperty("areaStats").GetProperty("min").GetDouble().Should().Be(4d);
        metrics.GetProperty("areaStats").GetProperty("max").GetDouble().Should().Be(6d);
        metrics.GetProperty("areaStats").GetProperty("mean").GetDouble().Should().Be(5d);
    }

    [Fact]
    public async Task PreviewNode_ReturnsDetectionFeedbackMetrics()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 18,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["DetectionList"] = new DetectionList(new[]
                            {
                                new DetectionResultValue("Wire_Brown", 0.98f, 10f, 10f, 8f, 8f),
                                new DetectionResultValue("Wire_Black", 0.66f, 30f, 10f, 8f, 8f)
                            }),
                            ["ObjectCount"] = 2,
                            ["ExpectedLabels"] = new[] { "Wire_Brown", "Wire_Black", "Wire_Blue" },
                            ["RequiredMinConfidence"] = 0.8
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Judge", OperatorType.DetectionSequenceJudge))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var metrics = document.RootElement.GetProperty("metrics");

        metrics.GetProperty("detectionCount").GetInt32().Should().Be(2);
        metrics.GetProperty("objectCount").GetInt32().Should().Be(2);
        metrics.GetProperty("sortedLabels")[0].GetString().Should().Be("Wire_Brown");
        metrics.GetProperty("sortedLabels")[1].GetString().Should().Be("Wire_Black");
        metrics.GetProperty("missingLabels")[0].GetString().Should().Be("Wire_Blue");
        metrics.GetProperty("diagnostics").EnumerateArray().Select(item => item.GetString()).Should()
            .Contain(new[]
            {
                PreviewDiagnosticTags.MissingExpectedClass,
                PreviewDiagnosticTags.DetectionCountMismatch,
                PreviewDiagnosticTags.LowDetectionConfidence,
                PreviewDiagnosticTags.OrderMismatch
            });

        var perClassCount = metrics.GetProperty("perClassCount");
        perClassCount.GetProperty("Wire_Brown").GetInt32().Should().Be(1);
        perClassCount.GetProperty("Wire_Black").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PreviewNode_ShouldNotInjectExternalImage_WhenTargetPathContainsImageAcquisition()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = Guid.NewGuid(),
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(acquisitionId, "图像采集", OperatorType.ImageAcquisition, outputPorts: [acquisitionOutput]),
                CreateOperatorDto(targetNodeId, "图像缩放", OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().BeNull();
    }

    [Fact]
    public async Task PreviewNode_ShouldNotInjectExternalImage_WhenTargetIsImageAcquisition()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = Guid.NewGuid(),
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = Convert.ToBase64String(new byte[] { 9, 9, 9 }),
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "图像采集", OperatorType.ImageAcquisition, outputPorts: [targetOutput]))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().BeNull();
    }

    [Fact]
    public async Task PreviewNode_ShouldInjectExternalImage_WhenNoImageAcquisitionExistsUpstream()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        Dictionary<string, object>? capturedInput = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedInput = callInfo.ArgAt<Dictionary<string, object>?>(2);
                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = Guid.NewGuid(),
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 }
                            }
                        }
                    });
                });
        });

        var externalImage = Convert.ToBase64String(new byte[] { 7, 8, 9 });
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            InputImageBase64 = externalImage,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "图像缩放", OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedInput.Should().NotBeNull();
        var input = capturedInput!;
        input.Should().ContainKey("Image");
        ((byte[])input["Image"]).Should().Equal(new byte[] { 7, 8, 9 });
    }

    [Fact]
    public async Task PreviewNode_ShouldFallbackInputImageToUpstreamImageAcquisitionOutput()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var targetOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var acquisitionImage = CreateBinaryPreviewImageBytes();
        var targetImage = new byte[] { 4, 5, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 22,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = targetImage
                        }
                    },
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = acquisitionId,
                            OperatorName = "Acquire",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = acquisitionImage
                            }
                        },
                        new()
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Resize",
                            IsSuccess = true,
                            ExecutionOrder = 1,
                            InputSnapshot = new Dictionary<string, object>(),
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = targetImage
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = "demo.png" }),
                CreateOperatorDto(
                    targetNodeId,
                    "Resize",
                    OperatorType.ImageResize,
                    inputPorts: [targetInput],
                    outputPorts: [targetOutput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("inputImageBase64").GetString().Should().Be(Convert.ToBase64String(acquisitionImage));
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(targetImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldExtractImageFromImageWrapperIntermediateOutput()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        using var previewMat = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));
        previewMat.Set(0, 0, 255);
        var expectedBytes = previewMat.ToBytes(".png");

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 10,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = new ImageWrapper(previewMat.Clone())
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(expectedBytes));
    }

    [Fact]
    public async Task PreviewNode_ShouldHideOriginalImageFromOutputData_AndKeepPreviewBoundToImage()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var previewImage = new byte[] { 1, 2, 3, 4 };
        var originalImage = new byte[] { 9, 8, 7, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 10,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = previewImage,
                            ["OriginalImage"] = originalImage,
                            ["Count"] = 1
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Resize", OperatorType.ImageResize))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(previewImage));

        var outputData = document.RootElement.GetProperty("outputData");
        outputData.TryGetProperty("OriginalImage", out _).Should().BeFalse();
        outputData.TryGetProperty("Image", out _).Should().BeFalse();
        outputData.GetProperty("Count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task PreviewNode_ShouldIgnoreInvalidDownstreamConnectionsOutsideTargetSubgraph()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var integerNodeId = Guid.NewGuid();
        var invalidTargetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var integerOutput = CreatePort("Count", PortDataType.Integer, PortDirection.Output);
        var detectionInput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Input, isRequired: true);
        var previewImage = new byte[] { 4, 3, 2, 1 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 8,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [acquisitionId] = new()
                        {
                            ["Image"] = previewImage
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = acquisitionId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = "demo.png" }),
                CreateOperatorDto(
                    integerNodeId,
                    "Counter",
                    OperatorType.VariableIncrement,
                    outputPorts: [integerOutput]),
                CreateOperatorDto(
                    invalidTargetNodeId,
                    "BoxFilter",
                    OperatorType.BoxFilter,
                    inputPorts: [detectionInput]),
                CreateConnection(integerNodeId, integerOutput.Id, invalidTargetNodeId, detectionInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputImageBase64").GetString().Should().Be(Convert.ToBase64String(previewImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldRepairIncompatiblePreferredPortIdsWithinTargetSubgraph()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var deepLearningId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var deepLearningImageInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var deepLearningImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var deepLearningObjectCountOutput = CreatePort("ObjectCount", PortDataType.Integer, PortDirection.Output);
        var deepLearningObjectsOutput = CreatePort("Objects", PortDataType.DetectionList, PortDirection.Output);
        var roiDetectionsInput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Input, isRequired: true);
        var roiImageInput = CreatePort("Image", PortDataType.Image, PortDirection.Input);
        var roiImageOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var roiDetectionsOutput = CreatePort("Detections", PortDataType.DetectionList, PortDirection.Output);
        OperatorFlow? capturedFlow = null;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    capturedFlow = callInfo.ArgAt<OperatorFlow>(0);
                    return Task.FromResult(new FlowDebugExecutionResult
                    {
                        IsSuccess = true,
                        DebugSessionId = Guid.NewGuid(),
                        ExecutionTimeMs = 9,
                        IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                        {
                            [targetNodeId] = new()
                            {
                                ["Image"] = new byte[] { 1, 2, 3 },
                                ["Count"] = 1
                            }
                        }
                    });
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionImageOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = "demo.png" }),
                CreateOperatorDto(
                    deepLearningId,
                    "DeepLearning",
                    OperatorType.DeepLearning,
                    inputPorts: [deepLearningImageInput],
                    outputPorts: [deepLearningImageOutput, deepLearningObjectCountOutput, deepLearningObjectsOutput]),
                CreateOperatorDto(
                    targetNodeId,
                    "BoxFilter",
                    OperatorType.BoxFilter,
                    inputPorts: [roiDetectionsInput, roiImageInput],
                    outputPorts: [roiDetectionsOutput, roiImageOutput]),
                CreateConnection(acquisitionId, acquisitionImageOutput.Id, deepLearningId, deepLearningImageInput.Id),
                CreateConnection(acquisitionId, acquisitionImageOutput.Id, targetNodeId, roiImageInput.Id),
                CreateConnection(deepLearningId, deepLearningObjectCountOutput.Id, targetNodeId, roiDetectionsInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        capturedFlow.Should().NotBeNull();

        var deepLearning = capturedFlow!.Operators.Single(op => op.Id == deepLearningId);
        var repairedConnection = capturedFlow.Connections.Single(conn =>
            conn.SourceOperatorId == deepLearningId &&
            conn.TargetOperatorId == targetNodeId);
        var repairedSourcePort = deepLearning.OutputPorts.Single(port => port.Id == repairedConnection.SourcePortId);

        repairedSourcePort.Name.Should().Be("Objects");
        repairedSourcePort.DataType.Should().Be(PortDataType.DetectionList);
    }

    [Fact]
    public async Task PreviewNode_ShouldPreferFailedOperatorInputSnapshotOverAcquisitionFallback()
    {
        var projectId = Guid.NewGuid();
        var acquisitionId = Guid.NewGuid();
        var resizeId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var acquisitionOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var resizeInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var resizeOutput = CreatePort("Image", PortDataType.Image, PortDirection.Output);
        var targetInput = CreatePort("Image", PortDataType.Image, PortDirection.Input, isRequired: true);
        var acquisitionImage = new byte[] { 1, 2, 3, 4 };
        var transformedImage = new byte[] { 9, 8, 7, 6 };

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 11,
                    ErrorMessage = "Resize failed",
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = acquisitionId,
                            OperatorName = "Acquire",
                            IsSuccess = true,
                            ExecutionOrder = 0,
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = acquisitionImage
                            }
                        },
                        new()
                        {
                            OperatorId = resizeId,
                            OperatorName = "Resize",
                            IsSuccess = false,
                            ErrorMessage = "Resize failed",
                            ExecutionOrder = 1,
                            InputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = transformedImage
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(
                    acquisitionId,
                    "Acquire",
                    OperatorType.ImageAcquisition,
                    outputPorts: [acquisitionOutput],
                    parameters: new Dictionary<string, object> { ["SourceType"] = "File", ["FilePath"] = "demo.png" }),
                CreateOperatorDto(
                    resizeId,
                    "Resize",
                    OperatorType.ImageResize,
                    inputPorts: [resizeInput],
                    outputPorts: [resizeOutput]),
                CreateOperatorDto(
                    targetNodeId,
                    "Threshold",
                    OperatorType.Thresholding,
                    inputPorts: [targetInput]),
                CreateConnection(acquisitionId, acquisitionOutput.Id, resizeId, resizeInput.Id),
                CreateConnection(resizeId, resizeOutput.Id, targetNodeId, targetInput.Id))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("inputImageBase64").GetString().Should().Be(Convert.ToBase64String(transformedImage));
    }

    [Fact]
    public async Task PreviewNode_ShouldReturnGatewayTimeout_WhenPreviewExecutionExceedsTimeout()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var cancellationObserved = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    var token = callInfo.ArgAt<CancellationToken>(3);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                    }
                    catch (OperationCanceledException)
                    {
                        cancellationObserved.TrySetResult(null);
                        throw;
                    }

                    return new FlowDebugExecutionResult { IsSuccess = true };
                });
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            TimeoutMs = 1,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.GatewayTimeout);
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PreviewNode_ShouldOmitLargeOutputImagePayload_AndKeepStructuredSummary()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var largeImage = new byte[(8 * 1024 * 1024) + 1];
        largeImage[0] = 1;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = true,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 5,
                    IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
                    {
                        [targetNodeId] = new()
                        {
                            ["Image"] = largeImage,
                            ["Score"] = 0.98
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("outputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.98d, 0.001d);
        outputData.GetProperty("_previewWarning").GetString().Should().Contain("过大");
    }

    [Fact]
    public async Task PreviewNode_ShouldOmitLargeFailureInputImagePayload_AndKeepStructuredSummary()
    {
        var projectId = Guid.NewGuid();
        var targetNodeId = Guid.NewGuid();
        var largeInputImage = new byte[(8 * 1024 * 1024) + 1];
        largeInputImage[0] = 1;

        await using var host = await PreviewNodeTestHost.CreateAsync(flowExecution =>
        {
            flowExecution.ExecuteFlowDebugAsync(
                    Arg.Any<OperatorFlow>(),
                    Arg.Any<DebugOptions>(),
                    Arg.Any<Dictionary<string, object>?>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new FlowDebugExecutionResult
                {
                    IsSuccess = false,
                    DebugSessionId = Guid.NewGuid(),
                    ExecutionTimeMs = 7,
                    ErrorMessage = "Target failed",
                    DebugOperatorResults = new List<OperatorDebugResult>
                    {
                        new()
                        {
                            OperatorId = targetNodeId,
                            OperatorName = "Threshold",
                            IsSuccess = false,
                            ExecutionOrder = 0,
                            ErrorMessage = "Target failed",
                            InputSnapshot = new Dictionary<string, object>
                            {
                                ["Image"] = largeInputImage
                            },
                            OutputSnapshot = new Dictionary<string, object>
                            {
                                ["Score"] = 0.42
                            }
                        }
                    }
                }));
        });

        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = targetNodeId,
            FlowData = CreateUpdateFlowRequest(
                CreateOperatorDto(targetNodeId, "Threshold", OperatorType.Thresholding))
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("inputImageBase64").ValueKind.Should().Be(JsonValueKind.Null);
        var outputData = document.RootElement.GetProperty("outputData");
        outputData.GetProperty("Score").GetDouble().Should().BeApproximately(0.42d, 0.001d);
        outputData.GetProperty("_previewWarning").GetString().Should().Contain("过大");
    }

    private static int ReadIntValue(object? value)
    {
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)doubleValue,
            JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Number => jsonElement.GetInt32(),
            _ => throw new InvalidOperationException($"Unsupported parameter value: {value?.GetType().FullName ?? "<null>"}")
        };
    }

    private static byte[] CreateBinaryPreviewImageBytes()
    {
        using var image = new Mat(2, 2, MatType.CV_8UC1, Scalar.All(0));
        image.Set(0, 0, 255);
        image.Set(1, 1, 255);
        return image.ToBytes(".png");
    }

    private static async Task<FlowDebugExecutionResult> CompleteWhenCanceledAsync(
        Guid targetNodeId,
        CancellationToken cancellationToken,
        TaskCompletionSource<object?> cancellationObserved)
    {
        using var registration = cancellationToken.Register(() => cancellationObserved.TrySetResult(null));
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        return new FlowDebugExecutionResult
        {
            IsSuccess = false,
            ErrorMessage = "Canceled",
            IntermediateResults = new Dictionary<Guid, Dictionary<string, object>>
            {
                [targetNodeId] = new()
            }
        };
    }

    private static UpdateFlowRequest CreateUpdateFlowRequest(params object[] items)
    {
        var flow = new UpdateFlowRequest();
        foreach (var item in items)
        {
            switch (item)
            {
                case OperatorDto operatorDto:
                    flow.Operators.Add(operatorDto);
                    break;
                case OperatorConnectionDto connectionDto:
                    flow.Connections.Add(connectionDto);
                    break;
            }
        }

        return flow;
    }

    private static ProjectGlobalVariableSchema CreatePreviewVariableSchema(Guid variableId, long initialValue)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = true
                }
            ]
        };
    }

    private static async Task<JsonElement> PostPreviewReadAsync(
        PreviewNodeTestHost host,
        Guid projectId,
        OperatorDto read,
        Guid debugSessionId)
    {
        using var response = await host.Client.PostAsJsonAsync("/api/flows/preview-node", new PreviewNodeRequest
        {
            ProjectId = projectId,
            TargetNodeId = read.Id,
            DebugSessionId = debugSessionId,
            FlowData = CreateUpdateFlowRequest(read)
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, payload);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static OperatorDto CreateOperatorDto(
        Guid id,
        string name,
        OperatorType type,
        List<PortDto>? inputPorts = null,
        List<PortDto>? outputPorts = null,
        Dictionary<string, object>? parameters = null)
    {
        return new OperatorDto
        {
            Id = id,
            Name = name,
            Type = type,
            X = 0,
            Y = 0,
            IsEnabled = true,
            InputPorts = inputPorts ?? new List<PortDto>(),
            OutputPorts = outputPorts ?? new List<PortDto>(),
            Parameters = parameters?.Select(kvp => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = kvp.Key,
                DisplayName = kvp.Key,
                DataType = kvp.Value switch
                {
                    int => "int",
                    long => "int",
                    float => "double",
                    double => "double",
                    bool => "bool",
                    _ => "string"
                },
                Value = kvp.Value,
                DefaultValue = kvp.Value,
                IsRequired = false
            }).ToList() ?? new List<ParameterDto>()
        };
    }

    private static PortDto CreatePort(
        string name,
        PortDataType dataType,
        PortDirection direction,
        bool isRequired = false)
    {
        return new PortDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            DataType = dataType,
            Direction = direction,
            IsRequired = isRequired
        };
    }

    private static OperatorConnectionDto CreateConnection(
        Guid sourceOperatorId,
        Guid sourcePortId,
        Guid targetOperatorId,
        Guid targetPortId)
    {
        return new OperatorConnectionDto
        {
            Id = Guid.NewGuid(),
            SourceOperatorId = sourceOperatorId,
            SourcePortId = sourcePortId,
            TargetOperatorId = targetOperatorId,
            TargetPortId = targetPortId
        };
    }

    private static string CreateStoredFlowJson(params OperatorDto[] operators)
    {
        var flowDto = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "StoredPreviewFlow",
            Operators = operators.ToList(),
            Connections = new List<OperatorConnectionDto>()
        };

        return JsonSerializer.Serialize(flowDto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
    }

    private sealed class PreviewNodeTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private PreviewNodeTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<PreviewNodeTestHost> CreateAsync(
            Action<IFlowExecutionService> configureFlowExecution,
            Action<IProjectRepository>? configureProjectRepository = null,
            Action<IProjectFlowStorage>? configureFlowStorage = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var flowExecution = Substitute.For<IFlowExecutionService>();
            var projectRepository = Substitute.For<IProjectRepository>();
            var flowStorage = Substitute.For<IProjectFlowStorage>();
            var registry = new ProjectVariableSessionRegistry();
            configureFlowExecution(flowExecution);
            configureProjectRepository?.Invoke(projectRepository);
            configureFlowStorage?.Invoke(flowStorage);

            builder.Services.AddSingleton(flowExecution);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton(registry);

            var app = builder.Build();
            app.MapPreviewNodeEndpoints();
            await app.StartAsync();

            return new PreviewNodeTestHost(app, app.GetTestClient());
        }

        public static async Task<PreviewNodeTestHost> CreateWithRealFlowExecutionAsync(
            Project project,
            ProjectVariableSessionRegistry registry,
            IEnumerable<IOperatorExecutor> executors,
            ProjectVariableExecutionContextAccessor? accessor = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            accessor ??= new ProjectVariableExecutionContextAccessor();
            var flowExecution = new FlowExecutionService(
                executors,
                NullLogger<FlowExecutionService>.Instance,
                new VariableContext(),
                accessor);
            var projectRepository = Substitute.For<IProjectRepository>();
            projectRepository.GetByIdAsync(project.Id).Returns(project);
            var flowStorage = Substitute.For<IProjectFlowStorage>();

            builder.Services.AddSingleton<IFlowExecutionService>(flowExecution);
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton(flowStorage);
            builder.Services.AddSingleton(registry);

            var app = builder.Build();
            app.MapPreviewNodeEndpoints();
            await app.StartAsync();

            return new PreviewNodeTestHost(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class CountingOperatorExecutor : IOperatorExecutor
    {
        private readonly IOperatorExecutor _inner;

        public CountingOperatorExecutor(IOperatorExecutor inner)
        {
            _inner = inner;
        }

        public OperatorType OperatorType => _inner.OperatorType;

        public int ExecuteCount { get; private set; }

        public Task<OperatorExecutionOutput> ExecuteAsync(
            Operator @operator,
            Dictionary<string, object>? inputs = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return _inner.ExecuteAsync(@operator, inputs, cancellationToken);
        }

        public ValidationResult ValidateParameters(Operator @operator)
        {
            return _inner.ValidateParameters(@operator);
        }
    }
}
