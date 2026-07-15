using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class IndustrialTemplateLibraryTests
{
    private static readonly string[] NewTraditionalTemplateNames =
    [
        "梯度形状匹配定位检测",
        "平面特征匹配定位检测",
        "Blob缺陷区域分析",
        "卡尺宽度测量",
        "圆孔孔径与圆度检测",
        "Lab色差检测",
        "条码二维码追溯检测",
        "参考图表面缺陷检测",
        "清晰度对焦质量门"
    ];

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact(DisplayName = "FlowTemplateService - 新增传统视觉模板应可解析且不依赖深度学习")]
    public async Task FlowTemplateService_NewIndustrialTemplates_ShouldBeParseableAndTraditional()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = new FlowTemplateService(tempRoot);
            var templates = await service.GetTemplatesAsync();
            var byName = templates.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var name in NewTraditionalTemplateNames)
            {
                byName.Should().ContainKey(name);
                var template = byName[name];
                template.TemplateVersion.Should().Be("1.0.0");
                template.ScenarioKey.Should().NotBeNullOrWhiteSpace();
                template.ScenarioPackage.Should().NotBeNull();
                template.ScenarioPackage!.RequiredResources.Should().BeEmpty();
                template.ScenarioPackage.AssetVersionIds.Should()
                    .Contain(item => item.StartsWith("template:", StringComparison.OrdinalIgnoreCase));
                template.ScenarioPackage.AssetVersionIds.Should()
                    .Contain(item => item.StartsWith("rule:", StringComparison.OrdinalIgnoreCase));

                using var document = JsonDocument.Parse(template.FlowJson);
                document.RootElement.GetProperty("explanation").GetString().Should().NotBeNullOrWhiteSpace();
                document.RootElement.GetProperty("tunableParameters").GetArrayLength().Should().BeGreaterThan(0);
                document.RootElement.GetProperty("parametersNeedingReview").EnumerateObject()
                    .Should().NotBeEmpty();

                var flow = DeserializeFlow(template);
                flow.Operators.Select(item => item.OperatorType).Should().NotContain("DeepLearning");
                flow.Operators.Select(item => item.OperatorType).Should().Contain("ResultOutput");
                flow.Connections.Should().NotBeEmpty();
            }
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "FlowTemplateService - 新增工业模板端口应通过 AI 校验器")]
    public async Task FlowTemplateService_NewIndustrialTemplates_ShouldUseValidOperatorPorts()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = new FlowTemplateService(tempRoot);
            var templates = (await service.GetTemplatesAsync())
                .Where(item => NewTraditionalTemplateNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            templates.Should().HaveCount(NewTraditionalTemplateNames.Length);
            var validator = new AiFlowValidator(new OperatorFactory());

            foreach (var template in templates)
            {
                var flow = DeserializeFlow(template);
                var result = validator.Validate(flow);

                result.IsValid.Should().BeTrue($"{template.Name} should be structurally valid: {string.Join(Environment.NewLine, result.Errors)}");
                result.Diagnostics
                    .Where(IsPortContractDiagnostic)
                    .Should().BeEmpty($"{template.Name} should only use declared operator ports and compatible port types");
            }
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "FlowTemplateService - built-in templates should disable ResultOutput file persistence by default")]
    public async Task FlowTemplateService_BuiltInTemplates_ShouldDisableResultOutputFilePersistenceByDefault()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = new FlowTemplateService(tempRoot);
            var templates = await service.GetTemplatesAsync();

            templates.Should().NotBeEmpty();
            foreach (var template in templates)
            {
                using var document = JsonDocument.Parse(template.FlowJson);
                var resultOutputs = document.RootElement.GetProperty("operators")
                    .EnumerateArray()
                    .Where(item =>
                        item.TryGetProperty("operatorType", out var operatorType) &&
                        string.Equals(operatorType.GetString(), "ResultOutput", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                resultOutputs.Should().NotBeEmpty($"{template.Name} should expose a ResultOutput operator");
                foreach (var resultOutput in resultOutputs)
                {
                    var hasSaveToFile = resultOutput.GetProperty("parameters")
                        .TryGetProperty("SaveToFile", out var saveToFile);

                    hasSaveToFile.Should().BeTrue($"{template.Name} ResultOutput should declare SaveToFile explicitly");
                    saveToFile.GetString().Should().Be("false", $"{template.Name} should not write per-result files unless explicitly enabled");
                }
            }
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static AiGeneratedFlowJson DeserializeFlow(FlowTemplate template)
    {
        var flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(template.FlowJson, JsonOptions);
        flow.Should().NotBeNull();
        return flow!;
    }

    private static bool IsPortContractDiagnostic(AiValidationDiagnostic diagnostic)
    {
        return diagnostic.Code is
            "missing_source_operator" or
            "missing_target_operator" or
            "missing_output_port" or
            "missing_input_port" or
            "incompatible_port_type";
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new FlexibleStringDictionaryJsonConverter());
        return options;
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "clearvision-industrial-template-test-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
