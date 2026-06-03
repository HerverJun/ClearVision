using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

var options = RunnerOptions.Parse(args);
if (options.ShowHelp)
{
    RunnerOptions.PrintHelp();
    return options.ParseError is null ? 0 : 2;
}

if (options.ParseError is not null)
{
    Console.Error.WriteLine(options.ParseError);
    RunnerOptions.PrintHelp();
    return 2;
}

var result = await ContractRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"ForEach contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "ForEach";

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = BuildCases();
        var results = new List<CaseResult>();

        foreach (var testCase in cases)
        {
            results.Add(await RunCaseAsync(testCase));
        }

        var byOperator = results
            .GroupBy(item => item.Operator)
            .Select(group => new OperatorSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                (long)Math.Round(group.Average(item => item.MemoryAllocationBytes))))
            .ToList();

        var byScenario = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToList();

        return new BaselineResult(
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            byOperator,
            byScenario,
            results);
    }

    private static List<ContractCase> BuildCases()
    {
        return new List<ContractCase>
        {
            // Basic execution (5)
            ForEachCase("empty_items", new List<object>(), "Parallel", expectedCount: 0, expectedAllPass: true),
            ForEachCase("single_item_parallel", new List<object> { 1 }, "Parallel", expectedCount: 1, expectedAllPass: true, expectedAllSucceeded: true),
            ForEachCase("single_item_sequential", new List<object> { "a" }, "Sequential", expectedCount: 1, expectedAllPass: true, expectedAllSucceeded: true),
            ForEachCase("three_items_parallel_all_pass", new List<object> { 1, 2, 3 }, "Parallel", expectedCount: 3, expectedAllPass: true, expectedAllSucceeded: true),
            ForEachCase("three_items_sequential_all_pass", new List<object> { 1, 2, 3 }, "Sequential", expectedCount: 3, expectedAllPass: true, expectedAllSucceeded: true),

            // FailFast behavior (4)
            ForEachCase("five_items_parallel_all_pass", new List<object> { 1, 2, 3, 4, 5 }, "Parallel", expectedCount: 5, expectedAllPass: true, expectedAllSucceeded: true),
            ForEachCase("failfast_true_sequential_first_fails", new List<object> { 1, 2, 3 }, "Sequential", failFast: true,
                failIndices: new HashSet<int> { 0 }, expectedCount: 1, expectedAllPass: false, expectedAllSucceeded: false),
            ForEachCase("failfast_false_parallel_mixed", new List<object> { 1, 2, 3, 4 }, "Parallel", failFast: false,
                failIndices: new HashSet<int> { 1, 3 }, expectedCount: 4, expectedPassCount: 2, expectedAllPass: false, expectedAllSucceeded: false),
            ForEachCase("failfast_false_sequential_mixed", new List<object> { 1, 2, 3, 4 }, "Sequential", failFast: false,
                failIndices: new HashSet<int> { 1, 3 }, expectedCount: 4, expectedPassCount: 2, expectedAllPass: false, expectedAllSucceeded: false),

            // Output contract (2)
            OutputContractCase("output_keys_parallel", new List<object> { 1 }, "Parallel"),
            OutputContractCase("output_keys_sequential", new List<object> { 1 }, "Sequential"),

            // Max parallelism (1)
            ForEachCase("max_parallelism_one", new List<object> { 1, 2, 3 }, "Parallel", maxParallelism: 1,
                expectedCount: 3, expectedAllPass: true, expectedAllSucceeded: true),

            // Error handling (2)
            ErrorCase("null_items_fails", null, "Parallel"),
            ErrorCase("non_enumerable_fails", "not-a-list", "Parallel"),

            // Validation (5)
            ValidationCase("validate_invalid_iomode", expectedValid: false, ioMode: "BadMode"),
            ValidationCase("validate_maxparallelism_too_low", expectedValid: false, maxParallelism: 0),
            ValidationCase("validate_maxparallelism_too_high", expectedValid: false, maxParallelism: 65),
            ValidationCase("validate_timeout_too_low", expectedValid: false, timeout: 500),
            ValidationCase("validate_timeout_too_high", expectedValid: false, timeout: 400000),
            ValidationCase("validate_valid_params", expectedValid: true, ioMode: "Parallel", maxParallelism: 4, timeout: 30000),
        };
    }

    private static ContractCase ForEachCase(
        string caseId,
        List<object>? items,
        string ioMode,
        int? maxParallelism = null,
        bool? failFast = null,
        HashSet<int>? failIndices = null,
        int? expectedCount = null,
        int? expectedPassCount = null,
        bool? expectedAllPass = null,
        bool? expectedAllSucceeded = null)
    {
        return new ContractCase(caseId, OperatorName, ioMode, async () =>
        {
            var serviceProvider = CreateServiceProvider(failIndices ?? new HashSet<int>());
            var op = CreateOperator(ioMode, maxParallelism, failFast);
            var inputs = items != null ? new Dictionary<string, object> { ["Items"] = items } : new Dictionary<string, object>();
            var executor = new ForEachOperator(NullLogger<ForEachOperator>.Instance, serviceProvider)
            {
                SubGraph = new OperatorFlow()
            };

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = await executor.ExecuteAsync(op, inputs, CancellationToken.None);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var eval = EvaluateForEach(output, expectedCount, expectedPassCount, expectedAllPass, expectedAllSucceeded);
            var metrics = new Dictionary<string, object>
            {
                ["IoMode"] = ioMode,
                ["Count"] = output.OutputData?.GetValueOrDefault("Count")?.ToString() ?? "null",
                ["PassCount"] = output.OutputData?.GetValueOrDefault("PassCount")?.ToString() ?? "null",
                ["AllPass"] = output.OutputData?.GetValueOrDefault("AllPass")?.ToString() ?? "null",
                ["AllSucceeded"] = output.OutputData?.GetValueOrDefault("AllSucceeded")?.ToString() ?? "null",
                ["Passed"] = eval.Passed,
            };

            return new CaseRunResult(
                caseId, OperatorName, ioMode, eval.Passed, eval.FailureMessage,
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics);
        });
    }

    private static ContractCase OutputContractCase(string caseId, List<object> items, string ioMode)
    {
        return new ContractCase(caseId, OperatorName, "Output contract", async () =>
        {
            var serviceProvider = CreateServiceProvider(new HashSet<int>());
            var op = CreateOperator(ioMode);
            var inputs = new Dictionary<string, object> { ["Items"] = items };
            var executor = new ForEachOperator(NullLogger<ForEachOperator>.Instance, serviceProvider)
            {
                SubGraph = new OperatorFlow()
            };

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = await executor.ExecuteAsync(op, inputs, CancellationToken.None);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var requiredKeys = new[] { "Results", "Count", "PassCount", "AllPass", "SuccessCount", "FailureCount", "AllSucceeded" };
            var data = output.OutputData ?? new Dictionary<string, object>();
            var missing = requiredKeys.Where(k => !data.ContainsKey(k)).ToList();
            var passed = output.IsSuccess && missing.Count == 0;

            var metrics = new Dictionary<string, object>
            {
                ["RequiredKeys"] = string.Join(", ", requiredKeys),
                ["MissingKeys"] = string.Join(", ", missing),
                ["Passed"] = passed,
            };

            return new CaseRunResult(
                caseId, OperatorName, "Output contract", passed,
                passed ? null : $"Missing keys: {string.Join(", ", missing)}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics);
        });
    }

    private static ContractCase ErrorCase(string caseId, object? items, string ioMode)
    {
        return new ContractCase(caseId, OperatorName, "Error handling", async () =>
        {
            var serviceProvider = CreateServiceProvider(new HashSet<int>());
            var op = CreateOperator(ioMode);
            var inputs = items != null ? new Dictionary<string, object> { ["Items"] = items } : new Dictionary<string, object>();
            var executor = new ForEachOperator(NullLogger<ForEachOperator>.Instance, serviceProvider)
            {
                SubGraph = new OperatorFlow()
            };

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = await executor.ExecuteAsync(op, inputs, CancellationToken.None);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var passed = !output.IsSuccess;
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedFailure"] = true,
                ["ActualIsSuccess"] = output.IsSuccess,
                ["ErrorMessage"] = output.ErrorMessage ?? string.Empty,
                ["Passed"] = passed,
            };

            return new CaseRunResult(
                caseId, OperatorName, "Error handling", passed,
                passed ? null : $"Expected failure, got success={output.IsSuccess}, msg={output.ErrorMessage}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics);
        });
    }

    private static ContractCase ValidationCase(string caseId, bool expectedValid, string? ioMode = null, int? maxParallelism = null, int? timeout = null)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(ioMode ?? "Parallel", maxParallelism, null, timeout);
            var serviceProvider = CreateServiceProvider(new HashSet<int>());
            var executor = new ForEachOperator(NullLogger<ForEachOperator>.Instance, serviceProvider);
            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var validation = executor.ValidateParameters(op);
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var passed = validation.IsValid == expectedValid;
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedValid"] = expectedValid,
                ["ActualValid"] = validation.IsValid,
                ["Errors"] = string.Join("; ", validation.Errors),
                ["Passed"] = passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, "Validation contract", passed,
                passed ? null : $"Expected IsValid={expectedValid}, got {validation.IsValid}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static Operator CreateOperator(string ioMode, int? maxParallelism = null, bool? failFast = null, int? timeout = null)
    {
        var op = new Operator(OperatorName, OperatorType.ForEach, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "IoMode", "IoMode", string.Empty, "string", ioMode));
        if (maxParallelism.HasValue)
            op.AddParameter(new Parameter(Guid.NewGuid(), "MaxParallelism", "MaxParallelism", string.Empty, "int", maxParallelism.Value));
        if (failFast.HasValue)
            op.AddParameter(new Parameter(Guid.NewGuid(), "FailFast", "FailFast", string.Empty, "bool", failFast.Value));
        if (timeout.HasValue)
            op.AddParameter(new Parameter(Guid.NewGuid(), "Timeout", "Timeout", string.Empty, "int", timeout.Value));
        return op;
    }

    private static IServiceProvider CreateServiceProvider(HashSet<int> failIndices)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IFlowExecutionService>(new MockFlowExecutionService(failIndices));
        return services.BuildServiceProvider();
    }

    private static (bool Passed, string? FailureMessage) EvaluateForEach(
        OperatorExecutionOutput output,
        int? expectedCount,
        int? expectedPassCount,
        bool? expectedAllPass,
        bool? expectedAllSucceeded)
    {
        if (!output.IsSuccess)
            return (false, $"Execution failed: {output.ErrorMessage}");

        var data = output.OutputData!;

        if (expectedCount.HasValue)
        {
            var actual = data.GetValueOrDefault("Count") is int i ? i : Convert.ToInt32(data.GetValueOrDefault("Count"));
            if (actual != expectedCount.Value)
                return (false, $"Count mismatch: expected {expectedCount}, got {actual}");
        }
        if (expectedPassCount.HasValue)
        {
            var actual = data.GetValueOrDefault("PassCount") is int i ? i : Convert.ToInt32(data.GetValueOrDefault("PassCount"));
            if (actual != expectedPassCount.Value)
                return (false, $"PassCount mismatch: expected {expectedPassCount}, got {actual}");
        }
        if (expectedAllPass.HasValue)
        {
            var actual = data.GetValueOrDefault("AllPass") is bool b && b;
            if (actual != expectedAllPass.Value)
                return (false, $"AllPass mismatch: expected {expectedAllPass}, got {actual}");
        }
        if (expectedAllSucceeded.HasValue)
        {
            var actual = data.GetValueOrDefault("AllSucceeded") is bool b && b;
            if (actual != expectedAllSucceeded.Value)
                return (false, $"AllSucceeded mismatch: expected {expectedAllSucceeded}, got {actual}");
        }

        return (true, null);
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase testCase)
    {
        var runResult = await testCase.RunAsync();
        return new CaseResult(
            runResult.CaseId,
            runResult.Operator,
            runResult.Scenario,
            runResult.Passed,
            runResult.FailureMessage,
            runResult.RuntimeMs,
            runResult.MemoryAllocationBytes,
            runResult.Metrics);
    }
}

