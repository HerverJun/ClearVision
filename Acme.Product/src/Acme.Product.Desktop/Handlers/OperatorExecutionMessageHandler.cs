using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Acme.Product.Application.DTOs;
using Acme.Product.Application.Services;
using Acme.Product.Contracts.Messages;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Interfaces;
using Acme.Product.Core.Services;
using Acme.Product.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Handlers;

internal sealed class OperatorExecutionMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebMessageClient _client;
    private readonly ILogger<OperatorExecutionMessageHandler> _logger;

    public OperatorExecutionMessageHandler(
        IServiceScopeFactory scopeFactory,
        IWebMessageClient client,
        ILogger<OperatorExecutionMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    public async Task HandleAsync(string messageJson)
    {
        var command = JsonSerializer.Deserialize<ExecuteOperatorCommand>(messageJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (command == null)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var flowService = scope.ServiceProvider.GetRequiredService<IFlowExecutionService>();
            var op = await ResolveOperatorAsync(scope.ServiceProvider, command.OperatorId);
            var result = await flowService.ExecuteOperatorAsync(op, NormalizeDictionary(command.Inputs));

            _client.SendEvent(new OperatorExecutedEvent
            {
                OperatorId = command.OperatorId,
                OperatorName = op.Name,
                IsSuccess = result.IsSuccess,
                OutputData = result.OutputData,
                ExecutionTimeMs = result.ExecutionTimeMs,
                ErrorMessage = result.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OperatorExecutionMessageHandler] Execute operator command failed.");
            _client.SendEvent(new OperatorExecutedEvent
            {
                OperatorId = command.OperatorId,
                OperatorName = "Unknown",
                IsSuccess = false,
                ErrorMessage = ex.Message
            });
        }
    }

    private static async Task<Operator> ResolveOperatorAsync(IServiceProvider serviceProvider, Guid operatorId)
    {
        var projectRepository = serviceProvider.GetRequiredService<IProjectRepository>();
        var projectService = serviceProvider.GetRequiredService<ProjectService>();

        foreach (var project in await projectRepository.GetAllAsync())
        {
            var projectDto = await projectService.GetByIdAsync(project.Id);
            var operatorDto = projectDto?.Flow?.Operators?.FirstOrDefault(op => op.Id == operatorId);
            if (operatorDto != null)
            {
                var flowDto = new OperatorFlowDto
                {
                    Name = projectDto?.Flow?.Name ?? "WebMessageFlow",
                    Operators = new List<OperatorDto> { operatorDto }
                };

                return flowDto.ToEntity().Operators.Single();
            }
        }

        var dbContext = serviceProvider.GetRequiredService<VisionDbContext>();
        var databaseOperator = await dbContext.Operators
            .Include(op => op.InputPorts)
            .Include(op => op.OutputPorts)
            .Include(op => op.Parameters)
            .FirstOrDefaultAsync(op => op.Id == operatorId);

        return databaseOperator
            ?? throw new KeyNotFoundException($"Operator was not found: {operatorId}");
    }

    private static Dictionary<string, object>? NormalizeDictionary(Dictionary<string, object>? values)
    {
        if (values == null)
        {
            return null;
        }

        return values.ToDictionary(
            item => item.Key,
            item => NormalizeJsonValue(item.Value) ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
    }

    private static object? NormalizeJsonValue(object? value)
    {
        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            Dictionary<string, object> dictionary => NormalizeDictionary(dictionary),
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
}
