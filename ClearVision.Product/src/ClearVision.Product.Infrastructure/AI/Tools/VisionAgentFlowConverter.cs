using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentFlowConverter
{
    public static OperatorFlow ToEntity(AiGeneratedFlowJson generatedFlow, IOperatorFactory operatorFactory)
    {
        var flow = new OperatorFlow("Vision Agent DryRun Flow");
        var operatorByTempId = new Dictionary<string, Operator>(StringComparer.OrdinalIgnoreCase);
        var inputPorts = new Dictionary<(string TempId, string PortName), Guid>();
        var outputPorts = new Dictionary<(string TempId, string PortName), Guid>();

        for (var index = 0; index < generatedFlow.Operators.Count; index++)
        {
            var item = generatedFlow.Operators[index];
            if (!Enum.TryParse<OperatorType>(item.OperatorType, ignoreCase: true, out var operatorType))
            {
                throw new InvalidOperationException($"Unknown operator type '{item.OperatorType}'.");
            }

            var metadata = operatorFactory.GetMetadata(operatorType)
                ?? throw new InvalidOperationException($"Operator type '{operatorType}' is not registered.");
            var op = new Operator(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(item.DisplayName) ? metadata.DisplayName : item.DisplayName,
                operatorType,
                x: 120 + index * 220,
                y: 120);

            foreach (var port in metadata.InputPorts)
            {
                var id = Guid.NewGuid();
                op.LoadInputPort(id, port.Name, port.DataType, port.IsRequired);
                inputPorts[(item.TempId, port.Name)] = id;
            }

            foreach (var port in metadata.OutputPorts)
            {
                var id = Guid.NewGuid();
                op.LoadOutputPort(id, port.Name, port.DataType);
                outputPorts[(item.TempId, port.Name)] = id;
            }

            foreach (var parameter in metadata.Parameters)
            {
                var parameterValue = item.Parameters.TryGetValue(parameter.Name, out var rawValue)
                    ? CoerceParameterValue(rawValue, parameter.DataType)
                    : parameter.DefaultValue;
                var runtimeParameter = new Parameter(
                    Guid.NewGuid(),
                    parameter.Name,
                    parameter.DisplayName,
                    parameter.Description ?? string.Empty,
                    parameter.DataType,
                    parameter.DefaultValue,
                    parameter.MinValue,
                    parameter.MaxValue,
                    parameter.IsRequired,
                    parameter.Options);
                runtimeParameter.SetValue(parameterValue);
                op.AddParameter(runtimeParameter);
            }

            flow.AddOperator(op);
            operatorByTempId[item.TempId] = op;
        }

        foreach (var connection in generatedFlow.Connections)
        {
            if (!operatorByTempId.TryGetValue(connection.SourceTempId, out var source) ||
                !operatorByTempId.TryGetValue(connection.TargetTempId, out var target))
            {
                throw new InvalidOperationException(
                    $"Connection references unknown operators: {connection.SourceTempId} -> {connection.TargetTempId}.");
            }

            if (!outputPorts.TryGetValue((connection.SourceTempId, connection.SourcePortName), out var sourcePortId) ||
                !inputPorts.TryGetValue((connection.TargetTempId, connection.TargetPortName), out var targetPortId))
            {
                throw new InvalidOperationException(
                    $"Connection references unknown ports: {connection.SourceTempId}.{connection.SourcePortName} -> {connection.TargetTempId}.{connection.TargetPortName}.");
            }

            flow.AddConnection(new OperatorConnection(source.Id, sourcePortId, target.Id, targetPortId));
        }

        return flow;
    }

    private static object? CoerceParameterValue(string rawValue, string dataType)
    {
        if (string.IsNullOrWhiteSpace(dataType))
        {
            return rawValue;
        }

        var normalized = dataType.Trim().ToLowerInvariant();
        if (normalized is "int" or "integer" && int.TryParse(rawValue, out var intValue))
        {
            return intValue;
        }

        if (normalized is "long" && long.TryParse(rawValue, out var longValue))
        {
            return longValue;
        }

        if ((normalized is "double" or "float" or "number" or "decimal") &&
            double.TryParse(rawValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
        {
            return doubleValue;
        }

        if (normalized is "bool" or "boolean" && bool.TryParse(rawValue, out var boolValue))
        {
            return boolValue;
        }

        return rawValue;
    }
}

