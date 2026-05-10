using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Contracts.Messages;
using Acme.Product.Core.DTOs;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Handlers;

internal sealed class ProjectFlowMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOperatorFactory _operatorFactory;
    private readonly ILogger<ProjectFlowMessageHandler> _logger;

    public ProjectFlowMessageHandler(
        IServiceScopeFactory scopeFactory,
        IOperatorFactory operatorFactory,
        ILogger<ProjectFlowMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _operatorFactory = operatorFactory;
        _logger = logger;
    }

    public async Task HandleUpdateFlowAsync(string messageJson)
    {
        var command = JsonSerializer.Deserialize<UpdateFlowCommand>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (command?.Flow == null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var projectService = scope.ServiceProvider.GetRequiredService<ProjectService>();
        var updateRequest = BuildUpdateFlowRequest(command.Flow);

        _logger.LogInformation(
            "[ProjectFlowMessageHandler] Flow update requested. ProjectId={ProjectId}, OperatorCount={OperatorCount}, ConnectionCount={ConnectionCount}",
            command.ProjectId,
            updateRequest.Operators.Count,
            updateRequest.Connections.Count);

        await projectService.UpdateFlowAsync(command.ProjectId, updateRequest);
    }

    private UpdateFlowRequest BuildUpdateFlowRequest(FlowData flowData)
    {
        var operators = flowData.Operators.Select(BuildOperatorDto).ToList();
        var operatorsById = operators.ToDictionary(op => op.Id);
        var connections = new List<OperatorConnectionDto>();

        foreach (var connection in flowData.Connections)
        {
            if (!operatorsById.TryGetValue(connection.SourceOperatorId, out var sourceOperator))
            {
                throw new InvalidOperationException($"Flow source operator does not exist: {connection.SourceOperatorId}");
            }

            if (!operatorsById.TryGetValue(connection.TargetOperatorId, out var targetOperator))
            {
                throw new InvalidOperationException($"Flow target operator does not exist: {connection.TargetOperatorId}");
            }

            var sourcePort = sourceOperator.OutputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, connection.SourcePort, StringComparison.OrdinalIgnoreCase));
            if (sourcePort == null)
            {
                throw new InvalidOperationException(
                    $"Output port '{connection.SourcePort}' does not exist on operator '{sourceOperator.Name}'.");
            }

            var targetPort = targetOperator.InputPorts.FirstOrDefault(port =>
                string.Equals(port.Name, connection.TargetPort, StringComparison.OrdinalIgnoreCase));
            if (targetPort == null)
            {
                throw new InvalidOperationException(
                    $"Input port '{connection.TargetPort}' does not exist on operator '{targetOperator.Name}'.");
            }

            connections.Add(new OperatorConnectionDto
            {
                Id = Guid.NewGuid(),
                SourceOperatorId = sourceOperator.Id,
                SourcePortId = sourcePort.Id,
                TargetOperatorId = targetOperator.Id,
                TargetPortId = targetPort.Id
            });
        }

        return new UpdateFlowRequest
        {
            Operators = operators,
            Connections = connections
        };
    }

    private OperatorDto BuildOperatorDto(OperatorData operatorData)
    {
        if (!Enum.TryParse<OperatorType>(operatorData.Type, true, out var parsedType))
        {
            throw new InvalidOperationException($"Unsupported operator type: {operatorData.Type}");
        }

        var operatorType = OperatorTypeAliasResolver.Resolve(parsedType);
        var @operator = _operatorFactory.CreateOperator(
            operatorType,
            string.IsNullOrWhiteSpace(operatorData.Name) ? operatorType.ToString() : operatorData.Name,
            operatorData.X,
            operatorData.Y);

        typeof(Acme.Product.Core.Entities.Operator)
            .GetProperty(nameof(Acme.Product.Core.Entities.Operator.Id))
            ?.SetValue(@operator, operatorData.Id);

        if (operatorData.Parameters != null)
        {
            foreach (var (name, value) in operatorData.Parameters)
            {
                var normalizedName = NormalizeParameterName(operatorType, name);
                var parameter = @operator.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

                parameter?.SetValue(NormalizeJsonValue(value));
            }
        }

        return new OperatorDto
        {
            Id = @operator.Id,
            Name = @operator.Name,
            Type = @operator.Type,
            X = operatorData.X,
            Y = operatorData.Y,
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
            }).ToList(),
            IsEnabled = @operator.IsEnabled,
            ExecutionStatus = @operator.ExecutionStatus,
            ExecutionTimeMs = @operator.ExecutionTimeMs,
            ErrorMessage = @operator.ErrorMessage
        };
    }

    private static object? NormalizeJsonValue(object? value)
    {
        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            Dictionary<string, object> dictionary => dictionary.ToDictionary(
                item => item.Key,
                item => NormalizeJsonValue(item.Value) ?? string.Empty,
                StringComparer.OrdinalIgnoreCase),
            IEnumerable<object> sequence => sequence.Select(NormalizeJsonValue).ToList(),
            _ => value
        };
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(property => property.Name, property => NormalizeJsonElement(property.Value) ?? string.Empty),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };
    }

    private static string NormalizeParameterName(OperatorType operatorType, string parameterName)
    {
        return operatorType == OperatorType.HistogramEqualization &&
               string.Equals(parameterName, "TileSize", StringComparison.OrdinalIgnoreCase)
            ? "TileGridSize"
            : parameterName;
    }
}
