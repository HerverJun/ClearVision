using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
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
    $"TimerStatistics contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "TimerStatistics";
    private const int CallSpacingMs = 25;

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
            // SingleShot scenario (3)
            ExecutionCase(
                "singleshot_first_call_zero",
                "SingleShot",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedElapsedMin: 0.0, ExpectedElapsedMax: 0.0, ExpectedTotalEqualsElapsed: true, ExpectedAverageEqualsElapsed: true),
                }),
            ExecutionCase(
                "singleshot_second_call_positive",
                "SingleShot",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedElapsedMin: 0.0, ExpectedElapsedMax: 0.0, ExpectedTotalEqualsElapsed: true, ExpectedAverageEqualsElapsed: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 1, ExpectedElapsedMin: CallSpacingMs / 2.0, ExpectedElapsedMax: 5000.0, ExpectedTotalEqualsElapsed: true, ExpectedAverageEqualsElapsed: true),
                }),
            ExecutionCase(
                "singleshot_default_mode",
                "SingleShot",
                mode: null,
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedElapsedMin: 0.0, ExpectedElapsedMax: 0.0, ExpectedTotalEqualsElapsed: true, ExpectedAverageEqualsElapsed: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 1, ExpectedElapsedMin: CallSpacingMs / 2.0, ExpectedElapsedMax: 5000.0, ExpectedTotalEqualsElapsed: true, ExpectedAverageEqualsElapsed: true),
                }),

            // Cumulative scenario (3)
            ExecutionCase(
                "cumulative_first_call_count_one",
                "Cumulative",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedElapsedMin: 0.0, ExpectedElapsedMax: 0.0, ExpectedTotalEqualsElapsed: false, ExpectedAverageEqualsElapsed: false, ExpectedTotal: 0.0, ExpectedAverage: 0.0),
                }),
            ExecutionCase(
                "cumulative_second_call_count_two",
                "Cumulative",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedTotal: 0.0, ExpectedAverage: 0.0),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2, ExpectedAverageEqualsTotalOverCount: true),
                }),
            ExecutionCase(
                "cumulative_three_calls_count_three",
                "Cumulative",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2, ExpectedAverageEqualsTotalOverCount: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 3, ExpectedAverageEqualsTotalOverCount: true),
                }),

            // Average correctness (1)
            ExecutionCase(
                "cumulative_average_equals_total_over_count",
                "Average correctness",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectedAverageEqualsTotalOverCount: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2, ExpectedAverageEqualsTotalOverCount: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 3, ExpectedAverageEqualsTotalOverCount: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 4, ExpectedAverageEqualsTotalOverCount: true),
                }),

            // Reset Interval scenario (3)
            ExecutionCase(
                "reset_interval_zero_no_reset",
                "Reset interval",
                mode: "Cumulative",
                resetInterval: 0,
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 3),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 4),
                }),
            ExecutionCase(
                "reset_interval_two_resets",
                "Reset interval",
                mode: "Cumulative",
                resetInterval: 2,
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 1),
                }),
            ExecutionCase(
                "reset_interval_three_resets",
                "Reset interval",
                mode: "Cumulative",
                resetInterval: 3,
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 3),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 1),
                }),

            // Output contract scenario (3)
            ExecutionCase(
                "output_keys_singleshot",
                "Output contract",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, RequireAllKeys: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 1, RequireAllKeys: true),
                }),
            ExecutionCase(
                "output_keys_cumulative",
                "Output contract",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, RequireAllKeys: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2, RequireAllKeys: true),
                }),
            ExecutionCase(
                "output_no_trigger_when_absent",
                "Output contract",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, ExpectTriggerAbsent: true),
                }),

            // Trigger passthrough scenario (2)
            ExecutionCase(
                "trigger_passthrough_string",
                "Trigger passthrough",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, TriggerInput: "hello", ExpectedTriggerValue: "hello"),
                }),
            ExecutionCase(
                "trigger_passthrough_int",
                "Trigger passthrough",
                mode: "SingleShot",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, TriggerInput: 42, ExpectedTriggerValue: 42),
                }),

            // Mode case-insensitive scenario (2)
            ExecutionCase(
                "mode_lowercase_cumulative",
                "Mode parsing",
                mode: "cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2),
                }),
            ExecutionCase(
                "mode_uppercase_cumulative",
                "Mode parsing",
                mode: "CUMULATIVE",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2),
                }),

            // Numeric finiteness scenario (1)
            ExecutionCase(
                "numeric_outputs_finite",
                "Numeric finiteness",
                mode: "Cumulative",
                steps: new[]
                {
                    new ExecutionStep(0, ExpectedCount: 1, RequireFiniteOutputs: true),
                    new ExecutionStep(CallSpacingMs, ExpectedCount: 2, RequireFiniteOutputs: true),
                }),

            // Validation contract (5)
            ValidationCase("validate_default_mode_ok", expectedValid: true),
            ValidationCase("validate_singleshot_ok", expectedValid: true, mode: "SingleShot"),
            ValidationCase("validate_cumulative_ok", expectedValid: true, mode: "Cumulative"),
            ValidationCase("validate_invalid_mode", expectedValid: false, mode: "BadMode"),
            ValidationCase("validate_reset_interval_negative", expectedValid: false, resetInterval: -1),
        };
    }

    private static ContractCase ExecutionCase(
        string caseId,
        string scenario,
        string? mode,
        ExecutionStep[] steps,
        int resetInterval = 0)
    {
        return new ContractCase(caseId, OperatorName, scenario, async () =>
        {
            var op = CreateOperator(mode, resetInterval);
            var executor = new TimerStatisticsOperator(NullLogger<TimerStatisticsOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);

            var stepResults = new List<StepResult>();
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step.DelayMs > 0)
                {
                    await Task.Delay(step.DelayMs);
                }

                var inputs = new Dictionary<string, object>();
                if (step.TriggerInput is not null)
                {
                    inputs["Trigger"] = step.TriggerInput;
                }

                var execution = await executor.ExecuteAsync(op, inputs);
                stepResults.Add(EvaluateStep(execution, step));
            }

            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var allPassed = stepResults.All(r => r.Passed);
            var metrics = new Dictionary<string, object>
            {
                ["StepCount"] = steps.Length,
                ["StepsPassed"] = stepResults.Count(r => r.Passed),
                ["StepsFailed"] = stepResults.Count(r => !r.Passed),
                ["FinalCount"] = stepResults.Count > 0 ? stepResults[^1].ActualCount : 0,
                ["FinalElapsedMs"] = stepResults.Count > 0 ? stepResults[^1].ActualElapsed : 0.0,
                ["FinalTotalMs"] = stepResults.Count > 0 ? stepResults[^1].ActualTotal : 0.0,
                ["FinalAverageMs"] = stepResults.Count > 0 ? stepResults[^1].ActualAverage : 0.0,
                ["Passed"] = allPassed,
            };

            return new CaseRunResult(
                allPassed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                allPassed ? null : FormatStepFailures(stepResults),
                metrics);
        });
    }

    private static ContractCase ValidationCase(
        string caseId,
        bool expectedValid,
        string? mode = null,
        int resetInterval = 0)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(mode, resetInterval);
            var executor = new TimerStatisticsOperator(NullLogger<TimerStatisticsOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var validation = executor.ValidateParameters(op);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var passed = validation.IsValid == expectedValid;
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedValid"] = expectedValid,
                ["ActualValid"] = validation.IsValid,
                ["ErrorMessage"] = string.Join("; ", validation.Errors),
                ["Passed"] = passed,
            };

            return Task.FromResult(new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : $"Expected validation={expectedValid}, got {validation.IsValid} ({string.Join("; ", validation.Errors)})",
                metrics));
        });
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase testCase)
    {
        try
        {
            var run = await testCase.RunAsync();
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                run.Passed,
                run.RuntimeMs,
                run.MemoryAllocationBytes,
                run.ErrorMessage,
                run.Metrics);
        }
        catch (Exception ex)
        {
            return new CaseResult(
                testCase.CaseId,
                testCase.Operator,
                testCase.Scenario,
                false,
                0,
                0,
                $"{ex.GetType().Name}: {ex.Message}",
                new Dictionary<string, object>());
        }
    }

    private static Operator CreateOperator(string? mode, int resetInterval)
    {
        var op = new Operator("timer_contract", OperatorType.TimerStatistics, 0, 0);
        if (mode is not null)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), "Mode", "Mode", string.Empty, "string", mode));
        }
        op.AddParameter(new Parameter(Guid.NewGuid(), "ResetInterval", "Reset Interval", string.Empty, "int", resetInterval));
        return op;
    }

    private static StepResult EvaluateStep(OperatorExecutionOutput execution, ExecutionStep step)
    {
        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return new StepResult(false, $"Execution failed: {execution.ErrorMessage}", 0, 0, 0, 0);
        }

        var output = execution.OutputData;
        var elapsed = TryGetDouble(output, "ElapsedMs");
        var total = TryGetDouble(output, "TotalMs");
        var average = TryGetDouble(output, "AverageMs");
        var count = TryGetInt(output, "Count");

        var failures = new List<string>();

        if (count != step.ExpectedCount)
        {
            failures.Add($"Count expected={step.ExpectedCount} actual={count}");
        }

        if (step.ExpectedElapsedMin.HasValue && elapsed < step.ExpectedElapsedMin.Value)
        {
            failures.Add($"ElapsedMs={elapsed} below {step.ExpectedElapsedMin.Value}");
        }

        if (step.ExpectedElapsedMax.HasValue && elapsed > step.ExpectedElapsedMax.Value)
        {
            failures.Add($"ElapsedMs={elapsed} above {step.ExpectedElapsedMax.Value}");
        }

        if (step.ExpectedTotal.HasValue && Math.Abs(total - step.ExpectedTotal.Value) > 1e-9)
        {
            failures.Add($"TotalMs expected={step.ExpectedTotal.Value} actual={total}");
        }

        if (step.ExpectedAverage.HasValue && Math.Abs(average - step.ExpectedAverage.Value) > 1e-9)
        {
            failures.Add($"AverageMs expected={step.ExpectedAverage.Value} actual={average}");
        }

        if (step.ExpectedTotalEqualsElapsed && Math.Abs(total - elapsed) > 1e-9)
        {
            failures.Add($"TotalMs={total} not equal ElapsedMs={elapsed}");
        }

        if (step.ExpectedAverageEqualsElapsed && Math.Abs(average - elapsed) > 1e-9)
        {
            failures.Add($"AverageMs={average} not equal ElapsedMs={elapsed}");
        }

        if (step.ExpectedAverageEqualsTotalOverCount)
        {
            var expected = count > 0 ? total / count : 0;
            if (Math.Abs(average - expected) > 1e-9)
            {
                failures.Add($"AverageMs={average} not equal TotalMs/Count={expected}");
            }
        }

        if (step.RequireAllKeys)
        {
            var requiredKeys = new[] { "ElapsedMs", "TotalMs", "AverageMs", "Count" };
            foreach (var key in requiredKeys)
            {
                if (!output.ContainsKey(key))
                {
                    failures.Add($"Missing key {key}");
                }
            }
        }

        if (step.RequireFiniteOutputs)
        {
            if (!double.IsFinite(elapsed))
            {
                failures.Add($"ElapsedMs not finite: {elapsed}");
            }

            if (!double.IsFinite(total))
            {
                failures.Add($"TotalMs not finite: {total}");
            }

            if (!double.IsFinite(average))
            {
                failures.Add($"AverageMs not finite: {average}");
            }
        }

        if (step.ExpectTriggerAbsent && output.ContainsKey("Trigger"))
        {
            failures.Add("Trigger key should be absent");
        }

        if (step.ExpectedTriggerValue is not null)
        {
            if (!output.TryGetValue("Trigger", out var trigger))
            {
                failures.Add("Trigger key missing");
            }
            else if (!Equals(trigger, step.ExpectedTriggerValue))
            {
                failures.Add($"Trigger expected={step.ExpectedTriggerValue} actual={trigger}");
            }
        }

        var passed = failures.Count == 0;
        return new StepResult(
            passed,
            passed ? null : string.Join("; ", failures),
            elapsed,
            total,
            average,
            count);
    }

    private static double TryGetDouble(IReadOnlyDictionary<string, object> output, string key)
    {
        if (!output.TryGetValue(key, out var raw))
        {
            return double.NaN;
        }

        try
        {
            return Convert.ToDouble(raw);
        }
        catch
        {
            return double.NaN;
        }
    }

    private static int TryGetInt(IReadOnlyDictionary<string, object> output, string key)
    {
        if (!output.TryGetValue(key, out var raw))
        {
            return -1;
        }

        try
        {
            return Convert.ToInt32(raw);
        }
        catch
        {
            return -1;
        }
    }

    private static string FormatStepFailures(IReadOnlyList<StepResult> results)
    {
        var pieces = new List<string>();
        for (var i = 0; i < results.Count; i++)
        {
            if (!results[i].Passed)
            {
                pieces.Add($"step[{i}]: {results[i].FailureMessage}");
            }
        }

        return string.Join(" | ", pieces);
    }
}

