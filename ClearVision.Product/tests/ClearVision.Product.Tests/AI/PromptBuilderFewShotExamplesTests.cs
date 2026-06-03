using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public class PromptBuilderFewShotExamplesTests
{
    [Fact(DisplayName = "PromptBuilder few-shot examples should stay valid against current validator")]
    public void BuildSystemPrompt_FewShotExamples_ShouldRemainValid()
    {
        var factory = new OperatorFactory();
        var builder = new PromptBuilder(factory);
        var validator = new AiFlowValidator(factory);
        var prompt = builder.BuildSystemPrompt("示例校验");
        var exampleJsons = ExtractFewShotJsonObjects(prompt);

        exampleJsons.Should().NotBeEmpty("the prompt should expose at least one valid few-shot flow JSON object");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleStringDictionaryJsonConverter());

        foreach (var exampleJson in exampleJsons)
        {
            var flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(exampleJson, options);
            flow.Should().NotBeNull("few-shot JSON should deserialize");

            var validation = validator.Validate(flow!);
            validation.Errors.Should().BeEmpty($"few-shot example should remain valid.\nJSON:\n{exampleJson}");
        }
    }

    [Fact(DisplayName = "PromptBuilder should expose normalized top-level section headers")]
    public void BuildSystemPrompt_ShouldContainNormalizedSections()
    {
        var factory = new OperatorFactory();
        var builder = new PromptBuilder(factory);

        var prompt = builder.BuildSystemPrompt("scratch detection");

        prompt.Should().Contain("## Section 1 - Role And Hard Rules");
        prompt.Should().Contain("## Section 7 - Operator Catalog");
        prompt.Should().Contain("## Section 10 - Output Format");
        prompt.Should().Contain("## Section 11 - Few Shot Examples");
    }

    [Fact(DisplayName = "PromptBuilder output contract should reject common invalid JSON envelopes")]
    public void BuildSystemPrompt_OutputFormat_ShouldRejectCommonInvalidEnvelopes()
    {
        var factory = new OperatorFactory();
        var builder = new PromptBuilder(factory);

        var prompt = builder.BuildSystemPrompt("generate inspection workflow");

        prompt.Should().Contain("operators as an array");
        prompt.Should().Contain("connections as an array");
        prompt.Should().Contain("Do not return an outer envelope");
        prompt.Should().Contain("Do not return the JSON object as an escaped string value");
        prompt.Should().Contain("Before replying, mentally validate");
    }

    private static List<string> ExtractFewShotJsonObjects(string prompt)
    {
        var fewShotStart = prompt.IndexOf("# Few Shot Examples", StringComparison.Ordinal);
        var fewShotSection = fewShotStart >= 0 ? prompt[fewShotStart..] : prompt;
        var robustResults = ExtractAllFlowJsonObjects(fewShotSection);
        if (robustResults.Count > 0)
        {
            return robustResults;
        }
        const string marker = "正确输出：";
        var results = new List<string>();
        var searchIndex = 0;

        while (true)
        {
            var markerIndex = prompt.IndexOf(marker, searchIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
                break;

            var objectStart = prompt.IndexOf('{', markerIndex);
            objectStart.Should().BeGreaterOrEqualTo(0, "each few-shot example should contain a JSON object");

            results.Add(ExtractJsonObject(prompt, objectStart));
            searchIndex = objectStart + 1;
        }

        return results;
    }

    private static List<string> ExtractAllFlowJsonObjects(string prompt)
    {
        var results = new List<string>();
        var searchIndex = 0;

        while (true)
        {
            var objectStart = prompt.IndexOf('{', searchIndex);
            if (objectStart < 0)
            {
                break;
            }

            searchIndex = objectStart + 1;
            try
            {
                var candidate = ExtractJsonObject(prompt, objectStart);
                using var json = JsonDocument.Parse(candidate);
                if (json.RootElement.TryGetProperty("operators", out _) &&
                    json.RootElement.TryGetProperty("connections", out _))
                {
                    results.Add(candidate);
                }
            }
            catch (JsonException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return results;
    }

    private static string ExtractJsonObject(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaping = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch != '}')
                continue;

            depth--;
            if (depth == 0)
                return text[startIndex..(i + 1)];
        }

        throw new InvalidOperationException("Failed to extract JSON object from few-shot examples.");
    }
}
