using ClearVision.Product.Runtime.Abstractions;
using ClearVision.Product.Station.Sync;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Tests;

public sealed class StationHubClientTests
{
    private static readonly TimeSpan TestCompletionTimeout = TimeSpan.FromSeconds(30);

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
        await WaitForAsync(
            connection.InvokeStarted.Task,
            connection,
            "Station hub invocation did not enter the blocking connection");

        var disconnectTask = client.DisconnectAsync(CancellationToken.None);

        disconnectTask.IsCompleted.Should().BeFalse();
        connection.DisposeStarted.Task.IsCompleted.Should().BeFalse();

        connection.AllowInvoke.TrySetResult();
        (await WaitForAsync(
            reportTask,
            connection,
            "Station hub invocation did not complete after it was released")).Should().BeTrue();
        await WaitForAsync(
            disconnectTask,
            connection,
            "Station hub disconnect did not complete after the in-flight invocation exited");

        connection.Disposed.Should().BeTrue();
        connection.InvokeCompleted.Task.IsCompleted.Should().BeTrue();
    }

    private static async Task WaitForAsync(
        Task task,
        BlockingStationHubConnection connection,
        string failureMessage)
    {
        try
        {
            await task.WaitAsync(TestCompletionTimeout);
        }
        catch (TimeoutException ex)
        {
            throw CreateTimeoutException(failureMessage, connection, ex);
        }
    }

    private static async Task<T> WaitForAsync<T>(
        Task<T> task,
        BlockingStationHubConnection connection,
        string failureMessage)
    {
        try
        {
            return await task.WaitAsync(TestCompletionTimeout);
        }
        catch (TimeoutException ex)
        {
            throw CreateTimeoutException(failureMessage, connection, ex);
        }
    }

    private static TimeoutException CreateTimeoutException(
        string failureMessage,
        BlockingStationHubConnection connection,
        TimeoutException innerException) =>
        new(
            $"{failureMessage} within {TestCompletionTimeout}. " +
            $"State={connection.State}; InvokeStarted={connection.InvokeStarted.Task.Status}; " +
            $"InvokeCompleted={connection.InvokeCompleted.Task.Status}; " +
            $"DisposeStarted={connection.DisposeStarted.Task.Status}; Disposed={connection.Disposed}.",
            innerException);

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
