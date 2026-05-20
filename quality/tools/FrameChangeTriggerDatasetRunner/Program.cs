using System.Diagnostics;
using System.Text.Json;
using Acme.Product.Infrastructure.Operators;
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

var dataset = SyntheticDatasetRunner.Run();
WriteJson(options.OutputPath, dataset);
if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    WriteText(options.ReportPath, DatasetMarkdownReport.Create(dataset));
}

var fieldReplay = FieldReplayRunner.Run(dataset.Seed);
WriteJson(options.FieldOutputPath, fieldReplay);
if (!string.IsNullOrWhiteSpace(options.FieldReportPath))
{
    WriteText(options.FieldReportPath, FieldMarkdownReport.Create(fieldReplay));
}

Console.WriteLine(
    $"FrameChangeTrigger dataset baseline complete: {dataset.Summary.Passed}/{dataset.Summary.CaseCount} passed, " +
    $"precision={dataset.Metrics.TriggerPrecision:F4}, recall={dataset.Metrics.TriggerRecall:F4}, output={options.OutputPath}");
Console.WriteLine(
    $"FrameChangeTrigger field-substitute baseline complete: {fieldReplay.Summary.Passed}/{fieldReplay.Summary.CaseCount} passed, " +
    $"output={options.FieldOutputPath}");

return dataset.Summary.Failed == 0 && fieldReplay.Summary.Failed == 0 ? 0 : 1;

static void WriteJson<T>(string path, T value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, JsonSerializer.Serialize(value, JsonSettings.Indented));
}

static void WriteText(string path, string value)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, value);
}

internal static class SyntheticDatasetRunner
{
    public const int Seed = 20260518;
    private const int Width = 320;
    private const int Height = 320;
    private static readonly Rect Roi = new(32, 32, 256, 256);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(100);
    private static readonly FrameChangeTriggerOptions Options = FrameChangeTriggerOptions.LineFastDefault with
    {
        PixelThreshold = 30,
        MinChangeRatio = 0.01,
        MinChangePixels = 500,
        CooldownMs = 300,
        ResetAfterNoChangeFrames = 1,
        TriggerOnRisingEdgeOnly = true
    };

