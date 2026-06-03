using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceDetectionResult = ClearVision.Product.Core.Services.DetectionResult;

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
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DualModalVoting contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private static readonly DualModalVotingOperator Operator = new(NullLogger<DualModalVotingOperator>.Instance);

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<ContractCase>
        {
            new("weighted_average_ok_probability_mixed_modalities", "WeightedAverage strategy", WeightedAverageOkProbabilityMixed),
            new("weighted_average_high_confidence_ng_wins", "WeightedAverage strategy", WeightedAverageHighConfidenceNgWins),
            new("weighted_average_boundary_is_ok", "WeightedAverage strategy", WeightedAverageBoundaryIsOk),
            new("weighted_average_custom_threshold_flips_to_ng", "WeightedAverage strategy", WeightedAverageCustomThresholdFlipsToNg),
            new("weighted_average_zero_weights_fail", "WeightedAverage strategy", WeightedAverageZeroWeightsFail),
            new("weighted_average_custom_weights_normalized", "WeightedAverage strategy", WeightedAverageCustomWeightsNormalized),
            new("unanimous_both_ok_is_ok", "Unanimous strategy", UnanimousBothOkIsOk),
            new("unanimous_one_ng_is_ng_confidence", "Unanimous strategy", UnanimousOneNgIsNgConfidence),
            new("majority_same_ok_averages_ok_probability", "Majority strategy", MajoritySameOk),
            new("majority_same_ng_uses_final_ng_confidence", "Majority strategy", MajoritySameNg),
            new("majority_conflict_higher_dl_confidence_wins", "Majority strategy", MajorityConflictDlWins),
            new("majority_conflict_higher_traditional_confidence_wins", "Majority strategy", MajorityConflictTraditionalWins),
            new("majority_conflict_equal_confidence_prefers_dl", "Majority strategy", MajorityConflictEqualPrefersDl),
            new("prioritize_deep_learning_follows_dl", "Priority strategies", PrioritizeDeepLearningFollowsDl),
            new("prioritize_traditional_follows_traditional", "Priority strategies", PrioritizeTraditionalFollowsTraditional),
            new("case_insensitive_strategy_executes", "Strategy parsing", CaseInsensitiveStrategyExecutes),
            new("dictionary_isok_confidence_clamps_high", "Input extraction", DictionaryIsOkConfidenceClampsHigh),
            new("dictionary_isok_confidence_clamps_low", "Input extraction", DictionaryIsOkConfidenceClampsLow),
            new("defect_count_good_maps_to_ok", "Input extraction", DefectCountGoodMapsToOk),
            new("defect_count_uses_max_defect_confidence", "Input extraction", DefectCountUsesMaxDefectConfidence),
            new("defect_count_missing_confidence_is_conservative_ng", "Input extraction", DefectCountMissingConfidenceConservativeNg),
            new("missing_traditional_uses_neutral_probability", "Missing input contract", MissingTraditionalUsesNeutralProbability),
            new("missing_dl_uses_neutral_probability", "Missing input contract", MissingDlUsesNeutralProbability),
            new("no_valid_inputs_fail", "Missing input contract", NoValidInputsFail),
            new("custom_judgment_values_are_used", "Output contract", CustomJudgmentValuesAreUsed),
            new("validate_defaults_valid", "Validation contract", ValidateDefaultsValid),
            new("validate_bad_strategy_invalid", "Validation contract", ValidateBadStrategyInvalid),
            new("validate_weight_sum_zero_invalid", "Validation contract", ValidateWeightSumZeroInvalid),
            new("validate_weight_sum_not_one_invalid", "Validation contract", ValidateWeightSumNotOneInvalid),
            new("normalize_strategy_trims_and_canonicalizes", "Private helper contract", NormalizeStrategyCanonicalizes),
            new("failed_detection_result_is_neutral", "Private helper contract", FailedDetectionResultIsNeutral)
        };

        var results = new List<CaseResult>(cases.Count);
        foreach (var contractCase in cases)
        {
            results.Add(await RunCaseAsync(contractCase));
        }

        var scenarioSummaries = results
            .GroupBy(x => x.Scenario)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(x => x.Passed),
                group.Count(x => !x.Passed),
                Math.Round(group.Average(x => x.RuntimeMs), 3)))
            .ToArray();

        var passed = results.Count(x => x.Passed);
        var failed = results.Count - passed;
        var summary = new BaselineSummary(
            DateTimeOffset.UtcNow,
            results.Count,
            passed,
            failed,
            Math.Round(results.Sum(x => x.RuntimeMs), 3));

        var operatorEvidence = new[]
        {
            new OperatorEvidence(
                "DualModalVoting",
                results.Count,
                passed,
                failed,
                Math.Round(results.Average(x => x.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(results.Average(x => x.MemoryAllocationBytes))))
        };

        return new BaselineResult(summary, operatorEvidence, scenarioSummaries, results.ToArray());
    }

    private static async Task<CaseResult> RunCaseAsync(ContractCase contractCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await contractCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(contractCase.Name, contractCase.Scenario, true, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3), Math.Max(0, afterBytes - beforeBytes), null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(contractCase.Name, contractCase.Scenario, false, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3), Math.Max(0, afterBytes - beforeBytes), FormatException(ex));
        }
    }

    private static async Task WeightedAverageOkProbabilityMixed()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.4));

        AssertOutput(result, true, 0.78, "1");
    }

    private static async Task WeightedAverageHighConfidenceNgWins()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            ServiceDetectionResult.Success(true, 0.51),
            ServiceDetectionResult.Success(false, 0.95));

        AssertOutput(result, false, 0.674, "0");
    }

    private static async Task WeightedAverageBoundaryIsOk()
    {
        var result = await ExecuteAsync(
            CreateOperator(confidenceThreshold: 0.78),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.4));

        AssertOutput(result, true, 0.78, "1");
    }

    private static async Task WeightedAverageCustomThresholdFlipsToNg()
    {
        var result = await ExecuteAsync(
            CreateOperator(confidenceThreshold: 0.8),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.4));

        AssertOutput(result, false, 0.22, "0");
    }

    private static async Task WeightedAverageZeroWeightsFail()
    {
        var result = await ExecuteAsync(
            CreateOperator(dlWeight: 0, traditionalWeight: 0),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.1));

        Require(!result.IsSuccess, "Zero-weight weighted average should fail.");
        RequireContains(result.ErrorMessage, "DLWeight + TraditionalWeight > 0");
    }

    private static async Task WeightedAverageCustomWeightsNormalized()
    {
        var result = await ExecuteAsync(
            CreateOperator(dlWeight: 0.25, traditionalWeight: 0.75),
            ServiceDetectionResult.Success(true, 0.8),
            ServiceDetectionResult.Success(false, 0.6));

        AssertOutput(result, true, 0.5, "1");
    }

    private static async Task UnanimousBothOkIsOk()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Unanimous"),
            ServiceDetectionResult.Success(true, 0.8),
            ServiceDetectionResult.Success(true, 0.7));

        AssertOutput(result, true, 0.7, "1");
    }

    private static async Task UnanimousOneNgIsNgConfidence()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Unanimous"),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.4));

        AssertOutput(result, false, 0.4, "0");
    }

    private static async Task MajoritySameOk()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Majority"),
            ServiceDetectionResult.Success(true, 0.8),
            ServiceDetectionResult.Success(true, 0.7));

        AssertOutput(result, true, 0.75, "1");
    }

    private static async Task MajoritySameNg()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Majority"),
            ServiceDetectionResult.Success(false, 0.8),
            ServiceDetectionResult.Success(false, 0.6));

        AssertOutput(result, false, 0.7, "0");
    }

    private static async Task MajorityConflictDlWins()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Majority"),
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.8));

        AssertOutput(result, true, 0.9, "1");
    }

    private static async Task MajorityConflictTraditionalWins()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Majority"),
            ServiceDetectionResult.Success(true, 0.7),
            ServiceDetectionResult.Success(false, 0.8));

        AssertOutput(result, false, 0.8, "0");
    }

    private static async Task MajorityConflictEqualPrefersDl()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "Majority"),
            ServiceDetectionResult.Success(true, 0.8),
            ServiceDetectionResult.Success(false, 0.8));

        AssertOutput(result, true, 0.8, "1");
    }

    private static async Task PrioritizeDeepLearningFollowsDl()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "PrioritizeDeepLearning"),
            ServiceDetectionResult.Success(false, 0.87),
            ServiceDetectionResult.Success(true, 0.99));

        AssertOutput(result, false, 0.87, "0");
    }

    private static async Task PrioritizeTraditionalFollowsTraditional()
    {
        var result = await ExecuteAsync(
            CreateOperator(strategy: "PrioritizeTraditional"),
            ServiceDetectionResult.Success(false, 0.99),
            ServiceDetectionResult.Success(true, 0.66));

        AssertOutput(result, true, 0.66, "1");
    }

    private static async Task CaseInsensitiveStrategyExecutes()
    {
        var op = CreateOperator(strategy: " weightedaverage ");
        var validation = Operator.ValidateParameters(op);
        Require(validation.IsValid, ValidationErrors(validation) ?? "Expected case-insensitive strategy to validate.");

        var result = await ExecuteAsync(
            op,
            ServiceDetectionResult.Success(true, 0.9),
            ServiceDetectionResult.Success(false, 0.4));

        AssertOutput(result, true, 0.78, "1");
    }

    private static async Task DictionaryIsOkConfidenceClampsHigh()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            new Dictionary<string, object> { ["IsOk"] = true, ["Confidence"] = 2.0 },
            ServiceDetectionResult.Success(true, 0.5));

        AssertOutput(result, true, 0.8, "1");
    }

    private static async Task DictionaryIsOkConfidenceClampsLow()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            new Dictionary<string, object> { ["IsOk"] = false, ["Confidence"] = -1.0 },
            ServiceDetectionResult.Success(false, 0.5));

        AssertOutput(result, true, 0.8, "1");
    }

    private static async Task DefectCountGoodMapsToOk()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            new Dictionary<string, object> { ["DefectCount"] = 0, ["Defects"] = new List<object>() },
            ServiceDetectionResult.Success(true, 0.5));

        AssertOutput(result, true, 0.8, "1");
    }

    private static async Task DefectCountUsesMaxDefectConfidence()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            new Dictionary<string, object>
            {
                ["DefectCount"] = 1,
                ["Defects"] = new List<object>
                {
                    new Dictionary<string, object> { ["Confidence"] = 0.4 },
                    new Dictionary<string, object> { ["Confidence"] = 0.9 }
                }
            },
            ServiceDetectionResult.Success(true, 1.0));

        AssertOutput(result, false, 0.54, "0");
    }

    private static async Task DefectCountMissingConfidenceConservativeNg()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            new Dictionary<string, object> { ["DefectCount"] = 1, ["Defects"] = new List<object>() },
            new Dictionary<string, object> { ["DefectCount"] = 1 });

        AssertOutput(result, false, 1.0, "0");
    }

    private static async Task MissingTraditionalUsesNeutralProbability()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            ServiceDetectionResult.Success(true, 0.9),
            null);

        AssertOutput(result, true, 0.74, "1");
    }

    private static async Task MissingDlUsesNeutralProbability()
    {
        var result = await ExecuteAsync(
            CreateOperator(),
            null,
            ServiceDetectionResult.Success(false, 0.9));

        AssertOutput(result, false, 0.66, "0");
    }

    private static async Task NoValidInputsFail()
    {
        var result = await ExecuteRawAsync(CreateOperator(), new Dictionary<string, object>());

        Require(!result.IsSuccess, "No valid inputs should fail.");
        RequireContains(result.ErrorMessage, "No valid detection result");
    }

    private static async Task CustomJudgmentValuesAreUsed()
    {
        var result = await ExecuteAsync(
            CreateOperator(okValue: "PASS", ngValue: "FAIL", strategy: "PrioritizeTraditional"),
            ServiceDetectionResult.Success(false, 0.99),
            ServiceDetectionResult.Success(true, 0.7));

        AssertOutput(result, true, 0.7, "PASS");
    }

    private static Task ValidateDefaultsValid()
    {
        var validation = Operator.ValidateParameters(CreateOperator());
        Require(validation.IsValid, ValidationErrors(validation) ?? "Defaults should validate.");
        return Task.CompletedTask;
    }

    private static Task ValidateBadStrategyInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(strategy: "Median"));
        Require(!validation.IsValid, "Bad strategy should be invalid.");
        RequireContains(ValidationErrors(validation), "VotingStrategy");
        return Task.CompletedTask;
    }

    private static Task ValidateWeightSumZeroInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(dlWeight: 0, traditionalWeight: 0));
        Require(!validation.IsValid, "Zero weight sum should be invalid.");
        RequireContains(ValidationErrors(validation), "DLWeight + TraditionalWeight > 0");
        return Task.CompletedTask;
    }

    private static Task ValidateWeightSumNotOneInvalid()
    {
        var validation = Operator.ValidateParameters(CreateOperator(dlWeight: 0.7, traditionalWeight: 0.4));
        Require(!validation.IsValid, "WeightedAverage weights should sum to about 1.");
        RequireContains(ValidationErrors(validation), "approximately 1.0");
        return Task.CompletedTask;
    }

    private static Task NormalizeStrategyCanonicalizes()
    {
        var normalized = InvokeStatic<string>("NormalizeStrategy", " prioritzetraditional ");
        Require(normalized == null, "Misspelled strategy should not normalize.");

        normalized = InvokeStatic<string>("NormalizeStrategy", " prioritizeTraditional ");
        Require(normalized == "PrioritizeTraditional", $"Unexpected normalized strategy: {normalized}");
        return Task.CompletedTask;
    }

    private static Task FailedDetectionResultIsNeutral()
    {
        var okProbability = InvokeStatic<double>("ToOkProbability", ServiceDetectionResult.Failed("missing"));
        RequireClose(okProbability, 0.5, "Failed-result OK probability");
        return Task.CompletedTask;
    }

    private static async Task<ClearVision.Product.Core.Operators.OperatorExecutionOutput> ExecuteAsync(Operator op, object? dlResult, object? traditionalResult)
    {
        var inputs = new Dictionary<string, object>();
        if (dlResult is not null)
        {
            inputs["DLResult"] = dlResult;
        }

        if (traditionalResult is not null)
        {
            inputs["TraditionalResult"] = traditionalResult;
        }

        return await ExecuteRawAsync(op, inputs);
    }

    private static async Task<ClearVision.Product.Core.Operators.OperatorExecutionOutput> ExecuteRawAsync(Operator op, Dictionary<string, object> inputs)
    {
        return await Operator.ExecuteAsync(op, inputs);
    }

    private static Operator CreateOperator(
        string strategy = "WeightedAverage",
        double dlWeight = 0.6,
        double traditionalWeight = 0.4,
        double confidenceThreshold = 0.5,
        string okValue = "1",
        string ngValue = "0")
    {
        var op = new Operator("dual_modal_voting_contract", OperatorType.DualModalVoting, 0, 0);
        AddParameter(op, "VotingStrategy", strategy, "enum");
        AddParameter(op, "DLWeight", dlWeight, "double");
        AddParameter(op, "TraditionalWeight", traditionalWeight, "double");
        AddParameter(op, "ConfidenceThreshold", confidenceThreshold, "double");
        AddParameter(op, "OkOutputValue", okValue, "string");
        AddParameter(op, "NgOutputValue", ngValue, "string");
        return op;
    }

    private static void AddParameter(Operator op, string name, object? value, string dataType)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
    }

    private static void AssertOutput(
        ClearVision.Product.Core.Operators.OperatorExecutionOutput result,
        bool expectedIsOk,
        double expectedConfidence,
        string expectedJudgmentValue)
    {
        Require(result.IsSuccess, result.ErrorMessage ?? "Expected success.");
        var output = result.OutputData ?? throw new InvalidOperationException("Expected output data.");
        Require(Convert.ToBoolean(output["IsOk"]) == expectedIsOk, $"Expected IsOk={expectedIsOk}, got {output["IsOk"]}.");
        RequireClose(Convert.ToDouble(output["Confidence"]), expectedConfidence, "Confidence");
        Require((string)output["JudgmentValue"] == expectedJudgmentValue, $"Expected JudgmentValue={expectedJudgmentValue}, got {output["JudgmentValue"]}.");
    }

    private static T? InvokeStatic<T>(string name, params object?[] args)
    {
        var method = typeof(DualModalVotingOperator).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DualModalVotingOperator), name);
        return (T?)method.Invoke(null, args);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireContains(string? value, string expected)
    {
        Require(value?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true, $"Expected '{value}' to contain '{expected}'.");
    }

    private static void RequireClose(double actual, double expected, string label, double tolerance = 1e-3)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static string? ValidationErrors(ClearVision.Product.Core.Operators.ValidationResult validation)
    {
        return validation.Errors.Count == 0 ? null : string.Join("; ", validation.Errors);
    }

    private static string FormatException(Exception ex)
    {
        if (ex is TargetInvocationException { InnerException: not null } tie)
        {
            var inner = tie.InnerException!;
            return $"{ex.GetType().Name}: {ex.Message} Inner={inner.GetType().Name}: {inner.Message}";
        }

        return ex.GetType().Name + ": " + ex.Message;
    }
}

