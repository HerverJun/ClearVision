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
    $"TypeConvert contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "TypeConvert";

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
            // String input (4)
            ConvertCase("string_to_string", "hello", "String", expectedString: "hello"),
            ConvertCase("string_to_float", "3.14", "Float", expectedFloat: 3.14f),
            ConvertCase("string_to_integer", "42", "Integer", expectedInt: 42),
            ConvertCase("string_to_boolean", "true", "Boolean", expectedBool: true),

            // Int input (4)
            ConvertCase("int_to_string", 42, "String", expectedString: "42"),
            ConvertCase("int_to_float", 42, "Float", expectedFloat: 42f),
            ConvertCase("int_to_integer", 42, "Integer", expectedInt: 42),
            ConvertCase("int_to_boolean", 1, "Boolean", expectedBool: true),

            // Float input (4)
            ConvertCase("float_to_string", 3.14f, "String", expectedString: "3.14"),
            ConvertCase("float_to_float", 3.14f, "Float", expectedFloat: 3.14f),
            ConvertCase("float_to_integer", 3.9f, "Integer", expectedInt: 3),
            ConvertCase("float_to_boolean_zero", 0.0f, "Boolean", expectedBool: false),

            // Bool input (4)
            ConvertCase("bool_to_string", true, "String", expectedString: "True"),
            ConvertCase("bool_to_float", true, "Float", expectedFloat: 1f),
            ConvertCase("bool_to_integer", false, "Integer", expectedInt: 0),
            ConvertCase("bool_to_boolean", true, "Boolean", expectedBool: true),

            // Double input (2)
            ConvertCase("double_to_string", 2.5, "String", expectedString: "2.5"),
            ConvertCase("double_to_integer", 2.9, "Integer", expectedInt: 2),

            // Format (1)
            ConvertCase("format_number", 1234.5, "String", format: "F2", expectedString: "1234.50"),

            // Validation (1)
            ValidationCase("validate_invalid_target", expectedValid: false, targetType: "BadType"),
            ValidationCase("validate_valid_target", expectedValid: true, targetType: "Float"),

            // Output contract (1)
            OutputContractCase("output_keys_present", 123, "Integer"),

            // Error (1)
            ErrorCase("null_input_fails", null, "String"),
        };
    }

    private static ContractCase ConvertCase(
        string caseId,
        object? input,
        string targetType,
        string? format = null,
        string? expectedString = null,
        float? expectedFloat = null,
        int? expectedInt = null,
        bool? expectedBool = null)
    {
        return new ContractCase(caseId, OperatorName, targetType, () =>
        {
            var op = CreateOperator(targetType, format);
            var inputs = input != null ? new Dictionary<string, object> { ["Input"] = input } : new Dictionary<string, object>();
            var executor = new TypeConvertOperator(NullLogger<TypeConvertOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var eval = EvaluateConversion(output, expectedString, expectedFloat, expectedInt, expectedBool);
            var metrics = new Dictionary<string, object>
            {
                ["Input"] = input?.ToString() ?? "null",
                ["TargetType"] = targetType,
                ["Output"] = output.OutputData?.GetValueOrDefault("Output")?.ToString() ?? "null",
                ["OriginalType"] = output.OutputData?.GetValueOrDefault("OriginalType")?.ToString() ?? "null",
                ["Passed"] = eval.Passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, targetType, eval.Passed, eval.ErrorMessage,
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase ErrorCase(string caseId, object? input, string targetType)
    {
        return new ContractCase(caseId, OperatorName, "Error handling", () =>
        {
            var op = CreateOperator(targetType);
            var inputs = input != null ? new Dictionary<string, object> { ["Input"] = input } : new Dictionary<string, object>();
            var executor = new TypeConvertOperator(NullLogger<TypeConvertOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var passed = !output.IsSuccess;
            var metrics = new Dictionary<string, object>
            {
                ["ExpectedFailure"] = true,
                ["ActualIsSuccess"] = output.IsSuccess,
                ["Passed"] = passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, "Error handling", passed,
                passed ? null : "Expected failure for null input",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase OutputContractCase(string caseId, object input, string targetType)
    {
        return new ContractCase(caseId, OperatorName, "Output contract", () =>
        {
            var op = CreateOperator(targetType);
            var inputs = new Dictionary<string, object> { ["Input"] = input };
            var executor = new TypeConvertOperator(NullLogger<TypeConvertOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var requiredKeys = new[] { "Output", "AsString", "AsFloat", "AsInteger", "AsBoolean", "OriginalType" };
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

    private static ContractCase ValidationCase(string caseId, bool expectedValid, string targetType)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(targetType);
            var executor = new TypeConvertOperator(NullLogger<TypeConvertOperator>.Instance);
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

    private static Operator CreateOperator(string targetType, string? format = null)
    {
        var op = new Operator(OperatorName, OperatorType.TypeConvert, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "TargetType", "Target Type", string.Empty, "string", targetType));
        if (format != null)
            op.AddParameter(new Parameter(Guid.NewGuid(), "Format", "Format", string.Empty, "string", format));
        return op;
    }

    private static (bool Passed, string? ErrorMessage) EvaluateConversion(
        OperatorExecutionOutput output,
        string? expectedString,
        float? expectedFloat,
        int? expectedInt,
        bool? expectedBool)
    {
        if (!output.IsSuccess)
            return (false, $"Execution failed: {output.ErrorMessage}");

        var data = output.OutputData!;

        if (expectedString != null)
        {
            var actual = data.GetValueOrDefault("Output")?.ToString() ?? string.Empty;
            if (actual != expectedString)
                return (false, $"String mismatch: expected '{expectedString}', got '{actual}'");
        }
        if (expectedFloat != null)
        {
            var actual = data.GetValueOrDefault("Output") is float f ? f : Convert.ToSingle(data.GetValueOrDefault("Output"));
            if (Math.Abs(actual - expectedFloat.Value) > 1e-5f)
                return (false, $"Float mismatch: expected {expectedFloat}, got {actual}");
        }
        if (expectedInt != null)
        {
            var actual = data.GetValueOrDefault("Output") is int i ? i : Convert.ToInt32(data.GetValueOrDefault("Output"));
            if (actual != expectedInt.Value)
                return (false, $"Int mismatch: expected {expectedInt}, got {actual}");
        }
        if (expectedBool != null)
        {
            var actual = data.GetValueOrDefault("Output") is bool b ? b : Convert.ToBoolean(data.GetValueOrDefault("Output"));
            if (actual != expectedBool.Value)
                return (false, $"Bool mismatch: expected {expectedBool}, got {actual}");
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
            runResult.ErrorMessage,
            runResult.RuntimeMs,
            runResult.MemoryAllocationBytes,
            runResult.Metrics);
    }
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
        sb.AppendLine("# TypeConvert Contract Baseline Report");
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

        outputPath ??= "quality/evals/reports/TypeConvert_baseline.json";
        return new RunnerOptions(outputPath, reportPath, false, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: TypeConvertContractRunner [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>   JSON output path (default: quality/evals/reports/TypeConvert_baseline.json)");
        Console.WriteLine("  -r, --report <path>   Markdown report path");
        Console.WriteLine("  -h, --help            Show this help");
    }
}
