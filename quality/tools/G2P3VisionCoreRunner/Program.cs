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

var result = await G2P3VisionCoreRunner.RunAsync();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(result, JsonSettings.Indented));

if (!string.IsNullOrWhiteSpace(options.ReportPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ReportPath))!);
    await File.WriteAllTextAsync(options.ReportPath, MarkdownReport.Create(result));
}

Console.WriteLine(
    $"G2 P3 vision core baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class G2P3VisionCoreRunner
{
    private const string EvidenceKind = "golden";

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<RunnerCase>();
        AddImageDiffCases(cases);
        AddImageSubtractCases(cases);
        AddAdaptiveThresholdCases(cases);
        AddEdgeDetectionCases(cases);
        AddContourDetectionCases(cases);
        AddBlobAnalysisCases(cases);
        AddBlobLabelingCases(cases);
        AddLineMeasurementCases(cases);
        AddCircleMeasurementCases(cases);
        AddWidthMeasurementCases(cases);
        AddGeometricFittingCases(cases);
        AddPerspectiveTransformCases(cases);
        AddAffineTransformCases(cases);
        AddDistanceTransformCases(cases);

        var results = new List<CaseResult>(cases.Count);
        foreach (var runnerCase in cases)
        {
            results.Add(await RunCaseAsync(runnerCase));
        }

        var operators = results
            .GroupBy(item => item.Operator)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new OperatorEvidence(
                group.Key,
                EvidenceKind,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3),
                Convert.ToInt64(Math.Round(group.Average(item => item.MemoryAllocationBytes)))))
            .ToArray();

        var scenarios = results
            .GroupBy(item => item.Scenario)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ScenarioSummary(
                group.Key,
                group.Count(),
                group.Count(item => item.Passed),
                group.Count(item => !item.Passed),
                Math.Round(group.Average(item => item.RuntimeMs), 3)))
            .ToArray();

        return new BaselineResult(
            EvidenceKind,
            new BaselineSummary(
                DateTimeOffset.UtcNow,
                results.Count,
                results.Count(item => item.Passed),
                results.Count(item => !item.Passed),
                Math.Round(results.Sum(item => item.RuntimeMs), 3)),
            operators,
            scenarios,
            results);
    }

    private static async Task<CaseResult> RunCaseAsync(RunnerCase runnerCase)
    {
        var beforeBytes = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var observed = await runnerCase.Body();
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                runnerCase.Id,
                runnerCase.Operator,
                runnerCase.Scenario,
                true,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                null,
                observed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var afterBytes = GC.GetTotalAllocatedBytes(precise: true);
            return new CaseResult(
                runnerCase.Id,
                runnerCase.Operator,
                runnerCase.Scenario,
                false,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                Math.Max(0, afterBytes - beforeBytes),
                ex.GetBaseException().Message,
                new Dictionary<string, object?>());
        }
    }

    private static void AddImageDiffCases(List<RunnerCase> cases)
    {
        var sut = new ImageDiffOperator(NullLogger<ImageDiffOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "ImageDiff", $"pixel_diff_{index:00}", "Pixel difference oracle", async () =>
            {
                const int size = 32;
                using var baseImage = CreateGrayImage(size, size, 0);
                using var compareImage = CreateGrayImage(size, size, 0);
                var rect = new Rect(2 + (index % 4), 3 + (index % 5), 4 + (index % 6), 5 + (index % 4));
                Cv2.Rectangle(compareImage.GetMat(), rect, Scalar.White, -1);

                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.ImageDiff),
                    Inputs(("BaseImage", baseImage), ("CompareImage", compareImage)));

                RequireSuccess(result);
                var expectedRate = rect.Width * rect.Height / (double)(size * size);
                RequireNear(RequireDouble(result, "DiffRate"), expectedRate, 1e-12, "DiffRate");
                return Observed(("ExpectedDiffRate", expectedRate), ("DiffRate", RequireDouble(result, "DiffRate")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "ImageDiff", $"mismatched_size_{i:00}", "Failure contract", async () =>
            {
                using var baseImage = CreateGrayImage(16, 16, 0);
                using var compareImage = CreateGrayImage(18 + i, 16, 255);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.ImageDiff),
                    Inputs(("BaseImage", baseImage), ("CompareImage", compareImage)));
                RequireFailure(result);
                return Observed(("FailureReason", "MismatchedSize"));
            });
        }
    }

    private static void AddImageSubtractCases(List<RunnerCase> cases)
    {
        var sut = new ImageSubtractOperator(NullLogger<ImageSubtractOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "ImageSubtract", $"constant_abs_{index:00}", "Subtraction statistics oracle", async () =>
            {
                var value1 = 120 + (index % 5);
                var value2 = 30 + index;
                using var image1 = CreateGrayImage(40, 28, (byte)value1);
                using var image2 = CreateGrayImage(index % 3 == 0 ? 20 : 40, index % 3 == 0 ? 14 : 28, (byte)value2);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.ImageSubtract, ("AbsoluteDiff", true)),
                    Inputs(("Image1", image1), ("Image2", image2)));

                RequireSuccess(result);
                var expected = Math.Abs(value1 - value2);
                RequireNear(RequireDouble(result, "MinDifference"), expected, 0.001, "MinDifference");
                RequireNear(RequireDouble(result, "MaxDifference"), expected, 0.001, "MaxDifference");
                RequireNear(RequireDouble(result, "MeanDifference"), expected, 0.001, "MeanDifference");
                return Observed(("ExpectedDifference", expected), ("MeanDifference", RequireDouble(result, "MeanDifference")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "ImageSubtract", $"missing_input_{i:00}", "Failure contract", async () =>
            {
                using var image = CreateGrayImage(12, 12, 10);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.ImageSubtract),
                    Inputs(("Image1", image)));
                RequireFailure(result);
                return Observed(("FailureReason", "MissingImage2"));
            });
        }
    }

    private static void AddAdaptiveThresholdCases(List<RunnerCase> cases)
    {
        var sut = new AdaptiveThresholdOperator(NullLogger<AdaptiveThresholdOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "AdaptiveThreshold", $"opencv_oracle_{index:00}", "OpenCV adaptive threshold oracle", async () =>
            {
                using var image = CreateIlluminationImage(48, 40, index);
                using var source = image.GetMat().Clone();
                var method = index % 2 == 0 ? "Gaussian" : "Mean";
                var thresholdType = index % 3 == 0 ? "BinaryInv" : "Binary";
                var blockSize = 5 + (2 * (index % 5));
                var c = index % 4;

                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.AdaptiveThreshold,
                        ("AdaptiveMethod", method),
                        ("ThresholdType", thresholdType),
                        ("BlockSize", blockSize),
                        ("C", c),
                        ("MaxValue", 255.0)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                using var expected = new Mat();
                Cv2.AdaptiveThreshold(
                    source,
                    expected,
                    255.0,
                    method == "Mean" ? AdaptiveThresholdTypes.MeanC : AdaptiveThresholdTypes.GaussianC,
                    thresholdType == "BinaryInv" ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary,
                    blockSize,
                    c);

                RequireImagesEqual(GetImage(result), expected, "AdaptiveThreshold output");
                return Observed(("Method", method), ("ThresholdType", thresholdType), ("BlockSize", blockSize));
            });
        }

        AddValidationCase(cases, "AdaptiveThreshold", "invalid_method", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.AdaptiveThreshold, ("AdaptiveMethod", "Median"))));
        AddValidationCase(cases, "AdaptiveThreshold", "invalid_threshold_type", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.AdaptiveThreshold, ("ThresholdType", "Trunc"))));
        AddValidationCase(cases, "AdaptiveThreshold", "invalid_block_size", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.AdaptiveThreshold, ("BlockSize", 1))));
        Add(cases, "AdaptiveThreshold", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.AdaptiveThreshold), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddEdgeDetectionCases(List<RunnerCase> cases)
    {
        var sut = new CannyEdgeOperator(NullLogger<CannyEdgeOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "EdgeDetection", $"canny_oracle_{index:00}", "OpenCV Canny oracle", async () =>
            {
                using var image = CreateEdgeScene(72, 60, index);
                using var source = image.GetMat().Clone();
                var threshold1 = 35.0 + index;
                var threshold2 = 120.0 + index;
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.EdgeDetection,
                        ("Threshold1", threshold1),
                        ("Threshold2", threshold2),
                        ("EnableGaussianBlur", false),
                        ("AutoThreshold", false),
                        ("ApertureSize", 3),
                        ("L2Gradient", false)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                using var expected = new Mat();
                Cv2.Canny(source, expected, threshold1, threshold2, 3, false);
                RequireImagesEqual(GetImage(result), expected, "Canny output");
                RequireNear(RequireDouble(result, "Threshold1Used"), threshold1, 1e-9, "Threshold1Used");
                RequireNear(RequireDouble(result, "Threshold2Used"), threshold2, 1e-9, "Threshold2Used");
                return Observed(("EdgePixels", CountNonZero(GetImage(result))));
            });
        }

        AddValidationCase(cases, "EdgeDetection", "invalid_threshold1", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.EdgeDetection, ("Threshold1", -1.0))));
        AddValidationCase(cases, "EdgeDetection", "invalid_threshold2", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.EdgeDetection, ("Threshold2", 300.0))));
        AddValidationCase(cases, "EdgeDetection", "invalid_auto_sigma", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.EdgeDetection, ("AutoThresholdSigma", 0.0))));
        Add(cases, "EdgeDetection", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.EdgeDetection), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddContourDetectionCases(List<RunnerCase> cases)
    {
        var sut = new FindContoursOperator(NullLogger<FindContoursOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "ContourDetection", $"rectangles_{index:00}", "Contour count oracle", async () =>
            {
                var expectedCount = 1 + (index % 3);
                using var image = CreateSeparatedRectangles(96, 72, expectedCount, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.ContourDetection,
                        ("Mode", "External"),
                        ("Method", "Simple"),
                        ("MinArea", 20),
                        ("MaxArea", 10000),
                        ("Threshold", 127.0),
                        ("ThresholdType", "Binary")),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireIntEquals(result, "ContourCount", expectedCount);
                return Observed(("ExpectedContourCount", expectedCount));
            });
        }

        AddValidationCase(cases, "ContourDetection", "invalid_area", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.ContourDetection, ("MinArea", 10), ("MaxArea", 10))));
        AddValidationCase(cases, "ContourDetection", "invalid_mode", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.ContourDetection, ("Mode", "BadMode"))));
        AddValidationCase(cases, "ContourDetection", "invalid_threshold_type", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.ContourDetection, ("ThresholdType", "Otsu"))));
        Add(cases, "ContourDetection", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.ContourDetection), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddBlobAnalysisCases(List<RunnerCase> cases)
    {
        var sut = new BlobDetectionOperator(NullLogger<BlobDetectionOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "BlobAnalysis", $"components_{index:00}", "Connected component oracle", async () =>
            {
                var expectedCount = 1 + (index % 3);
                using var image = CreateSeparatedRectangles(120, 80, expectedCount, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.BlobAnalysis,
                        ("MinArea", 20),
                        ("MaxArea", 10000),
                        ("Color", "White"),
                        ("OutputDetailedFeatures", true)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireIntEquals(result, "BlobCount", expectedCount);
                RequireKey(result, "BlobFeatures");
                return Observed(("ExpectedBlobCount", expectedCount));
            });
        }

        AddValidationCase(cases, "BlobAnalysis", "invalid_area", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.BlobAnalysis, ("MinArea", 100), ("MaxArea", 10))));
        AddValidationCase(cases, "BlobAnalysis", "invalid_color", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.BlobAnalysis, ("Color", "Blue"))));
        Add(cases, "BlobAnalysis", "invalid_feature_filter", "Failure contract", async () =>
        {
            using var image = CreateSeparatedRectangles(80, 60, 1, 0);
            var result = await sut.ExecuteAsync(
                CreateOperator(OperatorType.BlobAnalysis, ("FeatureFilter", "Area >")),
                Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "InvalidFeatureFilter"));
        });
        Add(cases, "BlobAnalysis", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.BlobAnalysis), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddBlobLabelingCases(List<RunnerCase> cases)
    {
        var sut = new BlobLabelingOperator(NullLogger<BlobLabelingOperator>.Instance);
        const string thresholds = """
        [{"Name":"Small","Min":1,"Max":399},{"Name":"Medium","Min":400,"Max":1599},{"Name":"Large","Min":1600,"Max":100000}]
        """;

        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "BlobLabeling", $"provided_blobs_{index:00}", "Provided blob label oracle", async () =>
            {
                using var image = CreateColorImage(160, 120, Scalar.Black);
                var blobs = new List<Dictionary<string, object>>
                {
                    new() { ["X"] = 8 + index, ["Y"] = 10, ["Width"] = 12, ["Height"] = 12, ["Area"] = 144 },
                    new() { ["X"] = 60, ["Y"] = 35, ["Width"] = 30, ["Height"] = 28, ["Area"] = 840 },
                    new() { ["X"] = 105, ["Y"] = 50, ["Width"] = 42, ["Height"] = 42, ["Area"] = 1764 }
                };

                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.BlobLabeling, ("LabelBy", "Area"), ("Thresholds", thresholds), ("DrawLabels", false)),
                    Inputs(("Image", image), ("Blobs", blobs)));

                RequireSuccess(result);
                RequireIntEquals(result, "Count", 3);
                var labels = RequireList(result, "Labels");
                var labelNames = labels.Select(RequireLabelName).ToArray();
                Require(labelNames.Contains("Small"), "Expected Small label.");
                Require(labelNames.Contains("Medium"), "Expected Medium label.");
                Require(labelNames.Contains("Large"), "Expected Large label.");
                return Observed(("Labels", string.Join(",", labelNames)));
            });
        }

        AddValidationCase(cases, "BlobLabeling", "invalid_label_by", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.BlobLabeling, ("LabelBy", "Color"))));
        AddValidationCase(cases, "BlobLabeling", "invalid_thresholds", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.BlobLabeling, ("Thresholds", "[{\"Name\":\"A\",\"Min\":2,\"Max\":1}]"))));
        Add(cases, "BlobLabeling", "invalid_blob_input", "Failure contract", async () =>
        {
            using var image = CreateColorImage(80, 60, Scalar.Black);
            var result = await sut.ExecuteAsync(
                CreateOperator(OperatorType.BlobLabeling),
                Inputs(("Image", image), ("Blobs", new object())));
            RequireFailure(result);
            return Observed(("FailureReason", "InvalidBlobInput"));
        });
        Add(cases, "BlobLabeling", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.BlobLabeling), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddLineMeasurementCases(List<RunnerCase> cases)
    {
        var sut = new LineMeasurementOperator(NullLogger<LineMeasurementOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "LineMeasurement", $"probabilistic_hough_{index:00}", "Line geometry oracle", async () =>
            {
                var horizontal = index % 2 == 0;
                using var image = CreateLineScene(180, 140, horizontal, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.LineMeasurement,
                        ("Method", "ProbabilisticHough"),
                        ("Threshold", 25),
                        ("MinLength", 45.0),
                        ("MaxGap", 8.0)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireAtLeast(RequireInt(result, "LineCount"), 1, "LineCount");
                RequireAtLeast(RequireDouble(result, "Length"), 80.0, "Line length");
                RequireLineAngle(RequireDouble(result, "Angle"), horizontal);
                return Observed(("Angle", RequireDouble(result, "Angle")), ("Length", RequireDouble(result, "Length")));
            });
        }

        AddValidationCase(cases, "LineMeasurement", "invalid_method", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.LineMeasurement, ("Method", "Bad"))));
        AddValidationCase(cases, "LineMeasurement", "invalid_threshold", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.LineMeasurement, ("Threshold", 0))));
        Add(cases, "LineMeasurement", "blank_image_no_feature", "Failure contract", async () =>
        {
            using var image = CreateGrayImage(80, 80, 0);
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.LineMeasurement), Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "NoFeature"));
        });
        Add(cases, "LineMeasurement", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.LineMeasurement), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddCircleMeasurementCases(List<RunnerCase> cases)
    {
        var sut = new CircleMeasurementOperator(NullLogger<CircleMeasurementOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "CircleMeasurement", $"fit_ellipse_{index:00}", "Circle fit oracle", async () =>
            {
                var radius = 18 + (index % 8);
                var center = new Point(50 + (index % 3), 48 + (index % 4));
                using var image = CreateCircleScene(120, 100, center, radius, filled: true);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.CircleMeasurement,
                        ("Method", "FitEllipse"),
                        ("MinRadius", 8),
                        ("MaxRadius", 50)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireAtLeast(RequireInt(result, "CircleCount"), 1, "CircleCount");
                RequireNear(RequireDouble(result, "Radius"), radius, 2.5, "Radius");
                RequirePositionNear(RequirePosition(result, "Center"), center.X, center.Y, 2.5, "Center");
                return Observed(("ExpectedRadius", radius), ("Radius", RequireDouble(result, "Radius")));
            });
        }

        AddValidationCase(cases, "CircleMeasurement", "invalid_method", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.CircleMeasurement, ("Method", "Bad"))));
        AddValidationCase(cases, "CircleMeasurement", "invalid_radius", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.CircleMeasurement, ("MinRadius", 20), ("MaxRadius", 10))));
        Add(cases, "CircleMeasurement", "blank_image_no_feature", "Failure contract", async () =>
        {
            using var image = CreateGrayImage(80, 80, 0);
            var result = await sut.ExecuteAsync(
                CreateOperator(OperatorType.CircleMeasurement, ("Method", "FitEllipse")),
                Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "NoFeature"));
        });
        Add(cases, "CircleMeasurement", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.CircleMeasurement), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddWidthMeasurementCases(List<RunnerCase> cases)
    {
        var sut = new WidthMeasurementOperator(NullLogger<WidthMeasurementOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "WidthMeasurement", $"manual_lines_{index:00}", "Manual line width oracle", async () =>
            {
                var stripeWidth = 28 + (index % 6);
                var x1 = 50;
                var x2 = x1 + stripeWidth;
                using var image = CreateStripeScene(140, 120, x1, x2);
                var line1 = new LineData(x1 - 12, 18, x1 - 12, 102);
                var line2 = new LineData(x2 + 12, 18, x2 + 12, 102);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.WidthMeasurement,
                        ("MeasureMode", "ManualLines"),
                        ("SampleCount", 12),
                        ("MultiScanCount", 20),
                        ("RobustMode", true),
                        ("MinValidSamples", 6)),
                    Inputs(("Image", image), ("Line1", line1), ("Line2", line2)));

                RequireSuccess(result);
                RequireNear(RequireDouble(result, "MeanWidth"), stripeWidth, 2.5, "MeanWidth");
                RequireAtLeast(RequireDouble(result, "ValidSampleRate"), 0.5, "ValidSampleRate");
                return Observed(("ExpectedWidth", stripeWidth), ("MeanWidth", RequireDouble(result, "MeanWidth")));
            });
        }

        AddValidationCase(cases, "WidthMeasurement", "invalid_mode", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.WidthMeasurement, ("MeasureMode", "Magic"))));
        AddValidationCase(cases, "WidthMeasurement", "invalid_multiscan", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.WidthMeasurement, ("SampleCount", 20), ("MultiScanCount", 10))));
        Add(cases, "WidthMeasurement", "manual_missing_lines", "Failure contract", async () =>
        {
            using var image = CreateStripeScene(80, 80, 20, 45);
            var result = await sut.ExecuteAsync(
                CreateOperator(OperatorType.WidthMeasurement, ("MeasureMode", "ManualLines")),
                Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "MissingManualLines"));
        });
        Add(cases, "WidthMeasurement", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.WidthMeasurement), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddGeometricFittingCases(List<RunnerCase> cases)
    {
        var sut = new GeometricFittingOperator(NullLogger<GeometricFittingOperator>.Instance);
        for (var i = 0; i < 6; i++)
        {
            var index = i;
            Add(cases, "GeometricFitting", $"line_fit_{index:00}", "Line fitting oracle", async () =>
            {
                using var image = CreateLineScene(160, 120, horizontal: index % 2 == 0, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.GeometricFitting, ("FitType", "Line"), ("MinArea", 10), ("MinPoints", 5)),
                    Inputs(("Image", image)));
                RequireSuccess(result);
                RequireString(RequireFitResult(result), "FitType", "Line");
                RequireAtLeast(RequireInt(result, "PointCount"), 5, "PointCount");
                return Observed(("FitType", "Line"), ("PointCount", RequireInt(result, "PointCount")));
            });
        }

        for (var i = 0; i < 6; i++)
        {
            var index = i;
            Add(cases, "GeometricFitting", $"circle_fit_{index:00}", "Circle fitting oracle", async () =>
            {
                using var image = CreateCircleScene(130, 110, new Point(64, 54), 22 + (index % 4), filled: false);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.GeometricFitting, ("FitType", "Circle"), ("MinArea", 10), ("MinPoints", 5)),
                    Inputs(("Image", image)));
                RequireSuccess(result);
                RequireString(RequireFitResult(result), "FitType", "Circle");
                RequireAtLeast(RequireInt(result, "PointCount"), 12, "PointCount");
                return Observed(("FitType", "Circle"), ("ResidualMean", TryDouble(result, "ResidualMean")));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            var index = i;
            Add(cases, "GeometricFitting", $"ellipse_fit_{index:00}", "Ellipse fitting oracle", async () =>
            {
                using var image = CreateEllipseScene(140, 120, index);
                var result = await sut.ExecuteAsync(
                    CreateOperator(OperatorType.GeometricFitting, ("FitType", "Ellipse"), ("MinArea", 10), ("MinPoints", 5)),
                    Inputs(("Image", image)));
                RequireSuccess(result);
                RequireString(RequireFitResult(result), "FitType", "Ellipse");
                RequireAtLeast(RequireInt(result, "PointCount"), 12, "PointCount");
                return Observed(("FitType", "Ellipse"), ("ResidualMean", TryDouble(result, "ResidualMean")));
            });
        }

        AddValidationCase(cases, "GeometricFitting", "invalid_fit_type", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.GeometricFitting, ("FitType", "Spline"))));
        AddValidationCase(cases, "GeometricFitting", "invalid_min_points", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.GeometricFitting, ("MinPoints", 2))));
        AddValidationCase(cases, "GeometricFitting", "invalid_robust_method", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.GeometricFitting, ("RobustMethod", "Magic"))));
        Add(cases, "GeometricFitting", "blank_image_no_feature", "Failure contract", async () =>
        {
            using var image = CreateGrayImage(80, 80, 0);
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.GeometricFitting), Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "NoFeature"));
        });
    }

    private static void AddPerspectiveTransformCases(List<RunnerCase> cases)
    {
        var sut = new PerspectiveTransformOperator(NullLogger<PerspectiveTransformOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "PerspectiveTransform", $"pointset_{index:00}", "Point set transform oracle", async () =>
            {
                using var image = CreateCheckerScene(120, 100);
                var outputWidth = 80 + index;
                var outputHeight = 70 + (index % 5);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.PerspectiveTransform,
                        ("SrcPointsJson", "[[10,8],[108,12],[102,88],[12,92]]"),
                        ("DstPointsJson", $"[[0,0],[{outputWidth - 1},0],[{outputWidth - 1},{outputHeight - 1}],[0,{outputHeight - 1}]]"),
                        ("OutputWidth", outputWidth),
                        ("OutputHeight", outputHeight)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                RequireValue(result, "PointSetMode", "PointSetJsonOrInput");
                RequireIntEquals(result, "Width", outputWidth);
                RequireIntEquals(result, "Height", outputHeight);
                return Observed(("OutputWidth", outputWidth), ("OutputHeight", outputHeight));
            });
        }

        AddValidationCase(cases, "PerspectiveTransform", "invalid_output_width", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.PerspectiveTransform, ("OutputWidth", 0))));
        AddValidationCase(cases, "PerspectiveTransform", "missing_dst_json", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.PerspectiveTransform, ("SrcPointsJson", "[[0,0],[1,0],[1,1],[0,1]]"))));
        Add(cases, "PerspectiveTransform", "degenerate_src_points", "Failure contract", async () =>
        {
            using var image = CreateCheckerScene(80, 80);
            var result = await sut.ExecuteAsync(
                CreateOperator(
                    OperatorType.PerspectiveTransform,
                    ("SrcPointsJson", "[[0,0],[10,0],[20,0],[30,0]]"),
                    ("DstPointsJson", "[[0,0],[50,0],[50,50],[0,50]]")),
                Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "DegeneratePoints"));
        });
        Add(cases, "PerspectiveTransform", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.PerspectiveTransform), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddAffineTransformCases(List<RunnerCase> cases)
    {
        var sut = new AffineTransformOperator(NullLogger<AffineTransformOperator>.Instance);
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            Add(cases, "AffineTransform", $"translate_{index:00}", "Affine matrix oracle", async () =>
            {
                using var image = CreateCheckerScene(90, 70);
                var tx = 2.0 + index;
                var ty = -3.0 + (index % 4);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.AffineTransform,
                        ("Mode", "RotateScaleTranslate"),
                        ("Angle", 0.0),
                        ("Scale", 1.0),
                        ("TranslateX", tx),
                        ("TranslateY", ty),
                        ("OutputWidth", 100),
                        ("OutputHeight", 80)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                var matrix = RequireMatrix(result, "TransformMatrix");
                RequireNear(matrix[0][2], tx, 1e-9, "TranslateX");
                RequireNear(matrix[1][2], ty, 1e-9, "TranslateY");
                RequireIntEquals(result, "Width", 100);
                RequireIntEquals(result, "Height", 80);
                return Observed(("TranslateX", tx), ("TranslateY", ty));
            });
        }

        for (var i = 0; i < 4; i++)
        {
            Add(cases, "AffineTransform", $"three_point_identity_{i:00}", "Three-point transform oracle", async () =>
            {
                using var image = CreateCheckerScene(80, 60);
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.AffineTransform,
                        ("Mode", "ThreePoint"),
                        ("SrcPoints", "[[0,0],[79,0],[0,59]]"),
                        ("DstPoints", "[[0,0],[79,0],[0,59]]"),
                        ("OutputWidth", 80),
                        ("OutputHeight", 60)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                var matrix = RequireMatrix(result, "TransformMatrix");
                RequireNear(matrix[0][0], 1.0, 1e-9, "M00");
                RequireNear(matrix[1][1], 1.0, 1e-9, "M11");
                return Observed(("Mode", "ThreePoint"));
            });
        }

        AddValidationCase(cases, "AffineTransform", "invalid_mode", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.AffineTransform, ("Mode", "Projective"))));
        AddValidationCase(cases, "AffineTransform", "invalid_scale", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.AffineTransform, ("Scale", 0.0))));
        Add(cases, "AffineTransform", "degenerate_three_point", "Failure contract", async () =>
        {
            using var image = CreateCheckerScene(80, 60);
            var result = await sut.ExecuteAsync(
                CreateOperator(
                    OperatorType.AffineTransform,
                    ("Mode", "ThreePoint"),
                    ("SrcPoints", "[[0,0],[10,0],[20,0]]"),
                    ("DstPoints", "[[0,0],[10,0],[0,10]]")),
                Inputs(("Image", image)));
            RequireFailure(result);
            return Observed(("FailureReason", "DegeneratePoints"));
        });
        Add(cases, "AffineTransform", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.AffineTransform), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddDistanceTransformCases(List<RunnerCase> cases)
    {
        var sut = new DistanceTransformOperator(NullLogger<DistanceTransformOperator>.Instance);
        for (var i = 0; i < 16; i++)
        {
            var index = i;
            Add(cases, "DistanceTransform", $"opencv_distance_{index:00}", "OpenCV distance transform oracle", async () =>
            {
                using var image = CreateDistanceMask(90, 76, index);
                using var source = image.GetMat().Clone();
                var distanceType = index % 3 == 0 ? "Manhattan" : index % 3 == 1 ? "Chessboard" : "Euclidean";
                var result = await sut.ExecuteAsync(
                    CreateOperator(
                        OperatorType.DistanceTransform,
                        ("DistanceType", distanceType),
                        ("MaskSize", 5),
                        ("Threshold", 127.0),
                        ("Invert", false),
                        ("Signed", false)),
                    Inputs(("Image", image)));

                RequireSuccess(result);
                using var binary = new Mat();
                Cv2.Threshold(source, binary, 127.0, 255, ThresholdTypes.Binary);
                using var expected = new Mat();
                Cv2.DistanceTransform(binary, expected, DistanceTypeFor(distanceType), DistanceTransformMasks.Mask5);
                Cv2.MinMaxLoc(expected, out _, out var expectedMax, out _, out _);
                RequireNear(RequireDouble(result, "MaxDistance"), expectedMax, 1e-4, "MaxDistance");
                return Observed(("DistanceType", distanceType), ("MaxDistance", RequireDouble(result, "MaxDistance")));
            });
        }

        AddValidationCase(cases, "DistanceTransform", "invalid_mask", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.DistanceTransform, ("MaskSize", 7))));
        AddValidationCase(cases, "DistanceTransform", "invalid_threshold", "Validation contract", () =>
            sut.ValidateParameters(CreateOperator(OperatorType.DistanceTransform, ("Threshold", 300.0))));
        Add(cases, "DistanceTransform", "max_limit_contract", "Distance limit contract", async () =>
        {
            using var image = CreateDistanceMask(80, 70, 0);
            var result = await sut.ExecuteAsync(
                CreateOperator(OperatorType.DistanceTransform, ("MaxDistanceLimit", 5.0)),
                Inputs(("Image", image)));
            RequireSuccess(result);
            RequireAtMost(RequireDouble(result, "MaxDistance"), 5.0, "MaxDistance");
            return Observed(("MaxDistanceLimit", 5.0), ("MaxDistance", RequireDouble(result, "MaxDistance")));
        });
        Add(cases, "DistanceTransform", "missing_image", "Failure contract", async () =>
        {
            var result = await sut.ExecuteAsync(CreateOperator(OperatorType.DistanceTransform), null);
            RequireFailure(result);
            return Observed(("FailureReason", "MissingImage"));
        });
    }

    private static void AddValidationCase(
        List<RunnerCase> cases,
        string operatorName,
        string id,
        string scenario,
        Func<ValidationResult> body)
    {
        Add(cases, operatorName, id, scenario, () =>
        {
            var validation = body();
            Require(!validation.IsValid, "Expected invalid validation result.");
            return Task.FromResult(Observed(("Validation", "Invalid")));
        });
    }

    private static void Add(
        List<RunnerCase> cases,
        string operatorName,
        string id,
        string scenario,
        Func<Task<Dictionary<string, object?>>> body)
    {
        cases.Add(new RunnerCase($"{operatorName}_{id}", operatorName, scenario, body));
    }

    private static Operator CreateOperator(OperatorType type, params (string Name, object Value)[] parameters)
    {
        var op = new Operator(Guid.NewGuid(), $"{type}G2P3VisionCore", type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferParameterType(value), value, isRequired: false));
        }

        return op;
    }

    private static string InferParameterType(object value)
    {
        return value switch
        {
            bool => "bool",
            int or long => "int",
            float or double or decimal => "double",
            _ => "string"
        };
    }

    private static Dictionary<string, object> Inputs(params (string Name, object Value)[] values)
    {
        return values.ToDictionary(item => item.Name, item => item.Value);
    }

    private static Dictionary<string, object?> Observed(params (string Name, object? Value)[] values)
    {
        return values.ToDictionary(item => item.Name, item => item.Value);
    }

    private static ImageWrapper CreateGrayImage(int width, int height, byte value)
    {
        return new ImageWrapper(new Mat(height, width, MatType.CV_8UC1, new Scalar(value)));
    }

    private static ImageWrapper CreateColorImage(int width, int height, Scalar color)
    {
        return new ImageWrapper(new Mat(height, width, MatType.CV_8UC3, color));
    }

    private static ImageWrapper CreateIlluminationImage(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var baseValue = 30 + (x * 110 / Math.Max(1, width - 1)) + ((index % 5) * 3);
                mat.Set(y, x, (byte)Math.Clamp(baseValue, 0, 255));
            }
        }

        Cv2.Rectangle(mat, new Rect(8 + (index % 4), 9, 18, 16), new Scalar(220), -1);
        Cv2.Circle(mat, new Point(34, 24 + (index % 5)), 7, new Scalar(15), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateEdgeScene(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(8 + (index % 7), 8, 24, 18 + (index % 5)), Scalar.White, -1);
        Cv2.Circle(mat, new Point(width - 20, height / 2), 8 + (index % 4), new Scalar(180), -1);
        Cv2.Line(mat, new Point(4, height - 8), new Point(width - 6, 12 + (index % 5)), new Scalar(120), 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateSeparatedRectangles(int width, int height, int count, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        for (var i = 0; i < count; i++)
        {
            var x = 8 + (i * 34) + (index % 3);
            var y = 10 + ((i + index) % 4);
            var w = 14 + ((i + index) % 5);
            var h = 16 + ((i * 2 + index) % 5);
            Cv2.Rectangle(mat, new Rect(x, y, w, h), Scalar.White, -1);
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateLineScene(int width, int height, bool horizontal, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        if (horizontal)
        {
            var y = (height / 2) + (index % 5) - 2;
            Cv2.Line(mat, new Point(18, y), new Point(width - 18, y + (index % 2)), Scalar.White, 3);
        }
        else
        {
            var x = (width / 2) + (index % 5) - 2;
            Cv2.Line(mat, new Point(x, 16), new Point(x + (index % 2), height - 16), Scalar.White, 3);
        }

        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateCircleScene(int width, int height, Point center, int radius, bool filled)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Circle(mat, center, radius, Scalar.White, filled ? -1 : 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateStripeScene(int width, int height, int x1, int x2)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(x1, 14, x2 - x1, height - 28), Scalar.White, -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateEllipseScene(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        Cv2.Ellipse(mat, new Point(width / 2, height / 2), new Size(28 + index, 16 + (index % 3)), 15 + index, 0, 360, Scalar.White, 2);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateCheckerScene(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(25, 25, 25));
        for (var y = 0; y < height; y += 12)
        {
            for (var x = 0; x < width; x += 12)
            {
                if (((x / 12) + (y / 12)) % 2 == 0)
                {
                    Cv2.Rectangle(mat, new Rect(x, y, Math.Min(12, width - x), Math.Min(12, height - y)), new Scalar(220, 220, 220), -1);
                }
            }
        }

        Cv2.Circle(mat, new Point(width / 2, height / 2), Math.Min(width, height) / 8, new Scalar(0, 0, 255), -1);
        return new ImageWrapper(mat);
    }

    private static ImageWrapper CreateDistanceMask(int width, int height, int index)
    {
        var mat = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        if (index % 2 == 0)
        {
            Cv2.Rectangle(mat, new Rect(18 + (index % 5), 15, 28 + (index % 6), 26 + (index % 4)), Scalar.White, -1);
        }
        else
        {
            Cv2.Circle(mat, new Point(width / 2, height / 2), 12 + (index % 5), Scalar.White, -1);
        }

        return new ImageWrapper(mat);
    }

    private static DistanceTypes DistanceTypeFor(string distanceType)
    {
        return distanceType.ToLowerInvariant() switch
        {
            "manhattan" => DistanceTypes.L1,
            "chessboard" => DistanceTypes.C,
            _ => DistanceTypes.L2
        };
    }

    private static void RequireSuccess(OperatorExecutionOutput result)
    {
        Require(result.IsSuccess, $"Expected success, got failure: {result.ErrorMessage}");
        Require(result.OutputData is not null, "Expected output data.");
    }

    private static void RequireFailure(OperatorExecutionOutput result)
    {
        Require(!result.IsSuccess, "Expected failure.");
    }

    private static void RequireKey(OperatorExecutionOutput result, string key)
    {
        RequireSuccess(result);
        Require(result.OutputData!.ContainsKey(key), $"Missing output key {key}.");
    }

    private static Mat GetImage(OperatorExecutionOutput result)
    {
        RequireKey(result, "Image");
        Require(result.OutputData!["Image"] is ImageWrapper, "Image output should be ImageWrapper.");
        return ((ImageWrapper)result.OutputData["Image"]).GetMat();
    }

    private static double RequireDouble(OperatorExecutionOutput result, string key)
    {
        RequireKey(result, key);
        return Convert.ToDouble(result.OutputData![key]);
    }

    private static double TryDouble(OperatorExecutionOutput result, string key)
    {
        return result.OutputData != null && result.OutputData.TryGetValue(key, out var raw)
            ? Convert.ToDouble(raw)
            : double.NaN;
    }

    private static int RequireInt(OperatorExecutionOutput result, string key)
    {
        RequireKey(result, key);
        return Convert.ToInt32(result.OutputData![key]);
    }

    private static void RequireIntEquals(OperatorExecutionOutput result, string key, int expected)
    {
        var actual = RequireInt(result, key);
        Require(actual == expected, $"Expected {key}={expected}, got {actual}.");
    }

    private static void RequireValue(OperatorExecutionOutput result, string key, object expected)
    {
        RequireKey(result, key);
        Require(Equals(result.OutputData![key], expected), $"Expected {key}={expected}, got {result.OutputData[key]}.");
    }

    private static IReadOnlyList<object> RequireList(OperatorExecutionOutput result, string key)
    {
        RequireKey(result, key);
        Require(result.OutputData![key] is IEnumerable<object>, $"{key} should be enumerable.");
        return ((IEnumerable<object>)result.OutputData[key]).ToList();
    }

    private static string RequireLabelName(object item)
    {
        if (item is IReadOnlyDictionary<string, object> readOnly &&
            readOnly.TryGetValue("Label", out var readOnlyLabel))
        {
            return Convert.ToString(readOnlyLabel) ?? string.Empty;
        }

        if (item is IDictionary<string, object> dict &&
            dict.TryGetValue("Label", out var label))
        {
            return Convert.ToString(label) ?? string.Empty;
        }

        throw new InvalidOperationException($"Label item should contain a Label field, got {item.GetType().Name}.");
    }

    private static Dictionary<string, object> RequireFitResult(OperatorExecutionOutput result)
    {
        RequireKey(result, "FitResult");
        Require(result.OutputData!["FitResult"] is Dictionary<string, object>, "FitResult should be a dictionary.");
        var fitResult = (Dictionary<string, object>)result.OutputData["FitResult"];
        Require(fitResult.TryGetValue("Success", out var success) && success is bool and true, "Expected fit success.");
        return fitResult;
    }

    private static void RequireString(Dictionary<string, object> data, string key, string expected)
    {
        Require(data.TryGetValue(key, out var actual), $"Missing key {key}.");
        Require(string.Equals(Convert.ToString(actual), expected, StringComparison.OrdinalIgnoreCase), $"Expected {key}={expected}, got {actual}.");
    }

    private static Position RequirePosition(OperatorExecutionOutput result, string key)
    {
        RequireKey(result, key);
        if (result.OutputData![key] is Position position)
        {
            return position;
        }

        throw new InvalidOperationException($"{key} should be Position, got {result.OutputData[key]?.GetType().Name ?? "null"}.");
    }

    private static double[][] RequireMatrix(OperatorExecutionOutput result, string key)
    {
        RequireKey(result, key);
        Require(result.OutputData![key] is double[][], $"{key} should be double[][].");
        return (double[][])result.OutputData[key];
    }

    private static void RequireImagesEqual(Mat actual, Mat expected, string label)
    {
        Require(actual.Size() == expected.Size(), $"{label} size mismatch: {actual.Size()} vs {expected.Size()}.");
        Require(actual.Type() == expected.Type(), $"{label} type mismatch: {actual.Type()} vs {expected.Type()}.");
        using var diff = new Mat();
        Cv2.Absdiff(actual, expected, diff);
        var nonZero = CountNonZero(diff);
        Require(nonZero == 0, $"{label} differs at {nonZero} pixels.");
    }

    private static int CountNonZero(Mat mat)
    {
        if (mat.Channels() == 1)
        {
            return Cv2.CountNonZero(mat);
        }

        using var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        return Cv2.CountNonZero(gray);
    }

    private static void RequireNear(double actual, double expected, double tolerance, string label)
    {
        Require(double.IsFinite(actual), $"{label} should be finite.");
        Require(Math.Abs(actual - expected) <= tolerance, $"{label} expected {expected:0.###}, got {actual:0.###}, tolerance {tolerance:0.###}.");
    }

    private static void RequirePositionNear(Position actual, double expectedX, double expectedY, double tolerance, string label)
    {
        var distance = Math.Sqrt(Math.Pow(actual.X - expectedX, 2) + Math.Pow(actual.Y - expectedY, 2));
        Require(distance <= tolerance, $"{label} expected near ({expectedX},{expectedY}), got ({actual.X},{actual.Y}), distance {distance:0.###}.");
    }

    private static void RequireAtLeast(double actual, double min, string label)
    {
        Require(actual >= min, $"{label} expected at least {min}, got {actual}.");
    }

    private static void RequireAtLeast(int actual, int min, string label)
    {
        Require(actual >= min, $"{label} expected at least {min}, got {actual}.");
    }

    private static void RequireAtMost(double actual, double max, string label)
    {
        Require(actual <= max, $"{label} expected at most {max}, got {actual}.");
    }

    private static void RequireLineAngle(double angle, bool horizontal)
    {
        var normalized = Math.Abs(angle) % 180.0;
        var distanceToHorizontal = Math.Min(normalized, Math.Abs(180.0 - normalized));
        var distanceToVertical = Math.Abs(90.0 - normalized);
        if (horizontal)
        {
            Require(distanceToHorizontal <= 6.0, $"Expected horizontal angle, got {angle}.");
        }
        else
        {
            Require(distanceToVertical <= 6.0, $"Expected vertical angle, got {angle}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record RunnerCase(
    string Id,
    string Operator,
    string Scenario,
    Func<Task<Dictionary<string, object?>>> Body);

internal sealed record BaselineResult(
    string EvidenceKind,
    BaselineSummary Summary,
    IReadOnlyList<OperatorEvidence> Operators,
    IReadOnlyList<ScenarioSummary> Scenarios,
    IReadOnlyList<CaseResult> Cases);

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
    long MemoryAllocationBytesAvg);

internal sealed record ScenarioSummary(
    string Scenario,
    int CaseCount,
    int Passed,
    int Failed,
    double RuntimeMsAvg);

internal sealed record CaseResult(
    string CaseId,
    string Operator,
    string Scenario,
    bool Passed,
    double RuntimeMs,
    long MemoryAllocationBytes,
    string? ErrorMessage,
    Dictionary<string, object?> Observed);

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# G2 P3 Vision Core Baseline",
            "",
            $"EvidenceKind: `{result.EvidenceKind}`",
            $"GeneratedAtUtc: `{result.Summary.GeneratedAtUtc:O}`",
            "",
            "## Summary",
            "",
            "| Metric | Value |",
            "| --- | ---: |",
            $"| Cases | {result.Summary.CaseCount} |",
            $"| Passed | {result.Summary.Passed} |",
            $"| Failed | {result.Summary.Failed} |",
            $"| Runtime ms | {result.Summary.RuntimeMs:0.###} |",
            "",
            "## Operators",
            "",
            "| Operator | Cases | Passed | Failed | Avg ms | Avg bytes |",
            "| --- | ---: | ---: | ---: | ---: | ---: |"
        };

        lines.AddRange(result.Operators.Select(item =>
            $"| {item.Operator} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} | {item.MemoryAllocationBytesAvg} |"));

        lines.AddRange(
        [
            "",
            "## Scenarios",
            "",
            "| Scenario | Cases | Passed | Failed | Avg ms |",
            "| --- | ---: | ---: | ---: | ---: |"
        ]);
        lines.AddRange(result.Scenarios.Select(item =>
            $"| {item.Scenario} | {item.CaseCount} | {item.Passed} | {item.Failed} | {item.RuntimeMsAvg:0.###} |"));

        var failures = result.Cases.Where(item => !item.Passed).ToList();
        if (failures.Count > 0)
        {
            lines.AddRange(
            [
                "",
                "## Failures",
                "",
                "| Case | Operator | Scenario | Error |",
                "| --- | --- | --- | --- |"
            ]);
            lines.AddRange(failures.Select(item =>
                $"| {item.CaseId} | {item.Operator} | {item.Scenario} | {item.ErrorMessage} |"));
        }

        lines.Add("");
        return string.Join(Environment.NewLine, lines);
    }
}

internal sealed record RunnerOptions(
    string OutputPath,
    string? ReportPath,
    bool ShowHelp,
    string? ParseError)
{
    public static RunnerOptions Parse(string[] args)
    {
        var output = "quality/evals/reports/G2P3VisionCore_baseline.json";
        string? report = "quality/evals/reports/G2P3VisionCore_baseline.md";
        string? parseError = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--output":
                    output = NextValue(args, ref i, "--output", ref parseError);
                    break;
                case "--report":
                    report = NextValue(args, ref i, "--report", ref parseError);
                    break;
                default:
                    parseError = $"Unknown argument: {args[i]}";
                    break;
            }
        }

        return new RunnerOptions(output, report, showHelp, parseError);
    }

    public static void PrintHelp()
    {
        Console.WriteLine(
            """
            G2 P3 vision core runner

            Options:
              --output <path>  Baseline JSON output path.
              --report <path>  Markdown report output path.
              --help           Show help.
            """);
    }

    private static string NextValue(string[] args, ref int index, string optionName, ref string? parseError)
    {
        if (index + 1 >= args.Length)
        {
            parseError = $"Missing value for {optionName}";
            return string.Empty;
        }

        index++;
        return args[index];
    }
}

internal static class JsonSettings
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