internal sealed class MockFlowExecutionService : IFlowExecutionService
{
    private readonly HashSet<int> _failIndices;

    public MockFlowExecutionService(HashSet<int> failIndices)
    {
        _failIndices = failIndices;
    }

    public Task<FlowExecutionResult> ExecuteFlowAsync(OperatorFlow flow, Dictionary<string, object>? inputData = null, bool enableParallel = false, CancellationToken cancellationToken = default)
    {
        var index = inputData?.GetValueOrDefault("CurrentIndex") is int i ? i : 0;
        var shouldFail = _failIndices.Contains(index);

        return Task.FromResult(new FlowExecutionResult
        {
            IsSuccess = !shouldFail,
            OutputData = new Dictionary<string, object> { ["Result"] = !shouldFail },
            OperatorResults = new List<OperatorExecutionResult>()
        });
    }

    public Task<OperatorExecutionResult> ExecuteOperatorAsync(Operator @operator, Dictionary<string, object>? inputs = null)
        => throw new NotImplementedException();

    public FlowValidationResult ValidateFlow(OperatorFlow flow)
        => new() { IsValid = true };

    public FlowExecutionStatus? GetExecutionStatus(Guid flowId)
        => null;

    public Task CancelExecutionAsync(Guid flowId)
        => Task.CompletedTask;