internal sealed record ContractCase(string Name, string Scenario, Func<Task> Body);

internal sealed record BaselineResult(
    BaselineSummary Summary,
    OperatorEvidence[] Operators,
    ScenarioSummary[] Scenarios,
    CaseResult[] Cases);

internal sealed record BaselineSummary(
    DateTimeOffset GeneratedAtUtc,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMs);

internal sealed record OperatorEvidence(
    string Operator,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string Case,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? Failure);

internal sealed record RunnerOptions(string OutputPath, string? ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var outputPath = "quality/evals/reports/DualModalVoting_contract_baseline.json";
        string? reportPath = "quality/evals/reports/DualModalVoting_contract_baseline.md";
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--output":
                    if (++i >= args.Length)
                    {
                        return new RunnerOptions(outputPath, reportPath, false, "--output requires a path.");
                    }

                    outputPath = args[i];
                    break;
                case "--report":
                    if (++i >= args.Length)
                    {
                        return new RunnerOptions(outputPath, reportPath, false, "--report requires a path.");
                    }

                    reportPath = args[i];
                    break;
                case "--no-report":
                    reportPath = null;
                    break;
                default:
                    return new RunnerOptions(outputPath, reportPath, false, $"Unknown argument: {arg}");
            }
        }

        return new RunnerOptions(outputPath, reportPath, showHelp, null);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run --project quality/tools/DualModalVotingContractRunner/DualModalVotingContractRunner.csproj -- [--output PATH] [--report PATH] [--no-report]");
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# DualModalVoting Contract Baseline",
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
            $"| Runtime ms | {result.Summary.RuntimeMs:F3} |",
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(s =>
            $"| {s.Scenario} | {s.CaseCount} | {s.Passed} | {s.Failed} | {s.RuntimeMsAvg:F3} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Passed | Runtime ms | Failure |",
            "| --- | --- | --- | ---: | --- |"
        ]);

        lines.AddRange(result.Cases.Select(c =>
            $"| {c.Case} | {c.Scenario} | {c.Passed} | {c.RuntimeMs:F3} | {Escape(c.Failure)} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Notes",
            string.Empty,
            "- This is a pure contract baseline for dual-modal decision fusion using controlled DetectionResult and dictionary inputs.",
            "- It validates all voting strategies, OK-probability conversion, missing-input behavior, DefectCount extraction, custom judgment values, strategy parsing, and validation failures.",
            "- It does not claim vision-model accuracy; it locks the decision contract that consumes upstream model and rule outputs."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
