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
    $"ResultJudgment contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private const string OperatorName = "ResultJudgment";

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
        var cases = new List<ContractCase>
        {
            // Equal scenario (4)
            ConditionCase(
                "equal_numeric_match",
                "Equal",
                condition: "Equal",
                expectValue: "42.0",
                inputValue: "42.0",
                expectedIsOk: true,
                expectedCondition: "Equal"),
            ConditionCase(
                "equal_numeric_mismatch",
                "Equal",
                condition: "Equal",
                expectValue: "42.0",
                inputValue: "43.0",
                expectedIsOk: false,
                expectedCondition: "Equal"),
            ConditionCase(
                "equal_string_match",
                "Equal",
                condition: "Equal",
                expectValue: "approved",
                inputValue: "approved",
                expectedIsOk: true,
                expectedCondition: "Equal"),
            ConditionCase(
                "equal_string_mismatch",
                "Equal",
                condition: "Equal",
                expectValue: "approved",
                inputValue: "rejected",
                expectedIsOk: false,
                expectedCondition: "Equal"),

            // NotEqual scenario (2)
            ConditionCase(
                "notequal_numeric_diff",
                "NotEqual",
                condition: "NotEqual",
                expectValue: "0",
                inputValue: "1",
                expectedIsOk: true,
                expectedCondition: "NotEqual"),
            ConditionCase(
                "notequal_numeric_same",
                "NotEqual",
                condition: "NotEqual",
                expectValue: "0",
                inputValue: "0",
                expectedIsOk: false,
                expectedCondition: "NotEqual"),

            // GreaterThan scenario (2)
            ConditionCase(
                "greaterthan_pass",
                "GreaterThan",
                condition: "GreaterThan",
                expectValue: "10",
                inputValue: "11",
                expectedIsOk: true,
                expectedCondition: "GreaterThan"),
            ConditionCase(
                "greaterthan_fail_equal",
                "GreaterThan",
                condition: "GreaterThan",
                expectValue: "10",
                inputValue: "10",
                expectedIsOk: false,
                expectedCondition: "GreaterThan"),

            // LessThan scenario (2)
            ConditionCase(
                "lessthan_pass",
                "LessThan",
                condition: "LessThan",
                expectValue: "10",
                inputValue: "9",
                expectedIsOk: true,
                expectedCondition: "LessThan"),
            ConditionCase(
                "lessthan_fail_equal",
                "LessThan",
                condition: "LessThan",
                expectValue: "10",
                inputValue: "10",
                expectedIsOk: false,
                expectedCondition: "LessThan"),

            // GreaterOrEqual scenario (2)
            ConditionCase(
                "ge_pass_strict",
                "GreaterOrEqual",
                condition: "GreaterOrEqual",
                expectValue: "10",
                inputValue: "11",
                expectedIsOk: true,
                expectedCondition: "GreaterOrEqual"),
            ConditionCase(
                "ge_pass_equal_tolerance",
                "GreaterOrEqual",
                condition: "GreaterOrEqual",
                expectValue: "10",
                inputValue: "10",
                expectedIsOk: true,
                expectedCondition: "GreaterOrEqual"),

            // LessOrEqual scenario (2)
            ConditionCase(
                "le_pass_strict",
                "LessOrEqual",
                condition: "LessOrEqual",
                expectValue: "10",
                inputValue: "9",
                expectedIsOk: true,
                expectedCondition: "LessOrEqual"),
            ConditionCase(
                "le_pass_equal_tolerance",
                "LessOrEqual",
                condition: "LessOrEqual",
                expectValue: "10",
                inputValue: "10",
                expectedIsOk: true,
                expectedCondition: "LessOrEqual"),

            // Range scenario (3)
            ConditionCase(
                "range_inside",
                "Range",
                condition: "Range",
                expectValue: "",
                inputValue: "7",
                expectedIsOk: true,
                expectedCondition: "Range",
                expectValueMin: "5",
                expectValueMax: "10"),
            ConditionCase(
                "range_below",
                "Range",
                condition: "Range",
                expectValue: "",
                inputValue: "4",
                expectedIsOk: false,
                expectedCondition: "Range",
                expectValueMin: "5",
                expectValueMax: "10"),
            ConditionCase(
                "range_above",
                "Range",
                condition: "Range",
                expectValue: "",
                inputValue: "11",
                expectedIsOk: false,
                expectedCondition: "Range",
                expectValueMin: "5",
                expectValueMax: "10"),

            // Confidence Gate scenario (3)
            ConditionCase(
                "confidence_above_threshold",
                "Confidence Gate",
                condition: "Equal",
                expectValue: "1",
                inputValue: "1",
                expectedIsOk: true,
                expectedCondition: "Equal",
                minConfidence: 0.5,
                inputConfidence: 0.8),
            ConditionCase(
                "confidence_below_threshold_gates_to_ng",
                "Confidence Gate",
                condition: "Equal",
                expectValue: "1",
                inputValue: "1",
                expectedIsOk: false,
                expectedCondition: "MinConfidenceGate",
                expectedDetailsContains: "Confidence below MinConfidence",
                minConfidence: 0.5,
                inputConfidence: 0.3),
            ConditionCase(
                "confidence_default_zero_always_passes",
                "Confidence Gate",
                condition: "Equal",
                expectValue: "5",
                inputValue: "5",
                expectedIsOk: true,
                expectedCondition: "Equal"),

            // Field Resolution (2)
            ConditionCase(
                "field_custom",
                "Field Resolution",
                condition: "Equal",
                expectValue: "42",
                inputValue: "42",
                expectedIsOk: true,
                expectedCondition: "Equal",
                fieldName: "Score",
                inputFieldKey: "Score"),
            ConditionCase(
                "field_fallback_to_value",
                "Field Resolution",
                condition: "Equal",
                expectValue: "hello",
                inputValue: "hello",
                expectedIsOk: true,
                expectedCondition: "Equal",
                fieldName: "MissingField",
                inputFieldKey: "Value"),

            // Output Contract (2)
            ConditionCase(
                "output_keys_when_ok",
                "Output contract",
                condition: "Equal",
                expectValue: "42",
                inputValue: "42",
                expectedIsOk: true,
                expectedCondition: "Equal",
                expectedActualValue: "42",
                requireAllKeys: true),
            ConditionCase(
                "output_keys_when_ng",
                "Output contract",
                condition: "Equal",
                expectValue: "42",
                inputValue: "100",
                expectedIsOk: false,
                expectedCondition: "Equal",
                expectedActualValue: "100",
                requireAllKeys: true),

            // Validation contract (5)
            ValidationCase(
                "validate_defaults_ok",
                expectedValid: true),
            ValidationCase(
                "validate_min_confidence_below_zero",
                expectedValid: false,
                minConfidence: -0.1),
            ValidationCase(
                "validate_min_confidence_above_one",
                expectedValid: false,
                minConfidence: 1.5),
            ValidationCase(
                "validate_abs_tol_negative",
                expectedValid: false,
                absTol: -1.0),
            ValidationCase(
                "validate_rel_tol_above_one",
                expectedValid: false,
                relTol: 1.5),
        };

        return cases;
    }

    private static ContractCase ConditionCase(
        string caseId,
        string scenario,
        string condition,
        string expectValue,
        string inputValue,
        bool expectedIsOk,
        string expectedCondition,
        string expectValueMin = "",
        string expectValueMax = "",
        double minConfidence = 0.0,
        double absTol = 1e-4,
        double relTol = 1e-6,
        string fieldName = "Value",
        string inputFieldKey = "Value",
        double? inputConfidence = null,
        string? expectedDetailsContains = null,
        string? expectedActualValue = null,
        bool requireAllKeys = false)
    {
        return new ContractCase(caseId, OperatorName, scenario, async () =>
        {
            var op = CreateOperator(
                condition: condition,
                expectValue: expectValue,
                expectValueMin: expectValueMin,
                expectValueMax: expectValueMax,
                minConfidence: minConfidence,
                absTol: absTol,
                relTol: relTol,
                fieldName: fieldName);

            var inputs = new Dictionary<string, object> { [inputFieldKey] = inputValue };
            if (inputConfidence.HasValue)
            {
                inputs["Confidence"] = inputConfidence.Value;
            }

            var executor = new ResultJudgmentOperator(NullLogger<ResultJudgmentOperator>.Instance);

            var stopwatch = Stopwatch.StartNew();
            var allocationBefore = GC.GetTotalAllocatedBytes(precise: true);
            var execution = await executor.ExecuteAsync(op, inputs);
            var allocationAfter = GC.GetTotalAllocatedBytes(precise: true);
            stopwatch.Stop();

            var metrics = EvaluateExecution(
                execution,
                expectedIsOk,
                expectedCondition,
                expectedDetailsContains,
                expectedActualValue ?? inputValue,
                requireAllKeys);
            var passed = BoolMetric(metrics, "Passed");

            return new CaseRunResult(
                passed,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, allocationAfter - allocationBefore),
                passed ? null : FormatFailure(execution, metrics, expectedIsOk),
                metrics);
        });
    }

    private static ContractCase ValidationCase(
        string caseId,
        bool expectedValid,
        double minConfidence = 0.0,
        double absTol = 1e-4,
        double relTol = 1e-6)
    {
        return new ContractCase(caseId, OperatorName, "Validation contract", () =>
        {
            var op = CreateOperator(
                condition: "Equal",
                expectValue: "1",
                minConfidence: minConfidence,
                absTol: absTol,
                relTol: relTol);
            var executor = new ResultJudgmentOperator(NullLogger<ResultJudgmentOperator>.Instance);

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

    private static Operator CreateOperator(
        string condition,
        string expectValue,
        string expectValueMin = "",
        string expectValueMax = "",
        double minConfidence = 0.0,
        double absTol = 1e-4,
        double relTol = 1e-6,
        string fieldName = "Value")
    {
        var op = new Operator("rj_contract", OperatorType.ResultJudgment, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "FieldName", "Field Name", string.Empty, "string", fieldName));
        op.AddParameter(new Parameter(Guid.NewGuid(), "Condition", "Condition", string.Empty, "string", condition));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ExpectValue", "Expected Value", string.Empty, "string", expectValue));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ExpectValueMin", "Expected Min", string.Empty, "string", expectValueMin));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ExpectValueMax", "Expected Max", string.Empty, "string", expectValueMax));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinConfidence", "Min Confidence", string.Empty, "double", minConfidence));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumericAbsTolerance", "Numeric Absolute Tolerance", string.Empty, "double", absTol));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumericRelTolerance", "Numeric Relative Tolerance", string.Empty, "double", relTol));
        return op;
    }

    private static Dictionary<string, object> EvaluateExecution(
        OperatorExecutionOutput execution,
        bool expectedIsOk,
        string expectedCondition,
        string? expectedDetailsContains,
        string expectedActualValue,
        bool requireAllKeys)
    {
        var metrics = new Dictionary<string, object>
        {
            ["ActualSuccess"] = execution.IsSuccess,
            ["IsOkCorrect"] = false,
            ["JudgmentResultCorrect"] = false,
            ["ConditionResultCorrect"] = false,
            ["JudgmentValueCorrect"] = false,
            ["ConditionFieldCorrect"] = false,
            ["ActualValueCorrect"] = false,
            ["DetailsCorrect"] = expectedDetailsContains is null,
            ["AllKeysPresent"] = !requireAllKeys,
            ["Passed"] = false,
        };

        if (!execution.IsSuccess || execution.OutputData is null)
        {
            return metrics;
        }

        var output = execution.OutputData;
        var requiredKeys = new[]
        {
            "JudgmentResult",
            "IsOk",
            "ConditionResult",
            "JudgmentValue",
            "Details",
            "Condition",
            "ActualValue",
        };
        var allKeysPresent = requiredKeys.All(output.ContainsKey);

        var isOk = output.TryGetValue("IsOk", out var isOkObj) && isOkObj is bool b && b;
        var conditionResult = output.TryGetValue("ConditionResult", out var crObj) && crObj is bool cr && cr;
        var judgmentResult = output.TryGetValue("JudgmentResult", out var jrObj) ? jrObj?.ToString() ?? string.Empty : string.Empty;
        var judgmentValue = output.TryGetValue("JudgmentValue", out var jvObj) ? jvObj?.ToString() ?? string.Empty : string.Empty;
        var conditionField = output.TryGetValue("Condition", out var cfObj) ? cfObj?.ToString() ?? string.Empty : string.Empty;
        var actualField = output.TryGetValue("ActualValue", out var avObj) ? avObj?.ToString() ?? string.Empty : string.Empty;
        var details = output.TryGetValue("Details", out var dObj) ? dObj?.ToString() ?? string.Empty : string.Empty;

        var isOkCorrect = isOk == expectedIsOk;
        var conditionResultCorrect = conditionResult == expectedIsOk;
        var judgmentResultCorrect = string.Equals(judgmentResult, expectedIsOk ? "OK" : "NG", StringComparison.Ordinal);
        var judgmentValueCorrect = string.Equals(judgmentValue, expectedIsOk ? "1" : "0", StringComparison.Ordinal);
        var conditionFieldCorrect = string.Equals(conditionField, expectedCondition, StringComparison.Ordinal);
        var actualValueCorrect = string.Equals(actualField, expectedActualValue, StringComparison.Ordinal);
        var detailsCorrect = expectedDetailsContains is null ||
            details.Contains(expectedDetailsContains, StringComparison.OrdinalIgnoreCase);
        var allKeysOk = !requireAllKeys || allKeysPresent;

        metrics["JudgmentResult"] = judgmentResult;
        metrics["IsOk"] = isOk;
        metrics["ConditionField"] = conditionField;
        metrics["JudgmentValue"] = judgmentValue;
        metrics["Details"] = details;
        metrics["ActualValue"] = actualField;
        metrics["IsOkCorrect"] = isOkCorrect;
        metrics["JudgmentResultCorrect"] = judgmentResultCorrect;
        metrics["ConditionResultCorrect"] = conditionResultCorrect;
        metrics["JudgmentValueCorrect"] = judgmentValueCorrect;
        metrics["ConditionFieldCorrect"] = conditionFieldCorrect;
        metrics["ActualValueCorrect"] = actualValueCorrect;
        metrics["DetailsCorrect"] = detailsCorrect;
        metrics["AllKeysPresent"] = allKeysOk;
        metrics["Passed"] =
            isOkCorrect &&
            judgmentResultCorrect &&
            conditionResultCorrect &&
            judgmentValueCorrect &&
            conditionFieldCorrect &&
            actualValueCorrect &&
            detailsCorrect &&
            allKeysOk;
        return metrics;
    }

    private static bool BoolMetric(IReadOnlyDictionary<string, object> metrics, string key) =>
        metrics.TryGetValue(key, out var value) && value is bool b && b;

    private static string FormatFailure(
        OperatorExecutionOutput? execution,
        IReadOnlyDictionary<string, object> metrics,
        bool expectedIsOk)
    {
        if (execution is not null && !execution.IsSuccess)
        {
            return execution.ErrorMessage ?? "Execution failed.";
        }

        var keys = new[]
        {
            "IsOkCorrect",
            "JudgmentResultCorrect",
            "ConditionResultCorrect",
            "JudgmentValueCorrect",
            "ConditionFieldCorrect",
            "ActualValueCorrect",
            "DetailsCorrect",
            "AllKeysPresent",
        };
        var pieces = keys
            .Where(metrics.ContainsKey)
            .Select(key => $"{key}={metrics[key]}")
            .ToList();
        pieces.Add($"ExpectedIsOk={expectedIsOk}");
        return string.Join(", ", pieces);
    }
}

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
            "# ResultJudgment Contract Baseline",
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
            "| Case | Scenario | Passed | Runtime ms | Judgment | Condition | Failure |",
            "| --- | --- | --- | ---: | --- | --- | --- |",
        });

        foreach (var item in result.Cases)
        {
            item.Metrics.TryGetValue("JudgmentResult", out var judgment);
            item.Metrics.TryGetValue("ConditionField", out var condition);
            lines.Add(
                $"| {item.CaseId} | {item.Scenario} | {(item.Passed ? "Yes" : "No")} | {item.RuntimeMs:0.###} | {judgment ?? "-"} | {condition ?? "-"} | {item.ErrorMessage ?? "-"} |");
        }

        lines.AddRange(new[]
        {
            string.Empty,
            "## Notes",
            string.Empty,
            "- Synthetic deterministic cases covering Equal/NotEqual/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual/Range conditions.",
            "- Confidence gate scenarios verify MinConfidence threshold short-circuits to NG with `MinConfidenceGate` condition.",
            "- Field resolution scenarios cover custom FieldName lookup and fallback to `Value` input.",
            "- Output contract scenarios assert the seven-key output bundle (JudgmentResult/IsOk/ConditionResult/JudgmentValue/Details/Condition/ActualValue).",
            "- Validation contract scenarios exercise MinConfidence, NumericAbsTolerance, and NumericRelTolerance bounds checks.",
        });

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/ResultJudgment_baseline.json";
        string? report = "quality/evals/reports/ResultJudgment_baseline.md";
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
            ResultJudgment contract runner

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
