using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime.Tensors;

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

var result = PostprocessAbRunner.Run();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
File.WriteAllText(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    File.WriteAllText(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"DeepLearning postprocess A/B complete: accepted={result.Accepted}, " +
    $"cases={result.Summary.CaseCount}, comparisons={result.Summary.ComparisonCount}, output={options.OutputPath}");
return result.Accepted ? 0 : 1;

internal static class PostprocessAbRunner
{
    private const int InputSize = 640;
    private const int ClassCount = 3;
    private const float ConfidenceThreshold = 0.35f;
    private static readonly DeepLearningOperator Operator = new(NullLogger<DeepLearningOperator>.Instance);

    public static PostprocessAbResult Run()
    {
        var cases = BuildCases();
        var results = cases.Select(RunCase).ToList();
        var comparisons = results.SelectMany(item => item.Comparisons).ToList();
        var accepted = results.All(item => item.Accepted);

        return new PostprocessAbResult(
            "2026-04-30.deep-learning-postprocess-ab.v1",
            DateTimeOffset.UtcNow,
            accepted,
            new Summary(
                results.Count,
                comparisons.Count,
                results.Count(item => item.Accepted),
                results.Count(item => !item.Accepted),
                comparisons.Count(item => item.Topic == "nms"),
                comparisons.Count(item => item.Topic == "letterbox"),
                comparisons.Count(item => item.Topic == "clamp")),
            new ClaimBoundary(
                "This report evaluates DeepLearning postprocess behavior only: NMS, letterbox coordinate inversion, and clamp policy.",
                "It does not claim model-training, model-weight, AP, precision, or recall improvement.",
                "Offline variants are candidate evidence and are not production behavior unless promoted in a later algorithm change."),
            results);
    }

    private static IReadOnlyList<PostprocessCase> BuildCases() =>
    [
        new(
            "nms_same_class_overlap",
            320,
            320,
            "nms",
            [
                new CandidateBox(90, 90, 80, 80, 0, 0.96f),
                new CandidateBox(100, 100, 80, 80, 0, 0.91f),
                new CandidateBox(220, 220, 34, 34, 0, 0.82f)
            ],
            new ExpectedBox(90, 90, 80, 80, 0),
            ExpectedBaselineDetections: 2),
        new(
            "nms_cross_class_overlap",
            320,
            320,
            "nms",
            [
                new CandidateBox(90, 90, 80, 80, 0, 0.95f),
                new CandidateBox(100, 100, 80, 80, 1, 0.93f),
                new CandidateBox(230, 60, 28, 28, 2, 0.80f)
            ],
            new ExpectedBox(90, 90, 80, 80, 0),
            ExpectedBaselineDetections: 3),
        new(
            "letterbox_wide_image",
            1280,
            720,
            "letterbox",
            [
                new CandidateBox(100, 80, 300, 200, 0, 0.97f)
            ],
            new ExpectedBox(100, 80, 300, 200, 0),
            ExpectedBaselineDetections: 1),
        new(
            "letterbox_tall_image",
            720,
            1280,
            "letterbox",
            [
                new CandidateBox(90, 240, 220, 360, 1, 0.96f)
            ],
            new ExpectedBox(90, 240, 220, 360, 1),
            ExpectedBaselineDetections: 1),
        new(
            "clamp_top_left_overflow",
            320,
            240,
            "clamp",
            [
                new CandidateBox(-18, -12, 72, 64, 0, 0.92f)
            ],
            new ExpectedBox(0, 0, 54, 52, 0),
            ExpectedBaselineDetections: 1),
        new(
            "clamp_bottom_right_overflow",
            320,
            240,
            "clamp",
            [
                new CandidateBox(276, 205, 72, 58, 2, 0.94f)
            ],
            new ExpectedBox(276, 205, 44, 35, 2),
            ExpectedBaselineDetections: 1)
    ];

    private static CaseResult RunCase(PostprocessCase testCase)
    {
        var stopwatch = Stopwatch.StartNew();
        var tensor = CreateTensor(testCase);
        var noNms = InvokePostprocess(tensor, testCase.Width, testCase.Height, enableNms: false, nmsIou: 0.45f);
        var baseline = InvokePostprocess(tensor, testCase.Width, testCase.Height, enableNms: true, nmsIou: 0.45f);
        var permissiveHard = InvokePostprocess(tensor, testCase.Width, testCase.Height, enableNms: true, nmsIou: 0.75f);
        var classAgnosticHard = ApplyHardNms(noNms, 0.45f, classAware: false);
        var soft = ApplyLinearSoftNms(noNms, 0.45f, ConfidenceThreshold);

        var comparisons = new List<ComparisonResult>
        {
            CompareDetectionCount(testCase, "hard_nms_045_baseline", "no_nms_candidate", "nms", baseline, noNms),
            CompareDetectionCount(testCase, "hard_nms_045_baseline", "hard_nms_075_candidate", "nms", baseline, permissiveHard),
            CompareDetectionCount(testCase, "hard_nms_045_baseline", "soft_nms_linear_offline", "nms", baseline, soft),
            CompareDetectionCount(testCase, "hard_nms_045_baseline", "class_agnostic_hard_nms_offline", "nms", baseline, classAgnosticHard)
        };

        if (testCase.Topic == "letterbox")
        {
            var productError = CoordinateError(baseline.FirstOrDefault(), testCase.Expected);
            var naive = NaiveNoLetterboxInverse(testCase);
            var naiveError = CoordinateError(naive, testCase.Expected);
            comparisons.Add(new ComparisonResult(
                "letterbox_inverse_baseline",
                "naive_no_letterbox_inverse_offline",
                "letterbox",
                baseline.Count,
                1,
                Math.Round(naiveError - productError, 6),
                Math.Round(productError, 6),
                Math.Round(naiveError, 6),
                productError <= 0.01 && naiveError > productError,
                "Positive delta means the candidate has larger coordinate error than the product letterbox inverse."));
        }

        if (testCase.Topic == "clamp")
        {
            var unclamped = UnclampedLetterboxInverse(testCase);
            var invalidBeforeClamp = CountOutOfBounds(unclamped, testCase.Width, testCase.Height);
            var invalidAfterClamp = CountOutOfBounds(baseline.FirstOrDefault(), testCase.Width, testCase.Height);
            comparisons.Add(new ComparisonResult(
                "product_clamp_baseline",
                "no_clamp_offline",
                "clamp",
                invalidAfterClamp,
                invalidBeforeClamp,
                invalidBeforeClamp - invalidAfterClamp,
                invalidAfterClamp,
                invalidBeforeClamp,
                invalidAfterClamp == 0 && invalidBeforeClamp > 0,
                "Positive delta means clamp removed invalid coordinates."));
        }

        stopwatch.Stop();
        var coordinateError = CoordinateError(baseline.FirstOrDefault(), testCase.Expected);
        var accepted = baseline.Count == testCase.ExpectedBaselineDetections &&
            coordinateError <= (testCase.Topic == "nms" ? 20.0 : 0.01) &&
            comparisons.All(item => item.Accepted);

        return new CaseResult(
            testCase.CaseId,
            testCase.Topic,
            accepted,
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            testCase.ExpectedBaselineDetections,
            baseline.Count,
            Math.Round(coordinateError, 6),
            comparisons);
    }

    private static DenseTensor<float> CreateTensor(PostprocessCase testCase)
    {
        var anchorCount = Math.Max(testCase.Candidates.Count, 8);
        var tensor = new DenseTensor<float>(new float[1 * (4 + ClassCount) * anchorCount], [1, 4 + ClassCount, anchorCount]);
        var scale = Math.Min((float)InputSize / testCase.Width, (float)InputSize / testCase.Height);
        var xPad = (InputSize - testCase.Width * scale) / 2f;
        var yPad = (InputSize - testCase.Height * scale) / 2f;

        for (var i = 0; i < testCase.Candidates.Count; i++)
        {
            var item = testCase.Candidates[i];
            tensor[0, 0, i] = (item.X + item.Width / 2f) * scale + xPad;
            tensor[0, 1, i] = (item.Y + item.Height / 2f) * scale + yPad;
            tensor[0, 2, i] = item.Width * scale;
            tensor[0, 3, i] = item.Height * scale;
            tensor[0, 4 + item.ClassId, i] = item.Confidence;
        }

        return tensor;
    }

    private static List<DetectionRecord> InvokePostprocess(
        DenseTensor<float> tensor,
        int width,
        int height,
        bool enableNms,
        float nmsIou)
    {
        var method = typeof(DeepLearningOperator).GetMethod("PostprocessYoloV8V11", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(DeepLearningOperator), "PostprocessYoloV8V11");
        var values = (IEnumerable)(method.Invoke(Operator, [tensor, ConfidenceThreshold, width, height, InputSize, enableNms, nmsIou])
            ?? throw new InvalidOperationException("PostprocessYoloV8V11 returned null."));
        return values.Cast<object>().Select(ReadDetection).OrderByDescending(item => item.Confidence).ToList();
    }

    private static DetectionRecord ReadDetection(object detection) =>
        new(
            ReadProperty<float>(detection, "X"),
            ReadProperty<float>(detection, "Y"),
            ReadProperty<float>(detection, "Width"),
            ReadProperty<float>(detection, "Height"),
            ReadProperty<float>(detection, "Confidence"),
            ReadProperty<int>(detection, "ClassId"));

    private static T ReadProperty<T>(object instance, string propertyName) =>
        (T)(instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Property not found: {propertyName}"));

    private static ComparisonResult CompareDetectionCount(
        PostprocessCase testCase,
        string baselineName,
        string candidateName,
        string topic,
        IReadOnlyList<DetectionRecord> baseline,
        IReadOnlyList<DetectionRecord> candidate)
    {
        var delta = candidate.Count - baseline.Count;
        var accepted = topic != "nms" || baseline.Count == testCase.ExpectedBaselineDetections;
        return new ComparisonResult(
            baselineName,
            candidateName,
            topic,
            baseline.Count,
            candidate.Count,
            delta,
            CoordinateError(baseline.FirstOrDefault(), testCase.Expected),
            CoordinateError(candidate.FirstOrDefault(), testCase.Expected),
            accepted,
            "Detection-count delta only; AP/precision/recall are out of scope for this postprocess runner.");
    }

    private static List<DetectionRecord> ApplyHardNms(IReadOnlyList<DetectionRecord> detections, float threshold, bool classAware)
    {
        var keep = new List<DetectionRecord>();
        foreach (var detection in detections.OrderByDescending(item => item.Confidence))
        {
            var suppressed = keep.Any(item =>
                (!classAware || item.ClassId == detection.ClassId) &&
                IoU(item, detection) > threshold);
            if (!suppressed)
            {
                keep.Add(detection);
            }
        }

        return keep;
    }

    private static List<DetectionRecord> ApplyLinearSoftNms(IReadOnlyList<DetectionRecord> detections, float threshold, float scoreFloor)
    {
        var work = detections.Select(item => item with { }).ToList();
        var keep = new List<DetectionRecord>();
        while (work.Count > 0)
        {
            work.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
            var best = work[0];
            work.RemoveAt(0);
            if (best.Confidence >= scoreFloor)
            {
                keep.Add(best);
            }

            for (var i = 0; i < work.Count; i++)
            {
                if (work[i].ClassId != best.ClassId)
                {
                    continue;
                }

                var iou = IoU(best, work[i]);
                if (iou > threshold)
                {
                    work[i] = work[i] with { Confidence = work[i].Confidence * (1f - iou) };
                }
            }

            work.RemoveAll(item => item.Confidence < scoreFloor);
        }

        return keep;
    }

    private static DetectionRecord NaiveNoLetterboxInverse(PostprocessCase testCase)
    {
        var item = testCase.Candidates[0];
        var scaleX = (float)InputSize / testCase.Width;
        var scaleY = (float)InputSize / testCase.Height;
        var modelCx = (item.X + item.Width / 2f) * Math.Min(scaleX, scaleY) + (InputSize - testCase.Width * Math.Min(scaleX, scaleY)) / 2f;
        var modelCy = (item.Y + item.Height / 2f) * Math.Min(scaleX, scaleY) + (InputSize - testCase.Height * Math.Min(scaleX, scaleY)) / 2f;
        var modelW = item.Width * Math.Min(scaleX, scaleY);
        var modelH = item.Height * Math.Min(scaleX, scaleY);
        return new DetectionRecord(
            (modelCx - modelW / 2f) / scaleX,
            (modelCy - modelH / 2f) / scaleY,
            modelW / scaleX,
            modelH / scaleY,
            item.Confidence,
            item.ClassId);
    }

    private static DetectionRecord UnclampedLetterboxInverse(PostprocessCase testCase)
    {
        var item = testCase.Candidates[0];
        return new DetectionRecord(item.X, item.Y, item.Width, item.Height, item.Confidence, item.ClassId);
    }

    private static int CountOutOfBounds(DetectionRecord? detection, int width, int height)
    {
        if (detection is null)
        {
            return 1;
        }

        var count = 0;
        if (detection.X < 0 || detection.Y < 0)
        {
            count++;
        }

        if (detection.X + detection.Width > width || detection.Y + detection.Height > height)
        {
            count++;
        }

        if (detection.Width < 0 || detection.Height < 0)
        {
            count++;
        }

        return count;
    }

    private static double CoordinateError(DetectionRecord? detection, ExpectedBox expected)
    {
        if (detection is null)
        {
            return double.PositiveInfinity;
        }

        return new[]
        {
            Math.Abs(detection.X - expected.X),
            Math.Abs(detection.Y - expected.Y),
            Math.Abs(detection.Width - expected.Width),
            Math.Abs(detection.Height - expected.Height)
        }.Max();
    }

    private static float IoU(DetectionRecord a, DetectionRecord b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var union = a.Width * a.Height + b.Width * b.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

internal sealed record RunnerOptions(string OutputPath, string ReportPath, bool ShowHelp, string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var options = new RunnerOptions(
            "quality/evals/reports/QualityFlywheel_deep_learning_postprocess_ab_v1.json",
            "quality/evals/reports/QualityFlywheel_deep_learning_postprocess_ab_v1.md",
            false,
            null);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                return options with { ShowHelp = true };
            }

            if (i + 1 >= args.Length)
            {
                return options with { ParseError = $"Missing value for {arg}" };
            }

            var value = args[++i];
            options = arg switch
            {
                "--output" => options with { OutputPath = value },
                "--report" => options with { ReportPath = value },
                _ => options with { ParseError = $"Unknown argument: {arg}" }
            };
        }

        return options;
    }

    public static void PrintHelp() =>
        Console.WriteLine("Usage: dotnet run --project quality/tools/DeepLearningPostprocessABRunner/DeepLearningPostprocessABRunner.csproj -- [--output path] [--report path]");
}

internal static class MarkdownReport
{
    public static string Create(PostprocessAbResult result)
    {
        var lines = new List<string>
        {
            "# DeepLearning Postprocess A/B Report",
            "",
            $"GeneratedAtUtc: `{result.GeneratedAtUtc:O}`",
            $"Accepted: `{result.Accepted}`",
            "",
            "## Claim Boundary",
            "",
            $"- {result.ClaimBoundary.PostprocessOnlyRule}",
            $"- {result.ClaimBoundary.NoModelAccuracyRule}",
            $"- {result.ClaimBoundary.OfflineVariantRule}",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Comparisons | {result.Summary.ComparisonCount} |",
            $"| Accepted cases | {result.Summary.AcceptedCaseCount} |",
            $"| Failed cases | {result.Summary.FailedCaseCount} |",
            $"| NMS comparisons | {result.Summary.NmsComparisonCount} |",
            $"| Letterbox comparisons | {result.Summary.LetterboxComparisonCount} |",
            $"| Clamp comparisons | {result.Summary.ClampComparisonCount} |",
            "",
            "## Cases",
            "",
            "| Case | Topic | Accepted | Expected baseline detections | Actual baseline detections | Coordinate error px | Runtime ms |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Cases.Select(item =>
            $"| {item.CaseId} | {item.Topic} | {item.Accepted} | {item.ExpectedBaselineDetections} | {item.ActualBaselineDetections} | {item.BaselineCoordinateErrorPx:0.######} | {item.RuntimeMs:0.###} |"));

        lines.AddRange(
        [
            "",
            "## A/B Comparisons",
            "",
            "| Case | Topic | Baseline | Candidate | Baseline value | Candidate value | Delta | Baseline coord error | Candidate coord error | Accepted | Note |",
            "| --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |"
        ]);

        foreach (var item in result.Cases)
        {
            lines.AddRange(item.Comparisons.Select(comparison =>
                $"| {item.CaseId} | {comparison.Topic} | {comparison.Baseline} | {comparison.Candidate} | {comparison.BaselineValue:0.######} | {comparison.CandidateValue:0.######} | {comparison.Delta:0.######} | {comparison.BaselineCoordinateErrorPx:0.######} | {comparison.CandidateCoordinateErrorPx:0.######} | {comparison.Accepted} | {comparison.Note} |"));
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record PostprocessCase(
    string CaseId,
    int Width,
    int Height,
    string Topic,
    IReadOnlyList<CandidateBox> Candidates,
    ExpectedBox Expected,
    int ExpectedBaselineDetections);

internal sealed record CandidateBox(float X, float Y, float Width, float Height, int ClassId, float Confidence);
internal sealed record ExpectedBox(float X, float Y, float Width, float Height, int ClassId);
internal sealed record DetectionRecord(float X, float Y, float Width, float Height, float Confidence, int ClassId);
internal sealed record ComparisonResult(
    string Baseline,
    string Candidate,
    string Topic,
    double BaselineValue,
    double CandidateValue,
    double Delta,
    double BaselineCoordinateErrorPx,
    double CandidateCoordinateErrorPx,
    bool Accepted,
    string Note);
internal sealed record CaseResult(
    string CaseId,
    string Topic,
    bool Accepted,
    double RuntimeMs,
    int ExpectedBaselineDetections,
    int ActualBaselineDetections,
    double BaselineCoordinateErrorPx,
    IReadOnlyList<ComparisonResult> Comparisons);
internal sealed record Summary(
    int CaseCount,
    int ComparisonCount,
    int AcceptedCaseCount,
    int FailedCaseCount,
    int NmsComparisonCount,
    int LetterboxComparisonCount,
    int ClampComparisonCount);
internal sealed record ClaimBoundary(string PostprocessOnlyRule, string NoModelAccuracyRule, string OfflineVariantRule);
internal sealed record PostprocessAbResult(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Accepted,
    Summary Summary,
    ClaimBoundary ClaimBoundary,
    IReadOnlyList<CaseResult> Cases);

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };
}
