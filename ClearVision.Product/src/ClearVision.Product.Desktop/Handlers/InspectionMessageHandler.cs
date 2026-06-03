using System;
using System.Text.Json;
using System.Threading.Tasks;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Handlers;

internal sealed class InspectionMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebMessageClient _client;
    private readonly ILogger<InspectionMessageHandler> _logger;

    public InspectionMessageHandler(
        IServiceScopeFactory scopeFactory,
        IWebMessageClient client,
        ILogger<InspectionMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    public async Task HandleStartAsync(string messageJson)
    {
        var command = JsonSerializer.Deserialize<StartInspectionCommand>(messageJson, new JsonSerializerOptions
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
            var inspectionService = scope.ServiceProvider.GetRequiredService<IInspectionService>();

            byte[]? imageData = null;
            if (!string.IsNullOrEmpty(command.ImageBase64))
            {
                imageData = Convert.FromBase64String(command.ImageBase64);
            }

            var result = imageData != null
                ? await inspectionService.ExecuteSingleAsync(command.ProjectId, imageData)
                : await inspectionService.ExecuteSingleAsync(command.ProjectId, command.CameraId ?? "default");

            _client.NotifyInspectionResult(result, command.ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[InspectionMessageHandler] Start inspection command failed.");
        }
    }

    public Task HandleStopAsync()
    {
        throw new NotSupportedException(
            "StopInspectionCommand is disabled. Use /api/inspection/realtime/stop instead.");
    }
}
