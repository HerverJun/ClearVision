using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

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
    $"FrameChangeTrigger contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private static readonly FrameChangeTriggerOperator Operator = new(NullLogger<FrameChangeTriggerOperator>.Instance);

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<ContractCase>
        {
            new("first_frame_builds_baseline_and_short_circuits", "baseline and short-circuit", FirstFrameBuildsBaseline),
            new("large_area_change_triggers_and_continues", "baseline and short-circuit", LargeAreaChangeTriggers),
            new("small_change_below_threshold_short_circuits", "baseline and short-circuit", SmallChangeBelowThreshold),
            new("cooldown_suppresses_duplicate_arrival", "baseline and short-circuit", CooldownSuppressesDuplicate),
            new("disabled_passthrough_does_not_short_circuit", "enablement", DisabledPassthrough),
            new("short_circuit_false_passes_untriggered_frame", "enablement", ShortCircuitFalsePassesUntriggered),
            new("missing_image_fails_with_stable_message", "input failure", MissingImageFails),
            new("empty_image_fails_with_stable_message", "input failure", EmptyImageFails),
            new("invalid_pixel_threshold_low_rejected", "parameter validation", () => InvalidParam("PixelThreshold", 0, "PixelThreshold")),
            new("invalid_pixel_threshold_high_rejected", "parameter validation", () => InvalidParam("PixelThreshold", 256, "PixelThreshold")),
            new("invalid_min_change_ratio_low_rejected", "parameter validation", () => InvalidParam("MinChangeRatio", -0.1, "MinChangeRatio")),
            new("invalid_min_change_ratio_high_rejected", "parameter validation", () => InvalidParam("MinChangeRatio", 1.1, "MinChangeRatio")),
            new("invalid_min_change_pixels_rejected", "parameter validation", () => InvalidParam("MinChangePixels", -1, "MinChangePixels")),
            new("invalid_cooldown_low_rejected", "parameter validation", () => InvalidParam("CooldownMs", -1, "CooldownMs")),
            new("invalid_cooldown_high_rejected", "parameter validation", () => InvalidParam("CooldownMs", 60_001, "CooldownMs")),
            new("invalid_roi_negative_rejected", "parameter validation", () => InvalidParam("RoiX", -1, "RoiX")),
            new("invalid_enabled_type_rejected", "parameter validation", () => InvalidParam("Enabled", "not_bool", "Enabled")),
            new("invalid_short_circuit_type_rejected", "parameter validation", () => InvalidParam("ShortCircuitWhenNotTriggered", "not_bool", "ShortCircuitWhenNotTriggered")),
            new("invalid_normalize_mode_rejected", "parameter validation", () => InvalidParam("NormalizeMode", "Invalid", "NormalizeMode")),
            new("invalid_reference_update_mode_rejected", "parameter validation", () => InvalidParam("ReferenceUpdateMode", "Invalid", "ReferenceUpdateMode")),
            new("invalid_blur_size_even_rejected", "parameter validation", () => InvalidParam("BlurSize", 4, "BlurSize")),
            new("invalid_reference_alpha_rejected", "parameter validation", () => InvalidParam("ReferenceUpdateAlpha", 1.5, "ReferenceUpdateAlpha")),
            new("roi_clamps_to_image_boundary", "roi boundary", RoiClampsToImageBoundary),
            new("output_fields_are_complete", "output contract", OutputFieldsComplete),
            new("operator_instances_keep_independent_baselines", "state isolation", OperatorInstancesKeepIndependentBaselines),
            new("dispose_clears_state_for_long_running_reuse", "state isolation", DisposeClearsState),
            new("mean_shift_lighting_drift_does_not_trigger", "robustness", MeanShiftLightingDriftDoesNotTrigger),
            new("noise_guard_suppresses_salt_pepper_noise", "robustness", NoiseGuardSuppressesSaltPepper),
            new("min_consecutive_changed_frames_suppresses_single_flash", "trigger semantics", MinConsecutiveSuppressesSingleFlash),
            new("rising_edge_only_suppresses_sustained_change", "trigger semantics", RisingEdgeOnlySuppressesSustainedChange),
            new("reset_after_no_change_allows_second_arrival", "trigger semantics", ResetAfterNoChangeAllowsSecondArrival)
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

        var evidence = new[]
        {
            new OperatorEvidence(
                "FrameChangeTrigger",
                "contract",
                results.Count,
                passed,
                failed,
                Math.Round(results.Average(x => x.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(results.Average(x => x.MemoryAllocationBytes))),
                InputTypes: ["ImageWrapper", "Mat"],
                OutputFields: [
                    "Image",
                    "Triggered",
                    "ChangeScore",
                    "ChangedPixels",
                    "Reason",
                    "BaselineReady",
                    "TotalPixels",
                    "CooldownRemainingMs",
                    "EffectivePixelThreshold",
                    "EffectiveMinChangeRatio"
                ])
        };

        return new BaselineResult(
            "contract",
            "FrameChangeTrigger_contract_baseline",
            summary,
            evidence,
            scenarioSummaries,
            results.ToArray());
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
            return new CaseResult(
                contractCase.Name,
                contractCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                contractCase.Name,
                contractCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static async Task FirstFrameBuildsBaseline()
    {
        var result = await ExecuteAsync(CreateOperator(), CreateGray(32, 32, 20));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(result.ShouldShortCircuitFlow, "First frame should short-circuit.");
        RequireOutput(result, "Triggered", false);
        RequireOutput(result, "Reason", "baseline");
        RequireOutput(result, "BaselineReady", true);
    }

    private static async Task LargeAreaChangeTriggers()
    {
        var op = CreateOperator(new() { ["CooldownMs"] = 0, ["MinChangeRatio"] = 0.10, ["MinChangePixels"] = 100 });
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 20)))) { }

        var result = await ExecuteAsync(op, CreateGray(32, 32, 230));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(!result.ShouldShortCircuitFlow, "Triggered frame should continue downstream.");
        RequireOutput(result, "Triggered", true);
        RequireOutput(result, "Reason", "change_detected");
        Require(Convert.ToDouble(Output(result, "ChangeScore")) > 0.9, "Expected high ChangeScore.");
    }

    private static async Task SmallChangeBelowThreshold()
    {
        var op = CreateOperator(new() { ["MinChangeRatio"] = 0.5, ["MinChangePixels"] = 900 });
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 20)))) { }

        var result = await ExecuteAsync(op, CreatePatch(32, 32, 20, new Rect(0, 0, 3, 3), 240));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(result.ShouldShortCircuitFlow, "Below-threshold frame should short-circuit.");
        RequireOutput(result, "Triggered", false);
        RequireOutput(result, "Reason", "below_threshold");
    }

    private static async Task CooldownSuppressesDuplicate()
    {
        var op = CreateOperator(new() { ["CooldownMs"] = 10_000, ["MinChangeRatio"] = 0.10, ["MinChangePixels"] = 100 });
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 20)))) { }
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 230)))) { }

        var result = await ExecuteAsync(op, CreateGray(32, 32, 20));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(result.ShouldShortCircuitFlow, "Cooldown duplicate should short-circuit.");
        RequireOutput(result, "Triggered", false);
        RequireOutput(result, "Reason", "cooldown");
        Require(Convert.ToInt32(Output(result, "CooldownRemainingMs")) > 0, "Expected positive cooldown remaining.");
    }

    private static async Task DisabledPassthrough()
    {
        var result = await ExecuteAsync(CreateOperator(new() { ["Enabled"] = false }), CreateGray(32, 32, 20));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(!result.ShouldShortCircuitFlow, "Disabled operator should pass downstream.");
        RequireOutput(result, "Triggered", true);
        RequireOutput(result, "Reason", "disabled");
    }

    private static async Task ShortCircuitFalsePassesUntriggered()
    {
        var op = CreateOperator(new() { ["ShortCircuitWhenNotTriggered"] = false, ["MinChangeRatio"] = 0.9, ["MinChangePixels"] = 900 });
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 20)))) { }

        var result = await ExecuteAsync(op, CreateGray(32, 32, 21));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        Require(!result.ShouldShortCircuitFlow, "ShortCircuitWhenNotTriggered=false should continue downstream.");
        RequireOutput(result, "Triggered", false);
    }

    private static Task MissingImageFails()
    {
        return AssertFailureAsync(CreateOperator(), new Dictionary<string, object>(), "输入图像");
    }

    private static Task EmptyImageFails()
    {
        return AssertFailureAsync(CreateOperator(), new Dictionary<string, object> { ["Image"] = new ImageWrapper(new Mat()) }, "输入图像为空");
    }

    private static Task InvalidParam(string name, object value, string expectedMessage)
    {
        var validation = Operator.ValidateParameters(CreateOperator(new() { [name] = value }));
        Require(!validation.IsValid, $"{name} should be invalid.");
        RequireContains(string.Join("; ", validation.Errors), expectedMessage);
        return Task.CompletedTask;
    }

    private static async Task RoiClampsToImageBoundary()
    {
        var op = CreateOperator(new() { ["RoiX"] = 99, ["RoiY"] = 99, ["RoiW"] = 50, ["RoiH"] = 50 });
        var result = await ExecuteAsync(op, CreateGray(16, 16, 20));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        RequireOutput(result, "RoiX", 15);
        RequireOutput(result, "RoiY", 15);
        RequireOutput(result, "RoiW", 1);
        RequireOutput(result, "RoiH", 1);
    }

    private static async Task OutputFieldsComplete()
    {
        var result = await ExecuteAsync(CreateOperator(), CreateGray(32, 32, 20));
        using var cleanup = OutputCleanup.From(result);
        RequireSuccess(result);
        var output = result.OutputData ?? throw new InvalidOperationException("Expected output data.");
        foreach (var field in new[]
        {
            "Image",
            "Triggered",
            "ChangeScore",
            "ChangedPixels",
            "Reason",
            "BaselineReady",
            "TotalPixels",
            "CooldownRemainingMs",
            "EffectivePixelThreshold",
            "EffectiveMinChangeRatio",
            "RoiX",
            "RoiY",
            "RoiW",
            "RoiH",
            "StateScope",
            "StateKey",
            "NoMaterialFrame"
        })
        {
            Require(output.ContainsKey(field), $"Missing output field: {field}");
        }
    }

    private static async Task OperatorInstancesKeepIndependentBaselines()
    {
        var parameters = new Dictionary<string, object> { ["CooldownMs"] = 0, ["MinChangeRatio"] = 0.1, ["MinChangePixels"] = 100 };
        var first = CreateOperator(parameters);
        var second = CreateOperator(parameters);

        using (OutputCleanup.From(await ExecuteAsync(first, CreateGray(32, 32, 20)))) { }
        var secondFirst = await ExecuteAsync(second, CreateGray(32, 32, 230));
        using var secondCleanup = OutputCleanup.From(secondFirst);
        RequireOutput(secondFirst, "Reason", "baseline");

        var firstSecond = await ExecuteAsync(first, CreateGray(32, 32, 230));
        using var firstCleanup = OutputCleanup.From(firstSecond);
        RequireOutput(firstSecond, "Triggered", true);
    }

    private static async Task DisposeClearsState()
    {
        var op = CreateOperator(new() { ["CooldownMs"] = 0, ["MinChangeRatio"] = 0.1, ["MinChangePixels"] = 100 });
        using (OutputCleanup.From(await ExecuteAsync(op, CreateGray(32, 32, 20)))) { }

        Operator.Dispose();

        var result = await ExecuteAsync(op, CreateGray(32, 32, 230));
        using var cleanup = OutputCleanup.From(result);
        RequireOutput(result, "Reason", "baseline");
    }

    private static Task MeanShiftLightingDriftDoesNotTrigger()
    {
        using var baseline = CreateGray(64, 64, 80);
        using var drifted = CreateGray(64, 64, 125);
        using var first = FrameChangeTriggerKernel.BuildGrayRoi(baseline, new Rect(0, 0, 64, 64), FrameChangeTriggerOptions.LineFastDefault);
        var options = FrameChangeTriggerOptions.LineNoiseGuard with
        {
            PixelThreshold = 10,
            MinChangePixels = 100,
            MinChangeRatio = 0.02,
            MinConsecutiveChangedFrames = 1
        };
        using var second = FrameChangeTriggerKernel.BuildGrayRoi(drifted, new Rect(0, 0, 64, 64), options);
        using var state = new FrameChangeTriggerKernelState();
        _ = FrameChangeTriggerKernel.Evaluate(state, first, options, DateTime.UtcNow);
        var decision = FrameChangeTriggerKernel.Evaluate(state, second, options, DateTime.UtcNow.AddMilliseconds(100));
        Require(!decision.Triggered, "Mean-shift global lighting drift should not trigger.");
        Require(decision.Reason == "below_threshold", $"Expected below_threshold, got {decision.Reason}.");
        return Task.CompletedTask;
    }

    private static Task NoiseGuardSuppressesSaltPepper()
    {
        var options = FrameChangeTriggerOptions.LineNoiseGuard with
        {
            PixelThreshold = 30,
            MinChangePixels = 80,
            MinChangeRatio = 0.02,
            MinConsecutiveChangedFrames = 1
        };
        using var baseline = CreateGray(64, 64, 80);
        using var noisy = CreateSaltPepper(64, 64, 80, 60, seed: 42);
        using var first = FrameChangeTriggerKernel.BuildGrayRoi(baseline, new Rect(0, 0, 64, 64), options);
        using var second = FrameChangeTriggerKernel.BuildGrayRoi(noisy, new Rect(0, 0, 64, 64), options);
        using var state = new FrameChangeTriggerKernelState();
        _ = FrameChangeTriggerKernel.Evaluate(state, first, options, DateTime.UtcNow);
        var decision = FrameChangeTriggerKernel.Evaluate(state, second, options, DateTime.UtcNow.AddMilliseconds(100));
        Require(!decision.Triggered, "Noise guard should suppress sparse salt-pepper noise.");
        return Task.CompletedTask;
    }

    private static Task MinConsecutiveSuppressesSingleFlash()
    {
        var options = FrameChangeTriggerOptions.LineFastDefault with
        {
            CooldownMs = 0,
            MinConsecutiveChangedFrames = 2,
            ReferenceUpdateMode = FrameChangeReferenceUpdateMode.StableBackground,
            MinChangePixels = 100,
            MinChangeRatio = 0.05
        };
        using var state = new FrameChangeTriggerKernelState();
        using var baseline = CreateGray(64, 64, 80);
        using var flash = CreatePatch(64, 64, 80, new Rect(20, 20, 20, 20), 210);
        using var first = FrameChangeTriggerKernel.BuildGrayRoi(baseline, new Rect(0, 0, 64, 64), options);
        using var second = FrameChangeTriggerKernel.BuildGrayRoi(flash, new Rect(0, 0, 64, 64), options);
        _ = FrameChangeTriggerKernel.Evaluate(state, first, options, DateTime.UtcNow);
        var decision = FrameChangeTriggerKernel.Evaluate(state, second, options, DateTime.UtcNow.AddMilliseconds(100));
        Require(!decision.Triggered, "First changed frame should warm up.");
        Require(decision.Reason == "consecutive_warmup", $"Expected consecutive_warmup, got {decision.Reason}.");
        return Task.CompletedTask;
    }

    private static Task RisingEdgeOnlySuppressesSustainedChange()
    {
        var options = FrameChangeTriggerOptions.LineFastDefault with
        {
            CooldownMs = 0,
            ReferenceUpdateMode = FrameChangeReferenceUpdateMode.StableBackground,
            MinChangePixels = 100,
            MinChangeRatio = 0.05,
            TriggerOnRisingEdgeOnly = true
        };
        using var state = new FrameChangeTriggerKernelState();
        using var baseline = CreateGray(64, 64, 80);
        using var objectFrame = CreatePatch(64, 64, 80, new Rect(20, 20, 20, 20), 210);
        using var first = FrameChangeTriggerKernel.BuildGrayRoi(baseline, new Rect(0, 0, 64, 64), options);
        using var second = FrameChangeTriggerKernel.BuildGrayRoi(objectFrame, new Rect(0, 0, 64, 64), options);
        using var third = FrameChangeTriggerKernel.BuildGrayRoi(objectFrame, new Rect(0, 0, 64, 64), options);
        _ = FrameChangeTriggerKernel.Evaluate(state, first, options, DateTime.UtcNow);
        var triggered = FrameChangeTriggerKernel.Evaluate(state, second, options, DateTime.UtcNow.AddMilliseconds(100));
        var suppressed = FrameChangeTriggerKernel.Evaluate(state, third, options, DateTime.UtcNow.AddMilliseconds(200));
        Require(triggered.Triggered, "First changed frame should trigger.");
        Require(!suppressed.Triggered, "Sustained change should be suppressed.");
        Require(suppressed.Reason == "rising_edge_suppressed", $"Expected rising_edge_suppressed, got {suppressed.Reason}.");
        return Task.CompletedTask;
    }

    private static Task ResetAfterNoChangeAllowsSecondArrival()
    {
        var options = FrameChangeTriggerOptions.LineFastDefault with
        {
            CooldownMs = 0,
            ReferenceUpdateMode = FrameChangeReferenceUpdateMode.StableBackground,
            MinChangePixels = 100,
            MinChangeRatio = 0.05,
            ResetAfterNoChangeFrames = 1,
            TriggerOnRisingEdgeOnly = true
        };
        using var state = new FrameChangeTriggerKernelState();
        using var empty = CreateGray(64, 64, 80);
        using var objectFrame = CreatePatch(64, 64, 80, new Rect(20, 20, 20, 20), 210);
        using var first = FrameChangeTriggerKernel.BuildGrayRoi(empty, new Rect(0, 0, 64, 64), options);
        using var second = FrameChangeTriggerKernel.BuildGrayRoi(objectFrame, new Rect(0, 0, 64, 64), options);
        using var third = FrameChangeTriggerKernel.BuildGrayRoi(empty, new Rect(0, 0, 64, 64), options);
        using var fourth = FrameChangeTriggerKernel.BuildGrayRoi(objectFrame, new Rect(0, 0, 64, 64), options);

        var now = DateTime.UtcNow;
        _ = FrameChangeTriggerKernel.Evaluate(state, first, options, now);
        var firstArrival = FrameChangeTriggerKernel.Evaluate(state, second, options, now.AddMilliseconds(100));
        _ = FrameChangeTriggerKernel.Evaluate(state, third, options, now.AddMilliseconds(200));
        var secondArrival = FrameChangeTriggerKernel.Evaluate(state, fourth, options, now.AddMilliseconds(300));

        Require(firstArrival.Triggered, "First arrival should trigger.");
        Require(secondArrival.Triggered, "Second arrival after no-change reset should trigger.");
        return Task.CompletedTask;
    }

    private static async Task AssertFailureAsync(Operator op, Dictionary<string, object> inputs, string expected)
    {
        var result = await Operator.ExecuteAsync(op, inputs);
        using var cleanup = OutputCleanup.From(result);
        Require(!result.IsSuccess, "Expected failure.");
        RequireContains(result.ErrorMessage, expected);
    }

    private static async Task<OperatorExecutionOutput> ExecuteAsync(Operator op, Mat mat)
    {
        return await Operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = new ImageWrapper(mat) });
    }

    private static Operator CreateOperator(Dictionary<string, object>? parameters = null)
    {
        var op = new Operator("FrameChangeTrigger", OperatorType.FrameChangeTrigger, 0, 0);
        if (parameters == null)
        {
            return op;
        }

        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, value.GetType().Name, value));
        }

        return op;
    }

    private static Mat CreateGray(int width, int height, byte value)
    {
        return new Mat(height, width, MatType.CV_8UC1, new Scalar(value));
    }

    private static Mat CreatePatch(int width, int height, byte background, Rect patch, byte value)
    {
        var mat = CreateGray(width, height, background);
        Cv2.Rectangle(mat, patch, new Scalar(value), thickness: -1);
        return mat;
    }

    private static Mat CreateSaltPepper(int width, int height, byte background, int count, int seed)
    {
        var mat = CreateGray(width, height, background);
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(width);
            var y = random.Next(height);
            mat.Set(y, x, (byte)(i % 2 == 0 ? 255 : 0));
        }

        return mat;
    }

    private static object Output(OperatorExecutionOutput result, string key)
    {
        if (result.OutputData == null || !result.OutputData.TryGetValue(key, out var value))
        {
            throw new InvalidOperationException("Missing output: " + key);
        }

        return value;
    }

    private static void RequireOutput(OperatorExecutionOutput result, string key, object expected)
    {
        var actual = Output(result, key);
        Require(Equals(actual, expected), $"Expected {key}={expected}, got {actual}.");
    }

    private static void RequireSuccess(OperatorExecutionOutput result)
    {
        Require(result.IsSuccess, result.ErrorMessage ?? "Expected success.");
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
}

