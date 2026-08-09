using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Handlers;

internal sealed class FilePickerMessageHandler
{
    private readonly IHostMessageClient _client;
    private readonly ILogger<FilePickerMessageHandler> _logger;

    public FilePickerMessageHandler(
        IHostMessageClient client,
        ILogger<FilePickerMessageHandler> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task HandleAsync(string messageJson)
    {
        try
        {
            var command = JsonSerializer.Deserialize<PickFileCommand>(messageJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (command == null)
            {
                _logger.LogWarning("[FilePickerMessageHandler] Failed to parse PickFileCommand.");
                return;
            }

            var (filePath, isCancelled) = await ShowFileDialogOnStaThreadAsync(command);

            _client.SendEvent(new FilePickedEvent
            {
                ParameterName = command.ParameterName,
                FilePath = filePath,
                IsCancelled = isCancelled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FilePickerMessageHandler] File picker command failed.");
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
