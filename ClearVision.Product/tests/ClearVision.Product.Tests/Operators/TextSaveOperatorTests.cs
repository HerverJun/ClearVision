using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
[Trait("Category", "Sprint6_Phase3")]
public class TextSaveOperatorTests
{
    [Fact]
    public void OperatorType_ShouldBeTextSave()
    {
        var sut = CreateSut();
        Assert.Equal(OperatorType.TextSave, sut.OperatorType);
    }

    [Fact]
    public async Task ExecuteAsync_TextMode_ShouldWriteFile()
    {
        var sut = CreateSut();
        var path = Path.Combine(Path.GetTempPath(), $"cv_textsave_{Guid.NewGuid():N}.txt");
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FilePath", path },
            { "Format", "Text" },
            { "AppendMode", false },
            { "AddTimestamp", false },
            { "Encoding", "UTF8" }
        });

        var inputs = new Dictionary<string, object> { { "Text", "hello phase3" } };
        var result = await sut.ExecuteAsync(op, inputs);

        try
        {
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.OutputData);
            Assert.True((bool)result.OutputData!["Success"]);
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("hello phase3", text);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ValidateParameters_WithInvalidFormat_ShouldReturnInvalid()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FilePath", "a.txt" },
            { "Format", "XML" }
        });
        Assert.False(sut.ValidateParameters(op).IsValid);
    }

    [Fact]
    public async Task PendingFilePath_ShouldFailValidationAndExecuteWithoutWriting()
    {
        var sut = CreateSut();
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FilePath", "<pending-output-file>" },
            { "Format", "Text" },
            { "AppendMode", false },
            { "AddTimestamp", false }
        });

        Assert.False(sut.ValidateParameters(op).IsValid);
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Text"] = "must-not-write" });

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(Path.GetFullPath("<pending-output-file>")));
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentAppend_ShouldNotLoseLines()
    {
        var sut = CreateSut();
        var path = Path.Combine(Path.GetTempPath(), $"cv_textsave_parallel_{Guid.NewGuid():N}.txt");
        var op = CreateOperator(new Dictionary<string, object>
        {
            { "FilePath", path },
            { "Format", "Text" },
            { "AppendMode", true },
            { "AddTimestamp", false },
            { "Encoding", "UTF8" }
        });

        try
        {
            var tasks = Enumerable.Range(0, 20)
                .Select(i => sut.ExecuteAsync(op, new Dictionary<string, object> { { "Text", $"line-{i}" } }))
                .ToArray();

            var results = await Task.WhenAll(tasks);
            Assert.All(results, r => Assert.True(r.IsSuccess));

            var lines = File.ReadAllLines(path);
            Assert.Equal(20, lines.Length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FileLockStripes_AfterTenThousandUniquePaths_ShouldRemainAtHardCapacity()
    {
        var baseline = TextSaveOperator.RetainedFileLockCount;

        for (var index = 0; index < 10_000; index++)
        {
            var uniquePath = Path.Combine(Path.GetTempPath(), $"cv-text-lock-{index:D5}.txt");
            await TextSaveOperator.ExecuteWithFileLockAsync(
                uniquePath,
                static _ => Task.CompletedTask);
        }

        Assert.InRange(baseline, 1, 256);
        Assert.Equal(baseline, TextSaveOperator.RetainedFileLockCount);
    }

    [Fact]
    public async Task ExecuteWithFileLockAsync_SameCanonicalPath_ShouldSerializeAllCallers()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cv-text-lock-serial-{Guid.NewGuid():N}.txt");
        var aliasPath = Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maxActive = 0;

        var tasks = Enumerable.Range(0, 32)
            .Select(index => Task.Run(async () =>
            {
                await start.Task;
                var selectedPath = index % 2 == 0 ? path : aliasPath;
                await TextSaveOperator.ExecuteWithFileLockAsync(
                    selectedPath,
                    async cancellationToken =>
                    {
                        var current = Interlocked.Increment(ref active);
                        UpdateMaximum(ref maxActive, current);
                        await Task.Delay(2, cancellationToken);
                        Interlocked.Decrement(ref active);
                    });
            }))
            .ToArray();

        start.TrySetResult();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, maxActive);
    }

    private static TextSaveOperator CreateSut()
    {
        return new TextSaveOperator(Substitute.For<ILogger<TextSaveOperator>>());
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("TextSave", OperatorType.TextSave, 0, 0);
        if (parameters != null)
        {
            foreach (var (k, v) in parameters)
            {
                op.AddParameter(new Parameter(Guid.NewGuid(), k, k, string.Empty, "string", v));
            }
        }

        return op;
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }
}