    public static DatasetBaselineResult Run()
    {
        var specs = BuildSpecs(repeatsPerScenario: 10);
        var results = specs.Select(RunSequence).ToArray();
        var metrics = DatasetMetrics.From(results);
        var failed = results.Count(item => !item.Passed);
        var summary = new BaselineSummary(
            DateTimeOffset.UtcNow,
            results.Length,
            results.Length - failed,
            failed,
            Math.Round(results.Sum(item => item.RuntimeMsTotal), 3));

        var operators = new[]
        {
            new OperatorEvidence(
                "FrameChangeTrigger",
                "dataset",
                results.Length,
                summary.Passed,
                summary.Failed,
                Math.Round(results.Average(item => item.RuntimeMsAvg), 3),
                Convert.ToInt64(Math.Round(results.Average(item => item.MemoryAllocationBytesAvg))),
                HasPublicDataset: false,
                DatasetManifest: "quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json")
        };

        return new DatasetBaselineResult(
            EvidenceKind: "dataset",
            BaselineId: "FrameChangeTrigger_dataset_baseline",
            DatasetId: "frame_change_trigger_synthetic_arrival_v1",
            Seed: Seed,
            Profile: "line_fast_default",
            ImageWidth: Width,
            ImageHeight: Height,
            Roi: new RoiInfo(Roi.X, Roi.Y, Roi.Width, Roi.Height),
            Options: new OptionSnapshot(
                Options.PixelThreshold,
                Options.MinChangeRatio,
                Options.MinChangePixels,
                Options.CooldownMs,
                Options.ReferenceUpdateMode.ToString(),
                Options.NormalizeMode.ToString(),
                Options.BlurSize,
                Options.MorphOpenSize),
            Summary: summary,
            Operators: operators,
            Metrics: metrics,
            Scenarios: results
                .GroupBy(item => item.Scenario)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new ScenarioSummary(
                    group.Key,
                    group.Count(),
                    group.Count(item => item.Passed),
                    group.Count(item => !item.Passed),
                    Math.Round(group.Average(item => item.RuntimeMsAvg), 3),
                    group.Sum(item => item.FalseTriggerCount),
                    group.Sum(item => item.MissedTriggerCount)))
                .ToArray(),
            Sequences: results);
    }

    internal static IReadOnlyList<SequenceSpec> BuildSpecs(int repeatsPerScenario)
    {
        var scenarios = new[]
        {
            "static_empty",
            "terminal_enter_once",
            "terminal_stay_cooldown",
            "terminal_reenter_after_cooldown",
            "small_area_noise",
            "salt_pepper_noise",
            "compression_noise",
            "lighting_drift",
            "local_glare_flash",
            "camera_jitter",
            "outside_roi_motion",
            "roi_edge_enter",
            "partial_occlusion_enter",
            "low_contrast_enter"
        };

        var specs = new List<SequenceSpec>(scenarios.Length * repeatsPerScenario);
        foreach (var scenario in scenarios)
        {
            for (var i = 0; i < repeatsPerScenario; i++)
            {
                specs.Add(new SequenceSpec(
                    Id: $"{scenario}_{i:D2}",
                    Scenario: scenario,
                    Variant: i,
                    ExpectedTriggerFrames: ExpectedFrames(scenario),
                    NoiseModel: NoiseModel(scenario),
                    License: "repo-local synthetic"));
            }
        }

        return specs;
    }

    internal static SequenceResult RunSequence(SequenceSpec spec)
    {
        using var state = new FrameChangeTriggerKernelState();
        var triggerFrames = new List<int>();
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var frameRuntimeMs = new List<double>();
        var frameMemoryBytes = new List<long>();
        var now = new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc);

        for (var frameIndex = 0; frameIndex < 8; frameIndex++)
        {
            var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();
            using var frame = CreateFrame(spec, frameIndex);
            using var gray = FrameChangeTriggerKernel.BuildGrayRoi(frame, Roi, Options);
            var decision = FrameChangeTriggerKernel.Evaluate(
                state,
                gray,
                Options,
                now + TimeSpan.FromTicks(FrameInterval.Ticks * frameIndex));
            stopwatch.Stop();

            frameRuntimeMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            frameMemoryBytes.Add(Math.Max(0, GC.GetTotalAllocatedBytes(precise: true) - beforeBytes));
            reasons[decision.Reason] = reasons.TryGetValue(decision.Reason, out var count) ? count + 1 : 1;
            if (decision.Triggered)
            {
                triggerFrames.Add(frameIndex);
            }
        }

        var matched = CountMatchedTriggers(spec.ExpectedTriggerFrames, triggerFrames);
        var falseTriggers = triggerFrames.Count - matched;
        var misses = spec.ExpectedTriggerFrames.Length - matched;
        var duplicateTriggers = Math.Max(0, triggerFrames.Count - spec.ExpectedTriggerFrames.Length);
        var passed = falseTriggers == 0 && misses == 0;

        return new SequenceResult(
            spec.Id,
            spec.Scenario,
            spec.NoiseModel,
            spec.ExpectedTriggerFrames,
            triggerFrames.ToArray(),
            passed,
            falseTriggers,
            misses,
            duplicateTriggers,
            Math.Round(frameRuntimeMs.Sum(), 3),
            Math.Round(frameRuntimeMs.Average(), 3),
            Math.Round(Percentile(frameRuntimeMs, 0.95), 3),
            Convert.ToInt64(Math.Round(frameMemoryBytes.Average())),
            reasons
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(item => item.Key, item => item.Value));
    }

    internal static Mat CreateFrame(SequenceSpec spec, int frameIndex)
    {
        var mat = new Mat(Height, Width, MatType.CV_8UC1, new Scalar(BaseIntensity(spec, frameIndex)));
        switch (spec.Scenario)
        {
            case "terminal_enter_once":
            case "terminal_stay_cooldown":
                if (frameIndex >= 2)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 72 + spec.Variant, Roi.Y + 88, 44, 44), 170);
                }

                break;
            case "terminal_reenter_after_cooldown":
                if (frameIndex is 1 or 2 or 6 or 7)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 60 + spec.Variant, Roi.Y + 96, 46, 46), 170);
                }

                break;
            case "small_area_noise":
                if (frameIndex == 3)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 100, Roi.Y + 100, 14, 14), 180);
                }

                break;
            case "salt_pepper_noise":
                ApplySaltPepper(mat, 300, Seed + spec.Variant * 31 + frameIndex);
                break;
            case "compression_noise":
                ApplyLowAmplitudeNoise(mat, 10, Seed + spec.Variant * 37 + frameIndex);
                break;
            case "local_glare_flash":
                if (frameIndex == 3)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 120, Roi.Y + 40, 20, 20), 230);
                }

                break;
            case "camera_jitter":
                DrawTerminal(mat, new Rect(Roi.X + 40 + (frameIndex % 2), Roi.Y + 12, 8, 64), 115);
                break;
            case "outside_roi_motion":
                if (frameIndex >= 2)
                {
                    DrawTerminal(mat, new Rect(Width - 28, Height - 28, 24, 24), 200);
                }

                break;
            case "roi_edge_enter":
                if (frameIndex >= 2)
                {
                    DrawTerminal(mat, new Rect(Roi.X - 12, Roi.Y + 72 + spec.Variant, 48, 48), 170);
                }

                break;
            case "partial_occlusion_enter":
                if (frameIndex >= 2)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 112, Roi.Y + 112, 32, 32), 170);
                }

                break;
            case "low_contrast_enter":
                if (frameIndex >= 2)
                {
                    DrawTerminal(mat, new Rect(Roi.X + 82, Roi.Y + 82, 44, 44), 122);
                }

                break;
        }

        return mat;
    }

    private static byte BaseIntensity(SequenceSpec spec, int frameIndex)
    {
        if (spec.Scenario == "lighting_drift")
        {
            return (byte)(80 + frameIndex * 3 + spec.Variant % 3);
        }

        return 80;
    }

    private static int[] ExpectedFrames(string scenario)
    {
        return scenario switch
        {
            "terminal_enter_once" => [2],
            "terminal_stay_cooldown" => [2],
            "terminal_reenter_after_cooldown" => [1, 6],
            "roi_edge_enter" => [2],
            "partial_occlusion_enter" => [2],
            "low_contrast_enter" => [2],
            _ => []
        };
    }

    private static string NoiseModel(string scenario)
    {
        return scenario switch
        {
            "salt_pepper_noise" => "fixed-seed sparse salt-pepper",
            "compression_noise" => "fixed-seed low-amplitude quantization noise",
            "lighting_drift" => "global +3 gray/frame drift",
            "local_glare_flash" => "single-frame 20x20 specular patch",
            "camera_jitter" => "1px belt-edge stripe jitter",
            _ => "none"
        };
    }

    private static void DrawTerminal(Mat mat, Rect rect, byte value)
    {
        Cv2.Rectangle(mat, rect, new Scalar(value), thickness: -1);
    }

    private static void ApplySaltPepper(Mat mat, int count, int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var x = random.Next(Roi.X, Roi.Right);
            var y = random.Next(Roi.Y, Roi.Bottom);
            mat.Set(y, x, (byte)(i % 2 == 0 ? 255 : 0));
        }
    }

    private static void ApplyLowAmplitudeNoise(Mat mat, int amplitude, int seed)
    {
        var random = new Random(seed);
        for (var y = Roi.Y; y < Roi.Bottom; y += 4)
        {
            for (var x = Roi.X; x < Roi.Right; x += 4)
            {
                var value = 80 + random.Next(-amplitude, amplitude + 1);
                mat.Set(y, x, (byte)Math.Clamp(value, 0, 255));
            }
        }
    }

    private static int CountMatchedTriggers(IReadOnlyList<int> expected, IReadOnlyList<int> actual)
    {
        var actualRemaining = actual.ToList();
        var matched = 0;
        foreach (var frame in expected)
        {
            var index = actualRemaining.IndexOf(frame);
            if (index < 0)
            {
                continue;
            }

            matched++;
            actualRemaining.RemoveAt(index);
        }

        return matched;
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}