    public Task<FlowDebugExecutionResult> ExecuteFlowDebugAsync(OperatorFlow flow, DebugOptions options, Dictionary<string, object>? inputData = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Dictionary<string, object>? GetDebugIntermediateResult(Guid debugSessionId, Guid operatorId)
        => null;

    public Task ClearDebugCacheAsync(Guid debugSessionId)
        => Task.CompletedTask;
}

internal sealed record ContractCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<Task<CaseRunResult>> RunAsync);

internal sealed record CaseRunResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    string? FailureMessage,
    double RuntimeMs,
    long MemoryAllocationBytes,
    Dictionary<string, object> Metrics);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    List<OperatorSummary> Operators,
    List<ScenarioSummary> Scenarios,
    List<CaseResult> Cases);

internal sealed record BaselineSummary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed, double RuntimeMs);
internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg);
internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    string? FailureMessage,
    double RuntimeMs,
    long MemoryAllocationBytes,
    Dictionary<string, object> Metrics);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# ForEach Contract Baseline Report");
        sb.AppendLine();
        sb.AppendLine($"- **Generated**: {result.Summary.GeneratedAtUtc:O}");
        sb.AppendLine($"- **Total Cases**: {result.Summary.CaseCount}");
        sb.AppendLine($"- **Passed**: {result.Summary.Passed}");
        sb.AppendLine($"- **Failed**: {result.Summary.Failed}");
        sb.AppendLine($"- **Total Runtime**: {result.Summary.RuntimeMs} ms");
        sb.AppendLine();
        sb.AppendLine("| CaseId | Scenario | Passed | Runtime (ms) | Memory (bytes) | Notes |");
        sb.AppendLine("|--------|----------|--------|--------------|----------------|-------|");
        foreach (var c in result.Cases)
        {
            sb.AppendLine($"| {c.CaseId} | {c.Scenario} | {(c.Passed ? "PASS" : "FAIL")} | {c.RuntimeMs:F3} | {c.MemoryAllocationBytes} | {c.FailureMessage ?? ""} |");
        }
        return sb.ToString();
    }
}

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        string? outputPath = null;
        string? reportPath = null;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output":
                case "-o":
                    if (i + 1 < args.Length) outputPath = args[++i];
                    break;
                case "--report":
                case "-r":
                    if (i + 1 < args.Length) reportPath = args[++i];
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
            }
        }

        if (showHelp)
            return new RunnerOptions(outputPath ?? "", reportPath, true, null);

        outputPath ??= "quality/evals/reports/ForEach_baseline.json";
        return new RunnerOptions(outputPath, reportPath, false, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: ForEachContractRunner [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>   JSON output path (default: quality/evals/reports/ForEach_baseline.json)");
        Console.WriteLine("  -r, --report <path>   Markdown report path");
        Console.WriteLine("  -h, --help            Show this help");
    }
}
