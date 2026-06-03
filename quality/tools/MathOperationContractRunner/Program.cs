using System.Diagnostics;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
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
    $"MathOperation contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "MathOperation";

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
            // Add (2)
            MathCase("add_positive", "Add", valueA: 2.0, valueB: 3.0, expectedResult: 5.0, expectedIsPositive: true),
            MathCase("add_negative", "Add", valueA: -2.0, valueB: -3.0, expectedResult: -5.0, expectedIsNegative: true),

            // Subtract (2)
            MathCase("subtract_positive", "Subtract", valueA: 5.0, valueB: 3.0, expectedResult: 2.0, expectedIsPositive: true),
            MathCase("subtract_negative", "Subtract", valueA: 2.0, valueB: 5.0, expectedResult: -3.0, expectedIsNegative: true),

            // Multiply (2)
            MathCase("multiply_basic", "Multiply", valueA: 4.0, valueB: 5.0, expectedResult: 20.0, expectedIsPositive: true),
            MathCase("multiply_by_zero", "Multiply", valueA: 7.0, valueB: 0.0, expectedResult: 0.0, expectedIsZero: true),

            // Divide (3)
            MathCase("divide_basic", "Divide", valueA: 10.0, valueB: 2.0, expectedResult: 5.0, expectedIsPositive: true),
            MathCase("divide_fraction", "Divide", valueA: 1.0, valueB: 4.0, expectedResult: 0.25, expectedIsPositive: true),
            ErrorCase("divide_by_zero", "Divide", valueA: 5.0, valueB: 0.0, expectedErrorContains: "zero"),

            // Abs (1)
            MathCase("abs_negative", "Abs", valueA: -7.5, expectedResult: 7.5, expectedIsPositive: true, requiresB: false),

            // Min/Max (2)
            MathCase("min_basic", "Min", valueA: 3.0, valueB: 8.0, expectedResult: 3.0, expectedIsPositive: true),
            MathCase("max_basic", "Max", valueA: 3.0, valueB: 8.0, expectedResult: 8.0, expectedIsPositive: true),

            // Power (2)
            MathCase("power_basic", "Power", valueA: 2.0, valueB: 3.0, expectedResult: 8.0, expectedIsPositive: true),
            MathCase("power_zero_exp", "Power", valueA: 5.0, valueB: 0.0, expectedResult: 1.0, expectedIsPositive: true),

            // Sqrt (2)
            MathCase("sqrt_perfect", "Sqrt", valueA: 9.0, expectedResult: 3.0, expectedIsPositive: true, requiresB: false),
            ErrorCase("sqrt_negative", "Sqrt", valueA: -4.0, expectedErrorContains: "negative", requiresB: false),

            // Round (2)
            MathCase("round_up", "Round", valueA: 3.7, expectedResult: 4.0, expectedIsPositive: true, requiresB: false),
            MathCase("round_down", "Round", valueA: 3.2, expectedResult: 3.0, expectedIsPositive: true, requiresB: false),

            // Modulo (2)
            MathCase("modulo_basic", "Modulo", valueA: 10.0, valueB: 3.0, expectedResult: 1.0, expectedIsPositive: true),
            ErrorCase("modulo_by_zero", "Modulo", valueA: 10.0, valueB: 0.0, expectedErrorContains: "zero"),

            // Input type coercion (2)
            MathCase("add_int_input", "Add", valueA: 2, valueB: 3, expectedResult: 5.0, expectedIsPositive: true),
            MathCase("add_string_input", "Add", valueA: "2.5", valueB: "3.5", expectedResult: 6.0, expectedIsPositive: true),

            // Edge / error (3)
            ErrorCase("non_finite_input_nan", "Add", valueA: double.NaN, valueB: 1.0, expectedErrorContains: "finite"),
            ErrorCase("non_finite_input_infinity", "Add", valueA: double.PositiveInfinity, valueB: 1.0, expectedErrorContains: "finite"),

            // Output contract (1)
            OutputContractCase("output_keys_present", "Add", valueA: 1.0, valueB: 2.0),

            // Validation contract (1)
            ValidationCase("validate_invalid_operation", expectedValid: false, operation: "BadOp"),
            ValidationCase("validate_valid_operation", expectedValid: true, operation: "Add"),
        };
    }

    private static ContractCase MathCase(
        string caseId,
        string operation,
        object valueA,
        object? valueB = null,
        double expectedResult = 0,
        bool expectedIsPositive = false,
        bool expectedIsZero = false,
        bool expectedIsNegative = false,
        bool requiresB = true)
    {
        return new ContractCase(caseId, OperatorName, operation, () =>
        {
            var op = CreateOperator(operation);
            var inputs = new Dictionary<string, object> { ["ValueA"] = valueA };
            if (requiresB && valueB != null)
            {
                inputs["ValueB"] = valueB;
            }

            var executor = new MathOperationOperator(NullLogger<MathOperationOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var eval = EvaluateExecution(output, expectedResult, expectedIsPositive, expectedIsZero, expectedIsNegative, null);
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedResult"] = expectedResult,
                ["ActualResult"] = output.OutputData?.GetValueOrDefault("Result"),
                ["IsPositive"] = output.OutputData?.GetValueOrDefault("IsPositive"),
                ["IsZero"] = output.OutputData?.GetValueOrDefault("IsZero"),
                ["IsNegative"] = output.OutputData?.GetValueOrDefault("IsNegative"),
                ["Passed"] = eval.Passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, operation, eval.Passed, eval.ErrorMessage, sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase ErrorCase(
        string caseId,
        string operation,
        object valueA,
        object? valueB = null,
        string? expectedErrorContains = null,
        bool requiresB = true)
    {
        return new ContractCase(caseId, OperatorName, $"{operation} errors", () =>
        {
            var op = CreateOperator(operation);
            var inputs = new Dictionary<string, object> { ["ValueA"] = valueA };
            if (requiresB && valueB != null)
            {
                inputs["ValueB"] = valueB;
            }

            var executor = new MathOperationOperator(NullLogger<MathOperationOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var passed = !output.IsSuccess;
            if (passed && !string.IsNullOrWhiteSpace(expectedErrorContains))
            {
                passed = output.ErrorMessage?.Contains(expectedErrorContains, StringComparison.OrdinalIgnoreCase) == true;
            }

            var metrics = new Dictionary<string, object>
            {
                ["ExpectedFailure"] = true,
                ["ActualIsSuccess"] = output.IsSuccess,
                ["ErrorMessage"] = output.ErrorMessage ?? string.Empty,
                ["Passed"] = passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, $"{operation} errors", passed,
                passed ? null : $"Expected failure containing '{expectedErrorContains}', got success={output.IsSuccess}, msg={output.ErrorMessage}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase OutputContractCase(
        string caseId,
        string operation,
        object valueA,
        object? valueB = null)
    {
        return new ContractCase(caseId, OperatorName, "Output contract", () =>
        {
            var op = CreateOperator(operation);
            var inputs = new Dictionary<string, object> { ["ValueA"] = valueA };
            if (valueB != null) inputs["ValueB"] = valueB;

            var executor = new MathOperationOperator(NullLogger<MathOperationOperator>.Instance);
            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var requiredKeys = new[] { "Result", "ResultFloat", "ResultInt", "IsPositive", "IsZero", "IsNegative", "InputA", "InputB", "Operation" };
            var data = output.OutputData ?? new Dictionary<string, object>();
            var missing = requiredKeys.Where(k => !data.ContainsKey(k)).ToList();
            var passed = output.IsSuccess && missing.Count == 0;

            var metrics = new Dictionary<string, object>
            {
                ["RequiredKeys"] = string.Join(", ", requiredKeys),
                ["MissingKeys"] = string.Join(", ", missing),
                ["Passed"] = passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, "Output contract", passed,
                passed ? null : $"Missing keys: {string.Join(", ", missing)}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase ValidationCase(string caseId, bool expectedValid, string operation)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(operation);
            var executor = new MathOperationOperator(NullLogger<MathOperationOperator>.Instance);
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

    private static Operator CreateOperator(string operation)
    {
        var op = new Operator(OperatorName, OperatorType.MathOperation, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "Operation", "Operation", "运算类型", "string", operation));
        return op;
    }

    private static (bool Passed, string? ErrorMessage) EvaluateExecution(
        OperatorExecutionOutput output,
        double expectedResult,
        bool expectedIsPositive,
        bool expectedIsZero,
        bool expectedIsNegative,
        string? _)
    {
        if (!output.IsSuccess)
        {
            return (false, $"Execution failed: {output.ErrorMessage}");
        }

        var data = output.OutputData!;
        var result = data.GetValueOrDefault("Result") is double d ? d : Convert.ToDouble(data.GetValueOrDefault("Result"));
        if (Math.Abs(result - expectedResult) > 1e-9)
        {
            return (false, $"Result mismatch: expected {expectedResult}, got {result}");
        }

        if (expectedIsPositive && data.GetValueOrDefault("IsPositive") is not true)
            return (false, "Expected IsPositive=true");
        if (expectedIsZero && data.GetValueOrDefault("IsZero") is not true)
            return (false, "Expected IsZero=true");
        if (expectedIsNegative && data.GetValueOrDefault("IsNegative") is not true)
            return (false, "Expected IsNegative=true");

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
            runResult.ErrorMessage,
            runResult.RuntimeMs,
            runResult.MemoryAllocationBytes,
            runResult.Metrics);
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
    string? ErrorMessage,
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
    string? ErrorMessage,
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
        sb.AppendLine("# MathOperation Contract Baseline Report");
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
            sb.AppendLine($"| {c.CaseId} | {c.Scenario} | {(c.Passed ? "PASS" : "FAIL")} | {c.RuntimeMs:F3} | {c.MemoryAllocationBytes} | {c.ErrorMessage ?? ""} |");
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

        outputPath ??= "quality/evals/reports/MathOperation_baseline.json";
        return new RunnerOptions(outputPath, reportPath, false, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: MathOperationContractRunner [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>   JSON output path (default: quality/evals/reports/MathOperation_baseline.json)");
        Console.WriteLine("  -r, --report <path>   Markdown report path");
        Console.WriteLine("  -h, --help            Show this help");
    }
}