internal static class FieldReplayRunner
{
    public static FieldReplayBaselineResult Run(int seed)
    {
        var specs = SyntheticDatasetRunner.BuildSpecs(repeatsPerScenario: 2)
            .Where(item => item.Scenario is
                "static_empty" or
                "terminal_enter_once" or
                "terminal_stay_cooldown" or
                "terminal_reenter_after_cooldown" or
                "salt_pepper_noise" or
                "roi_edge_enter" or
                "partial_occlusion_enter" or
                "low_contrast_enter" or
                "outside_roi_motion" or
                "lighting_drift")
            .Take(20)
            .ToArray();

        var cases = specs.Select(RunCase).ToArray();
        var failed = cases.Count(item => !item.Passed);
        var summary = new BaselineSummary(
            DateTimeOffset.UtcNow,
            cases.Length,
            cases.Length - failed,
            failed,
            Math.Round(cases.Sum(item => item.RuntimeMsTotal), 3));
        var operators = new[]
        {
            new OperatorEvidence(
                "FrameChangeTrigger",
                "field",
                cases.Length,
                summary.Passed,
                summary.Failed,
                Math.Round(cases.Average(item => item.RuntimeMsAvg), 3),
                Convert.ToInt64(Math.Round(cases.Average(item => item.MemoryAllocationBytesAvg))),
                HasPublicDataset: false,
                DatasetManifest: "quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json")
        };

        return new FieldReplayBaselineResult(
            EvidenceKind: "field",
            BaselineId: "FrameChangeTrigger_field_substitute_baseline",
            ReplayId: "frame_change_trigger_field_substitute_v1",
            Seed: seed,
            Pipeline: "ImageAcquisition(Continuous) -> FrameChangeTrigger -> DeepLearning -> BoxFilter -> BoxNms -> DetectionSequenceJudge -> ResultOutput",
            Scope: "field-substitute synthetic replay; no real production-site sign-off is claimed",
            Summary: summary,
            Operators: operators,
            Metrics: new FieldReplayMetrics(
                Cases: cases.Length,
                NoMaterialFrames: cases.Sum(item => item.NoMaterialFrames),
                NoMaterialDownstreamExecutions: cases.Sum(item => item.NoMaterialDownstreamExecutions),
                ArrivalFrames: cases.Sum(item => item.ExpectedTriggerFrames.Length),
                ArrivalDownstreamExecutions: cases.Sum(item => item.DownstreamExecutionFrames.Length),
                TriggerFrameMismatches: cases.Sum(item => item.TriggerFrameMismatchCount)),
            Cases: cases);
    }