internal sealed record ExecutionStep(
    int DelayMs,
    int ExpectedCount,
    double? ExpectedElapsedMin = null,
    double? ExpectedElapsedMax = null,
    double? ExpectedTotal = null,
    double? ExpectedAverage = null,
    bool ExpectedTotalEqualsElapsed = false,
    bool ExpectedAverageEqualsElapsed = false,
    bool ExpectedAverageEqualsTotalOverCount = false,
    bool RequireAllKeys = false,
    bool RequireFiniteOutputs = false,
    bool ExpectTriggerAbsent = false,
    object? TriggerInput = null,
    object? ExpectedTriggerValue = null);

internal sealed record StepResult(
    bool Passed,
    string? FailureMessage,
    double ActualElapsed,
    double ActualTotal,
    double ActualAverage,
    int ActualCount);

internal sealed record ContractCase(
    string CaseId,
    string Operator,
    string Scenario,
    Func<Task<CaseRunResult>> RunAsync);

internal sealed record CaseRunResult(
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object> Metrics);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    IReadOnlyList<OperatorSummary> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

internal sealed record BaselineSummary(DateTimeOffset GeneratedAtUtc, int CaseCount, int Passed, int Failed, double RuntimeMs);

internal sealed record OperatorSummary(string Operator, int CaseCount, int Passed, int Failed, double RuntimeMsAvg, long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(string Scenario, int CaseCount, int Passed, int Failed, double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    IReadOnlyDictionary<string, object> Metrics);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# TimerStatistics Contract Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            string.Empty,
            "## Operators",
            string.Empty,
            "| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | ---: | ---: | ---: | ---: | ---: |",
        };

        foreach (var op in result.Operators)
        {
            lines.Add($"| {op.Operator} | {op.CaseCount} | {op.Passed} | {op.Failed} | {op.RuntimeMsAvg:0.###} | {op.MemoryAllocationBytesAvg} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |",
        });

        foreach (var scenario in result.Scenarios)
        {
            lines.Add($"| {scenario.Scenario} | {scenario.CaseCount} | {scenario.Passed} | {scenario.Failed} | {scenario.RuntimeMsAvg:0.###} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Runtime ms | Final Count | Final Total ms | Failure |",
            "| --- | --- | --- | ---: | ---: | ---: | --- |",
        });

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("FinalCount", out var count);
            item.Metrics.TryGetValue("FinalTotalMs", out var total);
            lines.Add(
                $"| {item.CaseId} | {item.Scenario} | {(item.Passed ? "Yes" : "No")} | {item.RuntimeMs:0.###} | {count ?? "-"} | {total ?? "-"} | {item.ErrorMessage ?? "-"} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Notes",
            string.Empty,
            "- Synthetic deterministic cases covering SingleShot/Cumulative modes, ResetInterval semantics, and trigger passthrough.",
            "- Multi-call cases use Task.Delay between executions to drive non-zero elapsed measurements.",
            "- Average correctness scenario verifies AverageMs == TotalMs / Count exactly across cumulative calls.",
            "- Reset interval scenarios exercise both no-reset (interval=0) and resetting (interval=2,3) flows, asserting count cycling.",
            "- Output contract scenarios assert presence of ElapsedMs/TotalMs/AverageMs/Count keys and conditional Trigger pass-through.",
            "- Validation contract scenarios cover Mode allowlist (SingleShot|Cumulative) and ResetInterval bounds.",
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/TimerStatistics_baseline.json";
        string? report = "quality/evals/reports/TimerStatistics_baseline.md";
        var showHelp = false;
        string? parseError = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = ReadValue(args, ref index, arg, ref parseError) ?? output;
                    break;
                case "--report":
                    report = ReadValue(args, ref index, arg, ref parseError);
                    break;
                case "--no-report":
                    report = null;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    parseError = $"Unknown argument: {arg}";
                    break;
            }
        }

        return new RunnerOptions(output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            TimerStatistics contract runner

            Options:
              --output <path>     JSON baseline output path.
              --report <path>     Markdown report output path.
              --no-report         Skip markdown report generation.
            """);
    }

    private static string? ReadValue(string[] args, ref int index, string name, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"{name} requires a value.";
            return null;
        }

        index++;
        return args[index];
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
    };
}
