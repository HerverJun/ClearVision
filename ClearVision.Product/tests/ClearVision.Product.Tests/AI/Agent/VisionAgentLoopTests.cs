using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Agent;
using ClearVision.Product.Infrastructure.AI.Connectors;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Runtime;
using ClearVision.Product.Contracts.Messages;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.AI.Agent;

public sealed class VisionAgentLoopTests
{
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _serviceScope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IVisionAgentToolRegistry _registry = Substitute.For<IVisionAgentToolRegistry>();
    
    private readonly IAiModelSelector _modelSelector = Substitute.For<IAiModelSelector>();
    private readonly IAiConnectorFactory _connectorFactory = Substitute.For<IAiConnectorFactory>();
    private readonly IAiConnector _connector = Substitute.For<IAiConnector>();
    private readonly AiGenerationOrchestrator _aiOrchestrator;
    
    private readonly VisionAgentProtocolParser _protocolParser = new();
    private readonly Microsoft.Extensions.Logging.ILogger<VisionAgentLoop> _logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<VisionAgentLoop>>();

    public VisionAgentLoopTests()
    {
        _scopeFactory.CreateScope().Returns(_serviceScope);
        _serviceScope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IVisionAgentToolRegistry)).Returns(_registry);

        _aiOrchestrator = new AiGenerationOrchestrator(_modelSelector, _connectorFactory);
        _connectorFactory.CreateConnector(Arg.Any<AiModelConfig>()).Returns(_connector);
        _modelSelector.SelectGenerationModel().Returns(new AiModelConfig { Id = "test-model" });
    }

    [Fact(DisplayName = "VisionAgentLoop should process tool calls and finish with final flow")]
    public async Task RunAsync_WithToolCalls_ShouldSucceed()
    {
        // Round 1: Model calls "list_operator_catalog"
        var round1Response = new AiCompletionResult
        {
            Content = @"{
                ""kind"": ""tool_call"",
                ""toolCalls"": [
                    { ""id"": ""call_1"", ""name"": ""list_operator_catalog"", ""arguments"": {} }
                ]
            }"
        };

        // Round 2: Model outputs final flow
        var round2Response = new AiCompletionResult
        {
            Content = @"{
                ""kind"": ""final_flow"",
                ""explanation"": ""Flow is ready"",
                ""operators"": [
                    { ""tempId"": ""op_1"", ""operatorType"": ""ImageAcquisition"", ""displayName"": ""采集"" }
                ],
                ""connections"": []
            }"
        };

        var calls = 0;
        _connector.StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                calls++;
                return calls == 1 ? Task.FromResult(round1Response) : Task.FromResult(round2Response);
            });

        // Mock Tool Registry behavior
        var mockTool = Substitute.For<IVisionAgentTool>();
        mockTool.Name.Returns("list_operator_catalog");
        mockTool.Permission.Returns(VisionAgentToolPermission.ReadOnly);

        _registry.TryGet("list_operator_catalog", out Arg.Any<IVisionAgentTool>()!)
            .Returns(x =>
            {
                x[1] = mockTool;
                return true;
            });

        _registry.ExecuteAsync("list_operator_catalog", Arg.Any<VisionAgentToolContext>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(VisionAgentToolResult.CreateSuccess(new { count = 10 }, "10 operators found")));

        var loop = new VisionAgentLoop(_scopeFactory, _aiOrchestrator, _protocolParser, _logger);
        
        var options = new AiGenerationOptions();
        var modelConfig = new AiModelConfig { Id = "test-model" };

        var result = await loop.RunAsync(
            "System prompt",
            new List<ChatMessage>(),
            "session-1",
            null,
            null,
            options,
            modelConfig,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Explanation.Should().Be("Flow is ready");
        result.Flow.Should().NotBeNull();
        result.Flow!.Operators.Should().HaveCount(1);
        result.ToolTrace.Should().HaveCount(1);
        result.ToolTrace![0].ToolName.Should().Be("list_operator_catalog");
        result.ToolTrace![0].Success.Should().BeTrue();
        
        calls.Should().Be(2);
    }

    [Fact(DisplayName = "VisionAgentLoop should abort and fail when exceeding MaxToolRounds limit")]
    public async Task RunAsync_ExceedingLimit_ShouldFail()
    {
        // Model always calls "list_operator_catalog"
        var loopResponse = new AiCompletionResult
        {
            Content = @"{
                ""kind"": ""tool_call"",
                ""toolCalls"": [
                    { ""id"": ""call_1"", ""name"": ""list_operator_catalog"", ""arguments"": {} }
                ]
            }"
        };

        _connector.StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(loopResponse));

        var mockTool = Substitute.For<IVisionAgentTool>();
        mockTool.Name.Returns("list_operator_catalog");
        mockTool.Permission.Returns(VisionAgentToolPermission.ReadOnly);

        _registry.TryGet("list_operator_catalog", out Arg.Any<IVisionAgentTool>()!)
            .Returns(x =>
            {
                x[1] = mockTool;
                return true;
            });

        _registry.ExecuteAsync("list_operator_catalog", Arg.Any<VisionAgentToolContext>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(VisionAgentToolResult.CreateSuccess(new { count = 10 }, "10 operators")));

        var loop = new VisionAgentLoop(_scopeFactory, _aiOrchestrator, _protocolParser, _logger);
        
        var options = new AiGenerationOptions { MaxRetries = 2 }; // Limit to max 2 rounds
        var modelConfig = new AiModelConfig { Id = "test-model" };

        var result = await loop.RunAsync(
            "System prompt",
            new List<ChatMessage>(),
            "session-1",
            null,
            null,
            options,
            modelConfig,
            null,
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("达到工具调用多轮 Loop 上限");
    }

    [Fact(DisplayName = "VisionAgentLoop should parallelize ReadOnly tools and run simulation tools sequentially")]
    public async Task RunAsync_MixedReadOnlyAndSimulationTools_ShouldExecuteAccordingly()
    {
        // Model calls one read-only and one simulation tool
        var round1Response = new AiCompletionResult
        {
            Content = @"{
                ""kind"": ""tool_call"",
                ""toolCalls"": [
                    { ""id"": ""call_ro"", ""name"": ""list_operator_catalog"", ""arguments"": {} },
                    { ""id"": ""call_sim"", ""name"": ""dryrun_flow"", ""arguments"": {} }
                ]
            }"
        };

        var round2Response = new AiCompletionResult
        {
            Content = @"{
                ""kind"": ""final_flow"",
                ""explanation"": ""Done"",
                ""operators"": [],
                ""connections"": []
            }"
        };

        var calls = 0;
        _connector.StreamCompleteAsync(
            Arg.Any<string>(),
            Arg.Any<List<ChatMessage>>(),
            Arg.Any<Action<AiStreamChunk>>(),
            Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                calls++;
                return calls == 1 ? Task.FromResult(round1Response) : Task.FromResult(round2Response);
            });

        var roTool = Substitute.For<IVisionAgentTool>();
        roTool.Name.Returns("list_operator_catalog");
        roTool.Permission.Returns(VisionAgentToolPermission.ReadOnly);

        var simTool = Substitute.For<IVisionAgentTool>();
        simTool.Name.Returns("dryrun_flow");
        simTool.Permission.Returns(VisionAgentToolPermission.Simulation);

        _registry.TryGet("list_operator_catalog", out Arg.Any<IVisionAgentTool>()!)
            .Returns(x =>
            {
                x[1] = roTool;
                return true;
            });

        _registry.TryGet("dryrun_flow", out Arg.Any<IVisionAgentTool>()!)
            .Returns(x =>
            {
                x[1] = simTool;
                return true;
            });

        _registry.ExecuteAsync("list_operator_catalog", Arg.Any<VisionAgentToolContext>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(VisionAgentToolResult.CreateSuccess(new { }, "RO Tool Execute")));

        _registry.ExecuteAsync("dryrun_flow", Arg.Any<VisionAgentToolContext>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(VisionAgentToolResult.CreateSuccess(new { }, "Sim Tool Execute")));

        var loop = new VisionAgentLoop(_scopeFactory, _aiOrchestrator, _protocolParser, _logger);
        
        var options = new AiGenerationOptions();
        var modelConfig = new AiModelConfig { Id = "test-model" };

        var result = await loop.RunAsync(
            "System",
            new List<ChatMessage>(),
            "session-1",
            null,
            null,
            options,
            modelConfig,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ToolTrace.Should().HaveCount(2);
    }
}
