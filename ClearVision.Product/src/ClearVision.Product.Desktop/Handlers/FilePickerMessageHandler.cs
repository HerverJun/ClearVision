using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Handlers;

internal sealed class FilePickerMessageHandler
{
    private readonly IWebMessageClient _client;
    private readonly ILogger<FilePickerMessageHandler> _logger;

    public FilePickerMessageHandler(
        IWebMessageClient client,
        ILogger<FilePickerMessageHandler> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task HandleAsync(string messageJson, WebMessageDeliveryBinding binding)
    {
        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            var commandElement = root.TryGetProperty("payload", out var payload) &&
                                 payload.ValueKind == JsonValueKind.Object
                ? payload
                : root;
            var command = commandElement.Deserialize<PickFileCommand>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (command == null || string.IsNullOrWhiteSpace(command.ParameterName))
            {
                _client.SendBoundProgressMessage("PickFileCommandResult", new
                {
                    success = false,
                    code = WebMessageErrorCodes.Validation,
                    errorMessage = "parameterName is required."
                }, binding);
                return;
            }

            var (filePath, isCancelled) = await ShowFileDialogOnStaThreadAsync(command);

            _client.SendBoundEvent(new FilePickedEvent
            {
                ParameterName = command.ParameterName,
                FilePath = filePath,
                IsCancelled = isCancelled
            }, binding);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[FilePickerMessageHandler] Invalid file picker payload.");
            _client.SendBoundProgressMessage("PickFileCommandResult", new
            {
                success = false,
                code = WebMessageErrorCodes.Validation,
                errorMessage = "File picker payload is invalid."
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FilePickerMessageHandler] File picker command failed.");
            _client.SendBoundProgressMessage("PickFileCommandResult", new
            {
                success = false,
                code = WebMessageErrorCodes.Conflict,
                errorMessage = "File picker is temporarily unavailable."
            }, binding);
        }
    }

    private Task<(string? filePath, bool isCancelled)> ShowFileDialogOnStaThreadAsync(PickFileCommand command)
    {
        var tcs = new TaskCompletionSource<(string?, bool)>();

        var thread = new Thread(() =>
        {
            try
            {
                using var dialog = new System.Windows.Forms.OpenFileDialog
                {
                    Filter = command.Filter,
                    Title = "Select file"
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    tcs.SetResult((dialog.FileName, false));
                }
                else
                {
                    tcs.SetResult((null, true));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FilePickerMessageHandler] File picker dialog thread failed.");
                tcs.SetResult((null, true));
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }
}
