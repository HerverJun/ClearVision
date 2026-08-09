using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Desktop.Tests;

internal static class WorkflowArtifactAdmissionTestSupport
{
    public static OperatorDto CreateCanonicalResultJudgmentOperator(string name)
    {
        const OperatorType type = OperatorType.ResultJudgment;
        var metadata = new OperatorFactory().GetMetadata(type)
            ?? throw new InvalidOperationException($"Missing metadata for {type}.");
        var result = new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            InputPorts = metadata.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Input,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = metadata.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Output,
                DataType = port.DataType
            }).ToList(),
            Parameters = metadata.Parameters.Select(parameter => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.DefaultValue,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList(),
            IsEnabled = true
        };

        SetParameter(result, "ExpectValueMin", "0");
        SetParameter(result, "ExpectValueMax", "1");
        return result;
    }

    public static IWorkflowArtifactAdmissionGate CreateGate()
    {
        var factory = new OperatorFactory();
        return new WorkflowArtifactAdmissionGate(
            new WorkflowLegacyScanner(factory),
            new WorkflowLegacyRepairService(factory),
            new DiscardingQuarantineStore());
    }

    private sealed class DiscardingQuarantineStore : IWorkflowArtifactQuarantineStore
    {
        public void Preserve(WorkflowArtifactQuarantineRecord record)
        {
        }
    }

    private static void SetParameter(OperatorDto operatorDto, string name, object value)
    {
        var parameter = operatorDto.Parameters.Single(item => item.Name == name);
        parameter.Value = value;
        parameter.DefaultValue = value;
    }
}
