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
    $"JsonExtractor contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "JsonExtractor";

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
            // Basic path extraction (4)
            JsonCase("simple_object_path", "{\"data\":42}", "$.data", expectedValue: 42),
            JsonCase("nested_path", "{\"a\":{\"b\":1}}", "$.a.b", expectedValue: 1),
            JsonCase("array_index", "{\"items\":[10,20,30]}", "$.items[0]", expectedValue: 10),
            JsonCase("array_index_second", "{\"items\":[10,20,30]}", "$.items[1]", expectedValue: 20),

            // Nested array + object (2)
            JsonCase("array_nested_object", "{\"a\":[{\"b\":1},{\"b\":2}]}", "$.a[0].b", expectedValue: 1),
            JsonCase("deeply_nested", "{\"a\":{\"b\":{\"c\":{\"d\":99}}}}", "$.a.b.c.d", expectedValue: 99),

            // Root path (1)
            JsonCase("root_path_dollar", "{\"x\":1}", "$", expectedValue: null, outputType: "Any", expectJsonNode: true),

            // Output type conversions (4)
            JsonCase("output_type_string", "{\"data\":\"hello\"}", "$.data", expectedString: "hello", outputType: "String"),
            JsonCase("output_type_float", "{\"data\":3.14}", "$.data", expectedFloat: 3.14f, outputType: "Float"),
            JsonCase("output_type_integer", "{\"data\":42}", "$.data", expectedInt: 42, outputType: "Integer"),
            JsonCase("output_type_boolean", "{\"data\":true}", "$.data", expectedBool: true, outputType: "Boolean"),

            // Default value (2)
            JsonCase("default_value_used", "{\"a\":1}", "$.missing", defaultValue: "fallback", expectedString: "fallback", outputType: "String", required: false),
            JsonCase("default_value_numeric", "{\"a\":1}", "$.missing", defaultValue: "99", expectedInt: 99, outputType: "Integer", required: false),

            // Path miss required (1)
            ErrorCase("path_miss_required", "{\"a\":1}", "$.missing", required: true, expectedErrorContains: "未找到路径"),

            // Invalid JSON (1)
            ErrorCase("invalid_json", "{bad json", "$.a", expectedErrorContains: "解析失败"),

            // Empty JSON (1)
            ErrorCase("empty_json", "", "$.a", expectedErrorContains: "为空"),

            // Null input (1)
            ErrorCase("null_json_input", null, "$.a", expectedErrorContains: "未提供"),

            // Invalid output type conversion (1)
            ErrorCase("invalid_output_type_conversion", "{\"data\":\"hello\"}", "$.data", outputType: "Float", expectedErrorContains: "cannot be converted"),

            // Validation (2)
            ValidationCase("validate_empty_jsonpath", expectedValid: false, jsonPath: ""),
            ValidationCase("validate_invalid_outputtype", expectedValid: false, outputType: "BadType"),
            ValidationCase("validate_valid_params", expectedValid: true, jsonPath: "$.data", outputType: "String"),

            // Output contract (1)
            OutputContractCase("output_keys_present", "{\"x\":1}", "$.x"),
        };
    }

    private static ContractCase JsonCase(
        string caseId,
        string? json,
        string jsonPath,
        string? outputType = null,
        string? defaultValue = null,
        bool required = false,
        object? expectedValue = null,
        string? expectedString = null,
        float? expectedFloat = null,
        int? expectedInt = null,
        bool? expectedBool = null,
        bool expectJsonNode = false)
    {
        return new ContractCase(caseId, OperatorName, "Extraction", () =>
        {
            var op = CreateOperator(jsonPath, outputType ?? "Any", defaultValue, required);
            var inputs = json != null ? new Dictionary<string, object> { ["Json"] = json } : new Dictionary<string, object>();
            var executor = new JsonExtractorOperator(NullLogger<JsonExtractorOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var eval = EvaluateExtraction(output, expectedValue, expectedString, expectedFloat, expectedInt, expectedBool, expectJsonNode);
            var metrics = new Dictionary<string, object>
            {
                ["JsonPath"] = jsonPath,
                ["IsSuccess"] = output.IsSuccess,
                ["Value"] = output.OutputData?.GetValueOrDefault("Value")?.ToString() ?? "null",
                ["ExtractedIsSuccess"] = output.OutputData?.GetValueOrDefault("IsSuccess")?.ToString() ?? "null",
                ["Passed"] = eval.Passed,
            };

            return Task.FromResult(new CaseRunResult(
                caseId, OperatorName, "Extraction", eval.Passed, eval.ErrorMessage,
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase ErrorCase(
        string caseId,
        string? json,
        string jsonPath,
        string? outputType = null,
        bool required = false,
        string? expectedErrorContains = null)
    {
        return new ContractCase(caseId, OperatorName, "Error handling", () =>
        {
            var op = CreateOperator(jsonPath, outputType ?? "Any", null, required);
            var inputs = json != null ? new Dictionary<string, object> { ["Json"] = json } : new Dictionary<string, object>();
            var executor = new JsonExtractorOperator(NullLogger<JsonExtractorOperator>.Instance);

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
                caseId, OperatorName, "Error handling", passed,
                passed ? null : $"Expected failure containing '{expectedErrorContains}', got success={output.IsSuccess}, msg={output.ErrorMessage}",
                sw.Elapsed.TotalMilliseconds, allocAfter - allocBefore, metrics));
        });
    }

    private static ContractCase OutputContractCase(string caseId, string json, string jsonPath)
    {
        return new ContractCase(caseId, OperatorName, "Output contract", () =>
        {
            var op = CreateOperator(jsonPath);
            var inputs = new Dictionary<string, object> { ["Json"] = json };
            var executor = new JsonExtractorOperator(NullLogger<JsonExtractorOperator>.Instance);

            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var output = executor.ExecuteAsync(op, inputs, CancellationToken.None).GetAwaiter().GetResult();
            var allocAfter = GC.GetTotalAllocatedBytes(precise: true);
            sw.Stop();

            var requiredKeys = new[] { "Value", "IsSuccess" };
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

    private static ContractCase ValidationCase(string caseId, bool expectedValid, string? jsonPath = null, string? outputType = null)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(jsonPath ?? "$.data", outputType ?? "Any");
            var executor = new JsonExtractorOperator(NullLogger<JsonExtractorOperator>.Instance);
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

    private static Operator CreateOperator(string jsonPath, string outputType = "Any", string? defaultValue = null, bool required = false)
    {
        var op = new Operator(OperatorName, OperatorType.JsonExtractor, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "JsonPath", "JSONPath", string.Empty, "string", jsonPath));
        op.AddParameter(new Parameter(Guid.NewGuid(), "OutputType", "Output Type", string.Empty, "string", outputType));
        if (defaultValue != null)
            op.AddParameter(new Parameter(Guid.NewGuid(), "DefaultValue", "Default Value", string.Empty, "string", defaultValue));
        if (required)
            op.AddParameter(new Parameter(Guid.NewGuid(), "Required", "Required", string.Empty, "bool", true));
        return op;
    }

    private static (bool Passed, string? ErrorMessage) EvaluateExtraction(
        OperatorExecutionOutput output,
        object? expectedValue,
        string? expectedString,
        float? expectedFloat,
        int? expectedInt,
        bool? expectedBool,
        bool expectJsonNode)
    {
        if (!output.IsSuccess)
            return (false, $"Execution failed: {output.ErrorMessage}");

        var data = output.OutputData!;
        var raw = data.GetValueOrDefault("Value");

        if (expectJsonNode)
        {
            if (raw == null)
                return (false, "Expected JSON node but got null");
            return (true, null);
        }

        var valueText = raw?.ToString() ?? string.Empty;

        if (expectedValue != null)
        {
            if (!int.TryParse(valueText, out var actual))
                return (false, $"Value not an int: got '{valueText}'");
            if (actual != Convert.ToInt32(expectedValue))
                return (false, $"Value mismatch: expected {expectedValue}, got {actual}");
        }
        if (expectedString != null)
        {
            if (valueText != expectedString)
                return (false, $"String mismatch: expected '{expectedString}', got '{valueText}'");
        }
        if (expectedFloat != null)
        {
            if (!float.TryParse(valueText, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var actual))
                return (false, $"Value not a float: got '{valueText}'");
            if (Math.Abs(actual - expectedFloat.Value) > 1e-5f)
                return (false, $"Float mismatch: expected {expectedFloat}, got {actual}");
        }
        if (expectedInt != null)
        {
            if (!int.TryParse(valueText, out var actual))
                return (false, $"Value not an int: got '{valueText}'");
            if (actual != expectedInt.Value)
                return (false, $"Int mismatch: expected {expectedInt}, got {actual}");
        }
        if (expectedBool != null)
        {
            if (!bool.TryParse(valueText, out var actual))
                return (false, $"Value not a bool: got '{valueText}'");
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
        sb.AppendLine("# JsonExtractor Contract Baseline Report");
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

        outputPath ??= "quality/evals/reports/JsonExtractor_baseline.json";
        return new RunnerOptions(outputPath, reportPath, false, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: JsonExtractorContractRunner [options]");
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>   JSON output path (default: quality/evals/reports/JsonExtractor_baseline.json)");
        Console.WriteLine("  -r, --report <path>   Markdown report path");
        Console.WriteLine("  -h, --help            Show this help");
    }
}
