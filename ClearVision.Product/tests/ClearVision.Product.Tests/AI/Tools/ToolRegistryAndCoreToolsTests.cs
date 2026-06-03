using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI.Tools;
using FluentAssertions;
using Xunit;

namespace ClearVision.Product.Tests.AI.Tools;

public sealed class ToolRegistryAndCoreToolsTests
{
    private sealed class StubOperatorFactory : IOperatorFactory
    {
        public List<OperatorMetadata> MetadataList { get; } = new()
        {
            new OperatorMetadata
            {
                Type = OperatorType.ImageAcquisition,
                DisplayName = "图像采集",
                Category = "Acquisition",
                Description = "Acquires an image from camera or file",
                Keywords = new[] { "camera", "image", "capture" },
                InputPorts = new(),
                OutputPorts = new() { new PortDefinition { Name = "Image", DisplayName = "图像", DataType = PortDataType.Image } },
                Parameters = new()
                {
                    new ParameterDefinition { Name = "SourceType", DisplayName = "数据源", DataType = "enum", DefaultValue = "Camera" }
                }
            },
            new OperatorMetadata
            {
                Type = OperatorType.ResultJudgment,
                DisplayName = "结果判定",
                Category = "Judgment",
                Description = "Decides OK or NG",
                Keywords = new[] { "ok", "ng", "judge" },
                InputPorts = new() { new PortDefinition { Name = "Value", DataType = PortDataType.Any } },
                OutputPorts = new(),
                Parameters = new()
            }
        };

        public Operator CreateOperator(OperatorType type, string name, double x, double y)
        {
            throw new NotImplementedException();
        }

        public OperatorMetadata? GetMetadata(OperatorType type)
        {
            return MetadataList.FirstOrDefault(m => m.Type == type);
        }

        public IEnumerable<OperatorMetadata> GetAllMetadata()
        {
            return MetadataList;
        }

        public IEnumerable<OperatorType> GetSupportedOperatorTypes()
        {
            return MetadataList.Select(m => m.Type);
        }

        public void RegisterOperator(OperatorMetadata metadata)
        {
            MetadataList.Add(metadata);
        }
    }

    [Fact(DisplayName = "Registry should register and execute tools successfully")]
    public async Task Registry_ListAndExecute_ShouldSucceed()
    {
        var factory = new StubOperatorFactory();
        var catalogTool = new OperatorCatalogTool(factory);
        var schemaTool = new OperatorSchemaTool(factory);

        var registry = new VisionAgentToolRegistry(new IVisionAgentTool[] { catalogTool, schemaTool });
        
        var tools = registry.ListTools();
        tools.Should().HaveCount(2);
        tools.Select(t => t.Name).Should().Contain(new[] { "list_operator_catalog", "get_operator_schema" });

        // Test catalog execution
        var context = new VisionAgentToolContext { SessionId = "test-session" };
        var arguments = JsonDocument.Parse("{}").RootElement;
        
        var result = await registry.ExecuteAsync("list_operator_catalog", context, arguments, CancellationToken.None);
        result.Success.Should().BeTrue();
        result.Summary.As<string>().Should().Contain("2 operators");

        // Test schema execution
        var schemaArguments = JsonDocument.Parse("{\"operatorType\": \"ImageAcquisition\"}").RootElement;
        var schemaResult = await registry.ExecuteAsync("get_operator_schema", context, schemaArguments, CancellationToken.None);
        schemaResult.Success.Should().BeTrue();
        schemaResult.Summary.As<string>().Should().Contain("ImageAcquisition");
    }

    [Fact(DisplayName = "get_operator_schema should fail with invalid or unknown operatorType")]
    public async Task SchemaTool_InvalidArgs_ShouldFail()
    {
        var factory = new StubOperatorFactory();
        var tool = new OperatorSchemaTool(factory);
        var context = new VisionAgentToolContext { SessionId = "test-session" };

        var invalidArgs = JsonDocument.Parse("{}").RootElement;
        var result1 = await tool.ExecuteAsync(context, invalidArgs, CancellationToken.None);
        result1.Success.Should().BeFalse();
        result1.ErrorMessage.Should().Contain("Missing or invalid");

        var unknownArgs = JsonDocument.Parse("{\"operatorType\": \"NonExistentOperator\"}").RootElement;
        var result2 = await tool.ExecuteAsync(context, unknownArgs, CancellationToken.None);
        result2.Success.Should().BeFalse();
        result2.ErrorMessage.Should().Contain("Unknown operatorType");
    }
}
