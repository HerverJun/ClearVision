using System.Net;
using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.AI.Agent;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Tests.AI.VisionAgentSemanticExtraction;

public sealed class VisionAgentSemanticExtractorServiceTests
{
    [Fact(DisplayName = "Semantic extractor options should bind through AddAiFlowGeneration")]
    public void AddAiFlowGeneration_ShouldBindSemanticExtractorOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:VisionAgent:SemanticExtractor:Enabled"] = "false",
                ["AI:VisionAgent:SemanticExtractor:TimeoutSeconds"] = "7",
                ["AI:VisionAgent:SemanticExtractor:MaxContextChars"] = "3000",
                ["AI:VisionAgent:SemanticExtractor:ModelRole"] = "vision"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddAiFlowGeneration(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<VisionAgentSemanticExtractorOptions>>()
            .Value
            .Normalize();
        options.Enabled.Should().BeFalse();
        options.TimeoutSeconds.Should().Be(7);
        options.MaxContextChars.Should().Be(3000);
        options.ModelRole.Should().Be(AiModelConfig.RoleVision);
    }

    [Fact(DisplayName = "Semantic extractor parses markdown-wrapped JSON and normalizes attribute classification")]
    public async Task ExtractAsync_ShouldParseWrappedJson()
    {
        var service = CreateService(_ => $"""
            ```json
            {JsonSerializer.Serialize(new
            {
                isVisionRequest = true,
                intent = "new_flow",
                taskType = "attribute_classification",
                confidence = 0.92,
                taskTypeConfidence = 0.88,
                inspectionObject = "草莓",
                targetAttribute = "成熟度/熟透",
                imageSource = "相机",
                okCondition = "熟透则 OK",
                ngCondition = "否则 NG",
                suggestedRoute = "属性分类 / OK-NG 判别路线",
                canPlanCandidate = true,
                canBuildCandidate = true,
                objectSignals = new[] { "草莓" },
                taskSignals = new[] { "成熟度" },
                missingFields = Array.Empty<string>(),
                clarificationQuestions = Array.Empty<string>()
            })}
            ```
            """);

        var result = await service.ExtractAsync(StrawberryRequest(), CancellationToken.None);

        result.Source.Should().Be(VisionAgentSemanticSources.Model);
        result.FailureCode.Should().BeEmpty();
        result.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.InspectionObject.Should().Contain("草莓");
        result.TargetAttribute.Should().Contain("熟透");
        result.OkCondition.Should().Contain("OK");
        result.NgCondition.Should().Contain("NG");
        result.ImageSource.Should().Be("相机");
    }

    [Fact(DisplayName = "Semantic extractor repairs unknown model task type to unknown")]
    public async Task ExtractAsync_ShouldRepairUnknownTaskType()
    {
        var service = CreateService(_ => JsonSerializer.Serialize(new
        {
            isVisionRequest = true,
            intent = "new_flow",
            taskType = "quantum_magic",
            confidence = 0.7,
            taskTypeConfidence = 0.9,
            inspectionObject = "产品",
            objectSignals = Array.Empty<string>(),
            taskSignals = Array.Empty<string>(),
            missingFields = Array.Empty<string>(),
            clarificationQuestions = Array.Empty<string>()
        }));

        var result = await service.ExtractAsync(new VisionAgentSemanticExtractionRequest
        {
            Description = "检测产品。"
        }, CancellationToken.None);

        result.Source.Should().Be(VisionAgentSemanticSources.Model);
        result.TaskType.Should().Be(AiVisionTaskTypes.Unknown);
        result.TaskTypeConfidence.Should().Be(0.9);
    }

    [Fact(DisplayName = "Semantic extractor empty response returns rule fallback with semantic_model_empty")]
    public async Task ExtractAsync_ShouldFallbackOnEmptyResponse()
    {
        var service = CreateService(_ => "   ");

        var result = await service.ExtractAsync(StrawberryRequest(), CancellationToken.None);

        result.Source.Should().Be(VisionAgentSemanticSources.RuleFallback);
        result.FailureCode.Should().Be(VisionAgentSemanticFailureCodes.ModelEmpty);
        result.SanitizedErrorMessage.Should().Contain("rule fallback is active");
    }

