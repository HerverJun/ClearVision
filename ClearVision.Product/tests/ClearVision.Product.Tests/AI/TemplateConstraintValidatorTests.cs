using System.Text.Json;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class TemplateConstraintValidatorTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact(DisplayName = "TemplateConstraintValidator should fail when ResultOutput is removed")]
    public async Task Validate_MissingResultOutput_ShouldReturnRequiredOperatorError()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var template = await LoadTemplateAsync(tempRoot, "包装箱外观检测");
            var flow = DeserializeFlow(template);
            RemoveOperator(flow, "ResultOutput");

            var result = new TemplateConstraintValidator().Validate(flow, template, strict: true);

            result.IsValid.Should().BeFalse();
            result.Diagnostics.Should().Contain(item =>
                item.Code == "template_required_operator_missing" &&
                item.OperatorId == "ResultOutput");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "TemplateConstraintValidator should fail when wire-sequence judge is removed")]
    public async Task Validate_MissingDetectionSequenceJudge_ShouldReturnRequiredOperatorError()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var template = await LoadTemplateAsync(tempRoot, "端子线序检测");
            var flow = DeserializeFlow(template);
            RemoveOperator(flow, "DetectionSequenceJudge");

            var result = new TemplateConstraintValidator().Validate(flow, template, strict: true);

            result.IsValid.Should().BeFalse();
            result.Diagnostics.Should().Contain(item =>
                item.Code == "template_required_operator_missing" &&
                item.OperatorId == "DetectionSequenceJudge");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "TemplateConstraintValidator should fail when a required template connection is removed")]
    public async Task Validate_MissingRequiredConnection_ShouldReturnConnectionError()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var template = await LoadTemplateAsync(tempRoot, "两器铜孔间距检测");
            var flow = DeserializeFlow(template);
            flow.Connections.RemoveAll(connection =>
                connection.SourceTempId == "op_3" &&
                connection.TargetTempId == "op_4");

            var result = new TemplateConstraintValidator().Validate(flow, template, strict: true);

            result.IsValid.Should().BeFalse();
            result.Diagnostics.Should().Contain(item =>
                item.Code == "template_required_connection_missing");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "TemplateConstraintValidator should report missing model path as a resource warning")]
    public async Task Validate_MissingModelPath_ShouldReturnResourceWarningWithoutStructuralFailure()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var template = await LoadTemplateAsync(tempRoot, "包装箱外观检测");
            var flow = DeserializeFlow(template);

            var result = new TemplateConstraintValidator().Validate(flow, template, strict: true);

            result.IsValid.Should().BeTrue();
            result.Diagnostics.Should().Contain(item =>
                item.Code == "template_required_resource_missing" &&
                item.ParameterName == "DeepLearning.ModelPath");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact(DisplayName = "TemplateConstraintValidator should warn when strict template-fill adds non-template operators")]
    public async Task Validate_StrictUnexpectedOperator_ShouldReturnDriftWarning()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var template = await LoadTemplateAsync(tempRoot, "包装箱外观检测");
            var flow = DeserializeFlow(template);
            flow.Operators.Add(new AiGeneratedOperator
            {
                TempId = "op_extra",
                OperatorType = "BlobAnalysis",
                DisplayName = "额外 Blob 分析"
            });

            var result = new TemplateConstraintValidator().Validate(flow, template, strict: true);

            result.Diagnostics.Should().Contain(item =>
                item.Code == "template_unexpected_operator" &&
                item.Severity == "warning");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static async Task<FlowTemplate> LoadTemplateAsync(string tempRoot, string templateName)
    {
        var service = new FlowTemplateService(tempRoot);
        var templates = await service.GetTemplatesAsync();
        return templates.Single(template => template.Name == templateName);
    }

    private static AiGeneratedFlowJson DeserializeFlow(FlowTemplate template)
    {
        var flow = JsonSerializer.Deserialize<AiGeneratedFlowJson>(template.FlowJson, JsonOptions);
        flow.Should().NotBeNull();
        return flow!;
    }

    private static void RemoveOperator(AiGeneratedFlowJson flow, string operatorType)
    {
        flow.Operators.RemoveAll(op => op.OperatorType.Equals(operatorType, StringComparison.OrdinalIgnoreCase));
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
        return Path.Combine(Path.GetTempPath(), "clearvision-template-gate-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
            Directory.Delete(tempRoot, recursive: true);
    }
}