internal sealed record ContractCase(string Name, string Scenario, Func<Task> Body);

internal sealed record BaselineResult(
    string EvidenceKind,
    string BaselineId,
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
    string EvidenceKind,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    long MemoryAllocationBytesAvg,
    string[] InputTypes,
    string[] OutputFields);

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
        var outputPath = "quality/evals/reports/FrameChangeTrigger_contract_baseline.json";
        string? reportPath = "quality/evals/reports/FrameChangeTrigger_contract_baseline.md";
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
        Console.WriteLine("Usage: dotnet run --project quality/tools/FrameChangeTriggerContractRunner/FrameChangeTriggerContractRunner.csproj -- [--output PATH] [--report PATH] [--no-report]");
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# FrameChangeTrigger Contract Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "EvidenceKind: `contract`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:F3} |",
            $"| Avg runtime ms | {result.Operators[0].RuntimeMsAvg:F3} |",
            $"| Avg memory bytes | {result.Operators[0].MemoryAllocationBytesAvg} |",
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
            "- Ordinary xUnit tests remain product regression tests; this runner is the accepted quality contract evidence source for the matrix.",
            "- Contract coverage includes baseline short-circuit behavior, trigger/pass-through semantics, cooldown, ROI clamping, validation failures, output-field completeness, state isolation, and default-off robustness knobs.",
            "- This report is deterministic synthetic contract evidence; it is not a real production-site sign-off."
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

internal sealed class OutputCleanup : IDisposable
{
    private readonly ImageWrapper? _image;

    private OutputCleanup(ImageWrapper? image)
    {
        _image = image;
    }

    public static OutputCleanup From(OperatorExecutionOutput output)
    {
        return new OutputCleanup(output.OutputData != null &&
            output.OutputData.TryGetValue("Image", out var value) &&
            value is ImageWrapper image
                ? image
                : null);
    }

    public void Dispose()
    {
        _image?.Dispose();
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