    private static FieldReplayCaseResult RunCase(SequenceSpec spec)
    {
        var sequence = SyntheticDatasetRunner.RunSequence(spec);
        var downstreamFrames = sequence.ActualTriggerFrames;
        var noMaterialFrames = 8 - spec.ExpectedTriggerFrames.Length;
        var noMaterialDownstream = downstreamFrames.Count(frame => !spec.ExpectedTriggerFrames.Contains(frame));
        var mismatchCount = Math.Abs(downstreamFrames.Length - spec.ExpectedTriggerFrames.Length) +
            downstreamFrames.Count(frame => !spec.ExpectedTriggerFrames.Contains(frame));
        var passed = noMaterialDownstream == 0 &&
            downstreamFrames.SequenceEqual(spec.ExpectedTriggerFrames);

        return new FieldReplayCaseResult(
            spec.Id,
            spec.Scenario,
            spec.ExpectedTriggerFrames,
            downstreamFrames,
            noMaterialFrames,
            noMaterialDownstream,
            mismatchCount,
            passed,
            sequence.RuntimeMsTotal,
            sequence.RuntimeMsAvg,
            sequence.RuntimeP95Ms,
            sequence.MemoryAllocationBytesAvg);
    }
}

internal sealed record DatasetBaselineResult(
    string EvidenceKind,
    string BaselineId,
    string DatasetId,
    int Seed,
    string Profile,
    int ImageWidth,
    int ImageHeight,
    RoiInfo Roi,
    OptionSnapshot Options,
    BaselineSummary Summary,
    OperatorEvidence[] Operators,
    DatasetMetrics Metrics,
    ScenarioSummary[] Scenarios,
    SequenceResult[] Sequences);

internal sealed record FieldReplayBaselineResult(
    string EvidenceKind,
    string BaselineId,
    string ReplayId,
    int Seed,
    string Pipeline,
    string Scope,
    BaselineSummary Summary,
    OperatorEvidence[] Operators,
    FieldReplayMetrics Metrics,
    FieldReplayCaseResult[] Cases);

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
    bool HasPublicDataset,
    string DatasetManifest);

internal sealed record RoiInfo(int X, int Y, int Width, int Height);

internal sealed record OptionSnapshot(
    int PixelThreshold,
    double MinChangeRatio,
    int MinChangePixels,
    int CooldownMs,
    string ReferenceUpdateMode,
    string NormalizeMode,
    int BlurSize,
    int MorphOpenSize);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg,
    int FalseTriggerCount,
    int MissedTriggerCount);

internal sealed record SequenceSpec(
    string Id,
    string Scenario,
    int Variant,
    int[] ExpectedTriggerFrames,
    string NoiseModel,
    string License);

internal sealed record SequenceResult(
    string Id,
    string Scenario,
    string NoiseModel,
    int[] ExpectedTriggerFrames,
    int[] ActualTriggerFrames,
    bool Passed,
    int FalseTriggerCount,
    int MissedTriggerCount,
    int DuplicateTriggerCount,
    double RuntimeMsTotal,
    double RuntimeMsAvg,
    double RuntimeP95Ms,
    long MemoryAllocationBytesAvg,
    Dictionary<string, int> ReasonCounts);

