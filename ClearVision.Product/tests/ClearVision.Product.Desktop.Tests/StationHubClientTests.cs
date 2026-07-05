using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationHubClientTests
{
    [Fact]
    public async Task DisconnectAsync_WhenInvocationIsInFlight_ShouldWaitBeforeDisposingConnection()
    {
        var connection = new BlockingStationHubConnection();
        await using var client = new StationHubClient(
            Options.Create(new StationSyncOptions
            {
                Enabled = true,
                StudioBaseUrl = "http://127.0.0.1:5000",
                SharedToken = "station-secret"
            }),
            NullLogger<StationHubClient>.Instance,
            (_, _) => connection);

        var reportTask = client.ReportCommandResultAsync(
            new StationCommandResultDto
            {
                CommandId = "cmd-1",
                StationId = "station-1",
                Status = StationCommandStatus.Running,
                ProgressPercent = 50
            },
            CancellationToken.None);
        await connection.InvokeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disconnectTask = client.DisconnectAsync(CancellationToken.None);
        await Task.Delay(100);

        disconnectTask.IsCompleted.Should().BeFalse();
        connection.DisposeStarted.Task.IsCompleted.Should().BeFalse();

        connection.AllowInvoke.TrySetResult();
        (await reportTask.WaitAsync(TimeSpan.FromSeconds(2))).Should().BeTrue();
        await disconnectTask.WaitAsync(TimeSpan.FromSeconds(2));

        connection.Disposed.Should().BeTrue();
        connection.InvokeCompleted.Task.IsCompleted.Should().BeTrue();
    }

    private sealed class BlockingStationHubConnection : IStationHubConnection
    {
        public TaskCompletionSource InvokeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource AllowInvoke { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InvokeCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public HubConnectionState State { get; private set; } = HubConnectionState.Disconnected;

        public event Func<Exception?, Task>? Closed
        {
            add { }
            remove { }
        }

        public event Func<Exception?, Task>? Reconnecting
        {
            add { }
            remove { }
        }

        public event Func<string?, Task>? Reconnected
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            State = HubConnectionState.Connected;
            return Task.CompletedTask;
        }

        public async Task<T> InvokeAsync<T>(string methodName, object payload, CancellationToken cancellationToken)
        {
            await InvokeAsync(methodName, payload, cancellationToken);
            return default!;
        }

        public async Task InvokeAsync(string methodName, object payload, CancellationToken cancellationToken)
        {
            InvokeStarted.TrySetResult();
            await AllowInvoke.Task.WaitAsync(cancellationToken);
            Disposed.Should().BeFalse("disconnect/dispose must wait for in-flight Station hub invocations");
            InvokeCompleted.TrySetResult();
        }

        public ValueTask DisposeAsync()
        {
            DisposeStarted.TrySetResult();
            Disposed = true;
            State = HubConnectionState.Disconnected;
            return ValueTask.CompletedTask;
        }
    }
}
