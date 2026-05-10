using Acme.Product.Station;
using Acme.Product.Station.Sync;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Acme.Product.Desktop.Tests;

public sealed class StationLogRelayServiceTests
{
    [Fact]
    public void TryEnqueue_ShouldPersistLogSequenceAcrossServiceRestart()
    {
        var root = CreateTempDirectory();
        try
        {
            var first = CreateRelay(root);
            first.TryEnqueue("WARN", "RuntimeHost", "first warning").Should().BeTrue();
            first.TryRead(out var firstLog).Should().BeTrue();
            firstLog.SequenceId.Should().Be(1);

            var restarted = CreateRelay(root);
            restarted.TryEnqueue("WARN", "RuntimeHost", "second warning").Should().BeTrue();
            restarted.TryRead(out var secondLog).Should().BeTrue();
            secondLog.SequenceId.Should().Be(2);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void TryEnqueue_ShouldRedactTokenValuesFromRenderedMessage()
    {
        var root = CreateTempDirectory();
        try
        {
            var relay = CreateRelay(root);

            relay.TryEnqueue(
                "ERROR",
                "RuntimeHost",
                "X-ClearVision-Station-Token: secret-1 StationSync:SharedToken=secret-2 X-Station-Token=secret-3")
                .Should()
                .BeTrue();

            relay.TryRead(out var log).Should().BeTrue();
            log.RenderedMessage.Should().NotContain("secret-1");
            log.RenderedMessage.Should().NotContain("secret-2");
            log.RenderedMessage.Should().NotContain("secret-3");
            log.RenderedMessage.Should().Contain("redacted");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void TryEnqueue_ShouldRateLimitHighFrequencyErrors()
    {
        var root = CreateTempDirectory();
        try
        {
            var relay = CreateRelay(root, maxSummariesPerMinute: 3);

            for (var index = 0; index < 20; index++)
            {
                relay.TryEnqueue("ERROR", "RuntimeHost", $"runtime error burst {index}");
            }

            var accepted = 0;
            while (relay.TryRead(out _))
            {
                accepted++;
            }

            accepted.Should().Be(3);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void TryEnqueue_ShouldReportDroppedLogSummary_WhenQueueIsFull()
    {
        var root = CreateTempDirectory();
        try
        {
            var relay = CreateRelay(root, logQueueCapacity: 1);

            relay.TryEnqueue("ERROR", "RuntimeHost", "runtime error one").Should().BeTrue();
            relay.TryEnqueue("ERROR", "RuntimeHost", "runtime error two").Should().BeFalse();

            relay.DroppedLogSummaryCount.Should().Be(1);
            relay.TryRead(out var log).Should().BeTrue();
            log.RenderedMessage.Should().Be("runtime error one");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static StationLogRelayService CreateRelay(
        string root,
        int maxSummariesPerMinute = 60,
        int logQueueCapacity = 10)
    {
        var settingsStore = new StationLocalSettingsStore(root);
        settingsStore.UpdateStationIdentity("station-log", "line-log");
        return new StationLogRelayService(
            new StationIdentityResolver(settingsStore),
            settingsStore,
            Options.Create(new StationSyncOptions
            {
                LogQueueCapacity = logQueueCapacity,
                MaxLogSummariesPerMinute = maxSummariesPerMinute
            }));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionStationLogRelayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