    [Fact(DisplayName = "Semantic extractor invalid JSON returns parse failure fallback")]
    public async Task ExtractAsync_ShouldFallbackOnInvalidJson()
    {
        var service = CreateService(_ => "not json");

        var result = await service.ExtractAsync(StrawberryRequest(), CancellationToken.None);

        result.Source.Should().Be(VisionAgentSemanticSources.RuleFallback);
        result.FailureCode.Should().Be(VisionAgentSemanticFailureCodes.JsonParseFailed);
        result.TaskType.Should().Be(AiVisionTaskTypes.AttributeClassification);
        result.TargetAttribute.Should().Contain("熟透");
    }

    [Fact(DisplayName = "Semantic extractor timeout returns semantic_timeout fallback")]
    public async Task ExtractAsync_ShouldFallbackOnTimeout()
    {
        var service = CreateService(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return "{}";
            },
            new VisionAgentSemanticExtractorOptions { TimeoutSeconds = 1 });

        var result = await service.ExtractAsync(StrawberryRequest(), CancellationToken.None);

        result.Source.Should().Be(VisionAgentSemanticSources.RuleFallback);
        result.FailureCode.Should().Be(VisionAgentSemanticFailureCodes.Timeout);
    }

    [Fact(DisplayName = "Semantic extractor unauthorized response returns redacted unauthorized fallback")]
    public async Task ExtractAsync_ShouldFallbackOnUnauthorized()
    {
        var service = CreateService(_ => throw new HttpRequestException(
            "401 unauthorized api_key=secret-key baseUrl=https://semantic.example.invalid C:\\factory\\model.onnx",
            null,
            HttpStatusCode.Unauthorized));

        var result = await service.ExtractAsync(StrawberryRequest(), CancellationToken.None);
        var json = JsonSerializer.Serialize(result);

        result.Source.Should().Be(VisionAgentSemanticSources.RuleFallback);
        result.FailureCode.Should().Be(VisionAgentSemanticFailureCodes.Unauthorized);
        json.Should().NotContain("secret-key");
        json.Should().NotContain("semantic.example.invalid");
        json.Should().NotContain(@"C:\factory");
    }

    private static VisionAgentSemanticExtractionRequest StrawberryRequest()
    {
        return new VisionAgentSemanticExtractionRequest
        {
            Description = "检测目标是果园里的成熟了的草莓，如果草莓熟透了，则为OK，否则为NG，输入源是相机。",
            Mode = "plan"
        };
    }

    private static VisionAgentSemanticExtractorService CreateService(
        Func<VisionAgentSemanticExtractionCompletionRequest, string> completion)
    {
        return CreateService((request, _) => Task.FromResult(completion(request)));
    }

    private static VisionAgentSemanticExtractorService CreateService(
        Func<VisionAgentSemanticExtractionCompletionRequest, CancellationToken, Task<string>> completion,
        VisionAgentSemanticExtractorOptions? options = null)
    {
        return new VisionAgentSemanticExtractorService(
            new DelegateSemanticCompletionSource(completion),
            Options.Create(options ?? new VisionAgentSemanticExtractorOptions()),
            NullLogger<VisionAgentSemanticExtractorService>.Instance);
    }

    private sealed class DelegateSemanticCompletionSource : IVisionAgentSemanticExtractionCompletionSource
    {
        private readonly Func<VisionAgentSemanticExtractionCompletionRequest, CancellationToken, Task<string>> _completion;

        public DelegateSemanticCompletionSource(
            Func<VisionAgentSemanticExtractionCompletionRequest, CancellationToken, Task<string>> completion)
        {
            _completion = completion;
        }

        public Task<string> CompleteAsync(
            VisionAgentSemanticExtractionCompletionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _completion(request, cancellationToken);
        }
    }
}
