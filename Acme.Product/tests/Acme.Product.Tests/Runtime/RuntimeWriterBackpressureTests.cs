using System.Diagnostics;
using System.Reflection;
using System.Threading.Channels;
using Acme.Product.Runtime;
using Acme.Product.Runtime.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Acme.Product.Tests.Runtime;

public sealed class RuntimeWriterBackpressureTests
{
    [Fact]
    public async Task ResultRecordWriter_ShouldFlushAllQueuedRecords_WhenCapacityIsSmall()
    {
        var root = CreateTempDirectory();
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeResultRecordWriter",
            root,
            1,
            NullLogger.Instance);

        try
        {
            var tasks = Enumerable.Range(1, 40)
                .Select(index => EnqueueAsync(writer, BuildResult(index)))
                .ToArray();

            (await Task.WhenAll(tasks)).Should().OnlyContain(accepted => accepted);
            await ((IAsyncDisposable)writer).DisposeAsync();

            var resultFile = Directory.EnumerateFiles(root, "runtime-results.jsonl", SearchOption.AllDirectories)
                .Single();
            File.ReadLines(resultFile).Should().HaveCount(40);
            ReadIntProperty(writer, "DroppedCount").Should().Be(0);
        }
        finally
        {
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ImageWriter_ShouldFlushAllQueuedImages_WhenCapacityIsSmall()
    {
        var root = CreateTempDirectory();
        var profile = new RuntimeProfile
        {
            ImageQueueCapacity = 1
        };
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeImageWriter",
            root,
            profile,
            NullLogger.Instance);

        try
        {
            var tasks = Enumerable.Range(1, 20)
                .Select(index =>
                {
                    var result = BuildResult(index);
                    result.SavedImagePath = Path.Combine(root, "images", $"{index:D2}.png");
                    result.OutputImageBytes = [0x89, 0x50, 0x4E, 0x47, (byte)index];
                    return EnqueueAsync(writer, result);
                })
                .ToArray();

            (await Task.WhenAll(tasks)).Should().OnlyContain(accepted => accepted);
            await ((IAsyncDisposable)writer).DisposeAsync();

            Directory.EnumerateFiles(Path.Combine(root, "images"), "*.png").Should().HaveCount(20);
            ReadIntProperty(writer, "DroppedCount").Should().Be(0);
        }
        finally
        {
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ResultRecordWriter_ShouldSnapshotQueuedResultBeforeReturning()
    {
        var root = CreateTempDirectory();
        var consumer = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeResultRecordWriter",
            root,
            2,
            NullLogger.Instance,
            TimeSpan.FromSeconds(5),
            consumer.Task);
        var result = BuildResult(1);
        result.PrimaryOutputs["Score"] = 1;
        result.PrimaryOutputs["Nested"] = new Dictionary<string, object?>
        {
            ["Value"] = "before"
        };
        result.OutputImageBytes = [1, 2, 3];

        try
        {
            (await EnqueueAsync(writer, result)).Should().BeTrue();

            result.RunId = "mutated-run";
            result.PrimaryOutputs["Score"] = 99;
            ((Dictionary<string, object?>)result.PrimaryOutputs["Nested"]!)["Value"] = "after";
            result.OutputImageBytes[0] = 9;

            var queued = ReadQueuedResult(writer);
            queued.RunId.Should().Be("run-001");
            queued.PrimaryOutputs["Score"].Should().Be(1);
            ((Dictionary<string, object?>)queued.PrimaryOutputs["Nested"]!)["Value"].Should().Be("before");
            queued.OutputImageBytes.Should().BeNull();
            queued.SourceImageBytes.Should().BeNull();
        }
        finally
        {
            consumer.TrySetResult(null);
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ImageWriter_ShouldSnapshotQueuedImageBeforeReturning()
    {
        var root = CreateTempDirectory();
        var consumer = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = new RuntimeProfile
        {
            ImageQueueCapacity = 2
        };
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeImageWriter",
            root,
            profile,
            NullLogger.Instance,
            TimeSpan.FromSeconds(5),
            consumer.Task);
        var result = BuildResult(1);
        result.SavedImagePath = Path.Combine(root, "images", "before.png");
        result.OutputImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x01];

        try
        {
            (await EnqueueAsync(writer, result)).Should().BeTrue();

            result.SavedImagePath = Path.Combine(root, "images", "after.png");
            result.OutputImageBytes[4] = 0xFF;

            var queued = ReadQueuedResult(writer);
            queued.SavedImagePath.Should().EndWith(Path.Combine("images", "before.png"));
            queued.OutputImageBytes.Should().Equal([0x89, 0x50, 0x4E, 0x47, 0x01]);
        }
        finally
        {
            consumer.TrySetResult(null);
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ImageWriter_ShouldUseSourceImage_WhenOutputImageIsEmpty()
    {
        var root = CreateTempDirectory();
        var profile = new RuntimeProfile
        {
            ImageQueueCapacity = 2
        };
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeImageWriter",
            root,
            profile,
            NullLogger.Instance);
        var sourceImage = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x42 };
        var result = BuildResult(1);
        result.OutputImageBytes = [];
        result.SourceImageBytes = sourceImage;
        result.SavedImagePath = PlanImagePath(writer, result);

        try
        {
            result.SavedImagePath.Should().EndWith(".png");
            (await EnqueueAsync(writer, result)).Should().BeTrue();
            await ((IAsyncDisposable)writer).DisposeAsync();

            var savedPath = Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories).Single();
            File.ReadAllBytes(savedPath).Should().Equal(sourceImage);
        }
        finally
        {
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ImageWriter_ShouldSkipQueue_WhenNoImageBytesAreAvailable()
    {
        var root = CreateTempDirectory();
        var consumer = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = new RuntimeProfile
        {
            ImageQueueCapacity = 2
        };
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeImageWriter",
            root,
            profile,
            NullLogger.Instance,
            TimeSpan.FromSeconds(5),
            consumer.Task);
        var result = BuildResult(1);
        result.SavedImagePath = Path.Combine(root, "images", "missing.bin");
        result.OutputImageBytes = [];
        result.SourceImageBytes = null;

        try
        {
            (await EnqueueAsync(writer, result)).Should().BeFalse();
            TryReadQueuedResult(writer, out _).Should().BeFalse();
            ReadIntProperty(writer, "DroppedCount").Should().Be(0);
        }
        finally
        {
            consumer.TrySetResult(null);
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ResultRecordWriter_DisposeAsync_ShouldReturnWhenConsumerDoesNotFinish()
    {
        var root = CreateTempDirectory();
        var consumer = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeResultRecordWriter",
            root,
            1,
            NullLogger.Instance,
            TimeSpan.FromMilliseconds(100),
            consumer.Task);

        try
        {
            await EnqueueAsync(writer, BuildResult(1));

            var stopwatch = Stopwatch.StartNew();
            await ((IAsyncDisposable)writer).DisposeAsync();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task ImageWriter_DisposeAsync_ShouldReturnWhenConsumerDoesNotFinish()
    {
        var root = CreateTempDirectory();
        var consumer = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var profile = new RuntimeProfile
        {
            ImageQueueCapacity = 1
        };
        var writer = CreateInternalWriter(
            "Acme.Product.Runtime.RuntimeImageWriter",
            root,
            profile,
            NullLogger.Instance,
            TimeSpan.FromMilliseconds(100),
            consumer.Task);

        try
        {
            var result = BuildResult(1);
            result.SavedImagePath = Path.Combine(root, "images", "01.png");
            result.OutputImageBytes = [0x89, 0x50, 0x4E, 0x47, 0x01];
            await EnqueueAsync(writer, result);

            var stopwatch = Stopwatch.StartNew();
            await ((IAsyncDisposable)writer).DisposeAsync();
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await DisposeIfNeededAsync(writer);
            DeleteTempDirectory(root);
        }
    }

    private static object CreateInternalWriter(string typeName, params object[] arguments)
    {
        var type = typeof(RuntimeHost).Assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(type, arguments)!;
    }

    private static async Task<bool> EnqueueAsync(object writer, RuntimeNormalizedResult result)
    {
        var method = writer.GetType().GetMethod("EnqueueAsync")!;
        var valueTask = (ValueTask<bool>)method.Invoke(writer, [result, CancellationToken.None])!;
        return await valueTask;
    }

    private static string PlanImagePath(object writer, RuntimeNormalizedResult result)
    {
        return (string)writer.GetType().GetMethod("PlanPath")!.Invoke(writer, [result])!;
    }

    private static int ReadIntProperty(object instance, string propertyName)
    {
        return (int)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;
    }

    private static RuntimeNormalizedResult ReadQueuedResult(object writer)
    {
        TryReadQueuedResult(writer, out var result).Should().BeTrue();
        return result!;
    }

    private static bool TryReadQueuedResult(object writer, out RuntimeNormalizedResult? result)
    {
        var channel = (Channel<RuntimeNormalizedResult>)writer
            .GetType()
            .GetField("_channel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(writer)!;

        return channel.Reader.TryRead(out result);
    }

    private static async Task DisposeIfNeededAsync(object writer)
    {
        try
        {
            await ((IAsyncDisposable)writer).DisposeAsync();
        }
        catch
        {
        }
    }

    private static RuntimeNormalizedResult BuildResult(int index)
    {
        var now = DateTimeOffset.UtcNow;
        return new RuntimeNormalizedResult
        {
            RunId = $"run-{index:D3}",
            PackageId = "pkg-1",
            PackageName = "Package 1",
            FlowHash = "sha256:test",
            ImageId = $"image-{index:D3}",
            Outcome = RuntimeRunOutcome.Ng,
            InspectionStatus = Acme.Product.Core.Enums.InspectionStatus.NG,
            ExecutionTimeMs = index,
            DiagnosticCode = "NG",
            HasJudgmentSignal = true,
            StartedAtUtc = now.AddMilliseconds(-index),
            CompletedAtUtc = now,
            PrimaryOutputs = new Dictionary<string, object?>
            {
                ["JudgmentResult"] = "NG",
                ["Score"] = index
            },
            SourceImageBytes = [(byte)index]
        };
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClearVisionRuntimeWriterTests", Guid.NewGuid().ToString("N"));
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