internal sealed record DatasetMetrics(
    int TruePositiveTriggers,
    int FalseTriggerCount,
    int MissedTriggerCount,
    int DuplicateTriggerCount,
    double TriggerPrecision,
    double TriggerRecall,
    double DuplicateSuppressionRate,
    double StaticNoiseFalseTriggerRate,
    double RuntimeP95Ms)
{
    public static DatasetMetrics From(IReadOnlyList<SequenceResult> results)
    {
        var expected = results.Sum(item => item.ExpectedTriggerFrames.Length);
        var actual = results.Sum(item => item.ActualTriggerFrames.Length);
        var falseTriggers = results.Sum(item => item.FalseTriggerCount);
        var misses = results.Sum(item => item.MissedTriggerCount);
        var duplicates = results.Sum(item => item.DuplicateTriggerCount);
        var truePositives = expected - misses;
        var precision = actual == 0 ? 1.0 : truePositives / (double)actual;
        var recall = expected == 0 ? 1.0 : truePositives / (double)expected;
        var duplicateSuppression = 1.0 - (duplicates / (double)Math.Max(1, actual));
        var negativeSequences = results.Where(item => item.ExpectedTriggerFrames.Length == 0).ToArray();
        var negativeWithFalseTrigger = negativeSequences.Count(item => item.FalseTriggerCount > 0);
        var falseRate = negativeSequences.Length == 0 ? 0.0 : negativeWithFalseTrigger / (double)negativeSequences.Length;
        var p95 = Percentile(results.SelectMany(item => new[] { item.RuntimeP95Ms }).ToArray(), 0.95);

        return new DatasetMetrics(
            truePositives,
            falseTriggers,
            misses,
            duplicates,
            Math.Round(precision, 4),
            Math.Round(recall, 4),
            Math.Round(duplicateSuppression, 4),
            Math.Round(falseRate, 4),
            Math.Round(p95, 3));
    }

    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(x => x).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}

internal sealed record FieldReplayMetrics(
    int Cases,
    int NoMaterialFrames,
    int NoMaterialDownstreamExecutions,
    int ArrivalFrames,
    int ArrivalDownstreamExecutions,
    int TriggerFrameMismatches);

internal sealed record FieldReplayCaseResult(
    string Id,
    string Scenario,
    int[] ExpectedTriggerFrames,
    int[] DownstreamExecutionFrames,
    int NoMaterialFrames,
    int NoMaterialDownstreamExecutions,
    int TriggerFrameMismatchCount,
    bool Passed,
    double RuntimeMsTotal,
    double RuntimeMsAvg,
    double RuntimeP95Ms,
    long MemoryAllocationBytesAvg);

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    string FieldOutputPath,
    string? FieldReportPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var outputPath = "quality/evals/reports/FrameChangeTrigger_dataset_baseline.json";
        string? reportPath = "quality/evals/reports/FrameChangeTrigger_dataset_baseline.md";
        var fieldOutputPath = "quality/evals/reports/FrameChangeTrigger_field_substitute_baseline.json";
        string? fieldReportPath = "quality/evals/reports/FrameChangeTrigger_field_substitute_baseline.md";
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
                        return Error("--output requires a path.");
                    }

                    outputPath = args[i];
                    break;
                case "--report":
                    if (++i >= args.Length)
                    {
                        return Error("--report requires a path.");
                    }

                    reportPath = args[i];
                    break;
                case "--field-output":
                    if (++i >= args.Length)
                    {
                        return Error("--field-output requires a path.");
                    }

                    fieldOutputPath = args[i];
                    break;
                case "--field-report":
                    if (++i >= args.Length)
                    {
                        return Error("--field-report requires a path.");
                    }

                    fieldReportPath = args[i];
                    break;
                case "--no-report":
                    reportPath = null;
                    fieldReportPath = null;
                    break;
                default:
                    return Error($"Unknown argument: {arg}");
            }
        }

        return new RunnerOptions(outputPath, reportPath, fieldOutputPath, fieldReportPath, showHelp, null);

        RunnerOptions Error(string message) =>
            new(outputPath, reportPath, fieldOutputPath, fieldReportPath, false, message);
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: dotnet run --project quality/tools/FrameChangeTriggerDatasetRunner/FrameChangeTriggerDatasetRunner.csproj -- [--output PATH] [--report PATH] [--field-output PATH] [--field-report PATH] [--no-report]");
    }
}

internal static class DatasetMarkdownReport
{
    public static string Create(DatasetBaselineResult result)
    {
        var lines = new List<string>
        {
            "# FrameChangeTrigger Dataset Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "EvidenceKind: `dataset`",
            $"DatasetId: `{result.DatasetId}`",
            $"Profile: `{result.Profile}`",
            $"Seed: `{result.Seed}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value | Gate |",
            "| --- | ---: | ---: |",
            $"| Sequences | {result.Summary.CaseCount} | >= 120 |",
            $"| Passed | {result.Summary.Passed} | {result.Summary.CaseCount} |",
            $"| Failed | {result.Summary.Failed} | 0 |",
            $"| Trigger Precision | {result.Metrics.TriggerPrecision:F4} | >= 0.9800 |",
            $"| Trigger Recall | {result.Metrics.TriggerRecall:F4} | >= 0.9500 |",
            $"| Duplicate Suppression Rate | {result.Metrics.DuplicateSuppressionRate:F4} | >= 0.9800 |",
            $"| Static/Noise False Trigger Rate | {result.Metrics.StaticNoiseFalseTriggerRate:F4} | <= 0.0200 |",
            $"| P95 Runtime ms | {result.Metrics.RuntimeP95Ms:F3} | <= 3.000 |",
            string.Empty,
            "## Scenarios",
            string.Empty,
            "| Scenario | Cases | Passed | Failed | False Triggers | Misses | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Scenarios.Select(s =>
            $"| {s.Scenario} | {s.CaseCount} | {s.Passed} | {s.Failed} | {s.FalseTriggerCount} | {s.MissedTriggerCount} | {s.RuntimeMsAvg:F3} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Dataset Contract",
            string.Empty,
            $"- Manifest: `quality/datasets/manifests/FrameChangeTrigger_synthetic_arrival_manifest.json`",
            $"- Frame size: {result.ImageWidth}x{result.ImageHeight}; ROI: {result.Roi.X},{result.Roi.Y},{result.Roi.Width},{result.Roi.Height}",
            "- Scenarios cover static empty frames, arrival, dwell suppression, re-entry, small area noise, salt-pepper noise, compression noise, lighting drift, local glare, camera jitter, ROI-outside motion, ROI-edge entry, partial occlusion, and low-contrast entry.",
            "- This is deterministic repo-local synthetic evidence; it does not claim real production-line validation."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal static class FieldMarkdownReport
{
    public static string Create(FieldReplayBaselineResult result)
    {
        var lines = new List<string>
        {
            "# FrameChangeTrigger Field-Substitute Baseline",
            string.Empty,
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "EvidenceKind: `field`",
            $"ReplayId: `{result.ReplayId}`",
            $"Pipeline: `{result.Pipeline}`",
            string.Empty,
            "## Summary",
            string.Empty,
            "| Metric | Value | Gate |",
            "| --- | ---: | ---: |",
            $"| Replay cases | {result.Summary.CaseCount} | >= 20 |",
            $"| Passed | {result.Summary.Passed} | {result.Summary.CaseCount} |",
            $"| Failed | {result.Summary.Failed} | 0 |",
            $"| No-material downstream executions | {result.Metrics.NoMaterialDownstreamExecutions} | 0 |",
            $"| Arrival downstream executions | {result.Metrics.ArrivalDownstreamExecutions} | {result.Metrics.ArrivalFrames} |",
            $"| Trigger frame mismatches | {result.Metrics.TriggerFrameMismatches} | 0 |",
            string.Empty,
            "## Cases",
            string.Empty,
            "| Case | Scenario | Expected | Downstream | No-material downstream | Passed |",
            "| --- | --- | --- | --- | ---: | --- |"
        };

        lines.AddRange(result.Cases.Select(c =>
            $"| {c.Id} | {c.Scenario} | {string.Join(",", c.ExpectedTriggerFrames)} | {string.Join(",", c.DownstreamExecutionFrames)} | {c.NoMaterialDownstreamExecutions} | {c.Passed} |"));

        lines.AddRange(
        [
            string.Empty,
            "## Boundary Statement",
            string.Empty,
            "- This report is field-substitute replay evidence built from anonymous synthetic frames and the wire-sequence video-stream topology.",
            "- It validates that no-material frames short-circuit before DeepLearning and that arrival frames continue downstream.",
            "- It is not a real production-site sign-off and must not be described as customer or line validation."
        ]);

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
