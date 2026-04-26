using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime.Tensors;
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
    $"SemanticSegmentation contract baseline complete: {result.Summary.Passed}/{result.Summary.CaseCount} passed, " +
    $"failed={result.Summary.Failed}, output={options.OutputPath}");

return result.Summary.Failed == 0 ? 0 : 1;

internal static class ContractRunner
{
    private static readonly SemanticSegmentationOperator Operator = new(NullLogger<SemanticSegmentationOperator>.Instance);
    private static readonly Type ChannelOrderType = typeof(SemanticSegmentationOperator)
        .GetNestedType("SegmentationChannelOrder", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("SemanticSegmentationOperator.SegmentationChannelOrder not found.");

    public static async Task<BaselineResult> RunAsync()
    {
        var cases = new List<ContractCase>
        {
            new("identity_direct_class_map_exact", "End-to-end identity model", IdentityDirectClassMapExact),
            new("identity_output_types_and_size", "End-to-end identity model", IdentityOutputTypesAndSize),
            new("identity_present_classes_and_count", "End-to-end identity model", IdentityPresentClassesAndCount),
            new("identity_class_masks_exact", "End-to-end identity model", IdentityClassMasksExact),
            new("identity_colored_map_matches_palette", "End-to-end identity model", IdentityColoredMapMatchesPalette),
            new("catalog_resolves_model_defaults", "Model catalog contract", CatalogResolvesModelDefaults),
            new("validate_catalog_defaults_valid", "Model catalog contract", ValidateCatalogDefaultsValid),
            new("validate_missing_model_invalid", "Validation contract", ValidateMissingModelInvalid),
            new("validate_bad_input_size_invalid", "Validation contract", ValidateBadInputSizeInvalid),
            new("validate_zero_std_invalid", "Validation contract", ValidateZeroStdInvalid),
            new("validate_bad_execution_provider_invalid", "Validation contract", ValidateBadExecutionProviderInvalid),
            new("execute_missing_image_fails", "Failure contract", ExecuteMissingImageFails),
            new("execute_missing_model_fails", "Failure contract", ExecuteMissingModelFails),
            new("execute_bad_mean_fails", "Failure contract", ExecuteBadMeanFails),
            new("parse_size_accepts_trimmed_pair", "Parser contract", ParseSizeAcceptsTrimmedPair),
            new("parse_size_rejects_zero", "Parser contract", ParseSizeRejectsZero),
            new("parse_float_triplet_accepts_three_values", "Parser contract", ParseFloatTripletAcceptsThreeValues),
            new("parse_float_triplet_rejects_two_values", "Parser contract", ParseFloatTripletRejectsTwoValues),
            new("class_names_json_expands_missing", "Class-name contract", ClassNamesJsonExpandsMissing),
            new("class_names_comma_truncates_extra", "Class-name contract", ClassNamesCommaTruncatesExtra),
            new("class_names_empty_fallback", "Class-name contract", ClassNamesEmptyFallback),
            new("class_names_bad_json_fails", "Class-name contract", ClassNamesBadJsonFails),
            new("preprocess_rgb_channel_order", "Preprocess contract", PreprocessRgbChannelOrder),
            new("preprocess_bgr_channel_order", "Preprocess contract", PreprocessBgrChannelOrder),
            new("preprocess_unit_range_mean_std", "Preprocess contract", PreprocessUnitRangeMeanStd),
            new("preprocess_grayscale_promotes_to_three_channels", "Preprocess contract", PreprocessGrayscalePromotes),
            new("palette_color_is_stable_per_class", "Visualization contract", PaletteColorStable)
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
                "SemanticSegmentation",
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

    private static async Task IdentityDirectClassMapExact()
    {
        var result = await ExecuteIdentityAsync(CreateDirectOperator());
        try
        {
            RequireSuccess(result);
            var map = GetOutput<ImageWrapper>(result.OutputData!, "SegmentationMap").MatReadOnly;
            var indexer = map.GetGenericIndexer<byte>();

            Require(indexer[0, 0] == 0, "Expected class 0 at (0,0).");
            Require(indexer[0, 1] == 1, "Expected class 1 at (0,1).");
            Require(indexer[1, 0] == 2, "Expected class 2 at (1,0).");
            Require(indexer[1, 1] == 0, "Expected class 0 at (1,1).");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task IdentityOutputTypesAndSize()
    {
        var result = await ExecuteIdentityAsync(CreateDirectOperator());
        try
        {
            RequireSuccess(result);
            var map = GetOutput<ImageWrapper>(result.OutputData!, "SegmentationMap").MatReadOnly;
            var colored = GetOutput<ImageWrapper>(result.OutputData!, "ColoredMap").MatReadOnly;

            Require(map.Type() == MatType.CV_8UC1, $"Expected CV_8UC1 map, got {map.Type()}.");
            Require(colored.Type() == MatType.CV_8UC3, $"Expected CV_8UC3 colored map, got {colored.Type()}.");
            Require(map.Width == 2 && map.Height == 2, $"Expected 2x2 map, got {map.Width}x{map.Height}.");
            Require(colored.Width == 2 && colored.Height == 2, $"Expected 2x2 colored map, got {colored.Width}x{colored.Height}.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task IdentityPresentClassesAndCount()
    {
        var result = await ExecuteIdentityAsync(CreateDirectOperator());
        try
        {
            RequireSuccess(result);
            Require(Convert.ToInt32(result.OutputData!["ClassCount"]) == 3, "Expected ClassCount=3.");
            var present = GetOutput<string[]>(result.OutputData!, "PresentClasses");
            RequireSetEquals(present, ["red", "green", "blue"], "PresentClasses mismatch.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task IdentityClassMasksExact()
    {
        var result = await ExecuteIdentityAsync(CreateDirectOperator());
        try
        {
            RequireSuccess(result);
            var masks = GetOutput<Dictionary<string, object>>(result.OutputData!, "ClassMasks");
            RequireSetEquals(masks.Keys, ["red", "green", "blue"], "ClassMasks keys mismatch.");

            AssertMask(masks, "red", [[255, 0], [0, 255]]);
            AssertMask(masks, "green", [[0, 255], [0, 0]]);
            AssertMask(masks, "blue", [[0, 0], [255, 0]]);
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task IdentityColoredMapMatchesPalette()
    {
        var result = await ExecuteIdentityAsync(CreateDirectOperator());
        try
        {
            RequireSuccess(result);
            var colored = GetOutput<ImageWrapper>(result.OutputData!, "ColoredMap").MatReadOnly;
            var indexer = colored.GetGenericIndexer<Vec3b>();

            RequireVecEquals(indexer[0, 0], GetPaletteColor(0, 3), "Class 0 color mismatch.");
            RequireVecEquals(indexer[0, 1], GetPaletteColor(1, 3), "Class 1 color mismatch.");
            RequireVecEquals(indexer[1, 0], GetPaletteColor(2, 3), "Class 2 color mismatch.");
            RequireVecEquals(indexer[1, 1], GetPaletteColor(0, 3), "Class 0 repeat color mismatch.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task CatalogResolvesModelDefaults()
    {
        var result = await ExecuteIdentityAsync(CreateCatalogOperator());
        try
        {
            RequireSuccess(result);
            Require(Convert.ToInt32(result.OutputData!["ClassCount"]) == 3, "Expected ClassCount=3 from catalog.");
            RequireSetEquals(GetOutput<string[]>(result.OutputData!, "PresentClasses"), ["red", "green", "blue"], "Catalog class names mismatch.");
            Require((string)result.OutputData["ResolvedModelId"] == "semantic_identity_2x2", "ResolvedModelId mismatch.");
            Require((string)result.OutputData["ModelSource"] == "ModelCatalog", "ModelSource mismatch.");
            Require(((string)result.OutputData["ResolvedModelPath"]).EndsWith("identity_2x2.onnx", StringComparison.OrdinalIgnoreCase), "ResolvedModelPath mismatch.");
            var provenance = GetOutput<Dictionary<string, object>>(result.OutputData, "ModelProvenance");
            Require(provenance.TryGetValue("ModelType", out var modelType) && modelType?.ToString() == "segmentation", "Model provenance type mismatch.");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static Task ValidateCatalogDefaultsValid()
    {
        var validation = Operator.ValidateParameters(CreateCatalogOperator());
        Require(validation.IsValid, ValidationErrors(validation) ?? "Catalog defaults should validate.");
        return Task.CompletedTask;
    }

    private static Task ValidateMissingModelInvalid()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "ModelPath", Path.Combine(Path.GetTempPath(), "missing-semantic-model.onnx"), "file");
        var validation = Operator.ValidateParameters(op);
        Require(!validation.IsValid, "Missing model should be invalid.");
        RequireContains(ValidationErrors(validation), "Model file not found");
        return Task.CompletedTask;
    }

    private static Task ValidateBadInputSizeInvalid()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "InputSize", "bad-size", "string");
        var validation = Operator.ValidateParameters(op);
        Require(!validation.IsValid, "Bad input size should be invalid.");
        RequireContains(ValidationErrors(validation), "InputSize");
        return Task.CompletedTask;
    }

    private static Task ValidateZeroStdInvalid()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "Std", "1,0,1", "string");
        var validation = Operator.ValidateParameters(op);
        Require(!validation.IsValid, "Zero std should be invalid.");
        RequireContains(ValidationErrors(validation), "Std");
        return Task.CompletedTask;
    }

    private static Task ValidateBadExecutionProviderInvalid()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "ExecutionProvider", "tpu", "string");
        var validation = Operator.ValidateParameters(op);
        Require(!validation.IsValid, "Bad execution provider should be invalid.");
        RequireContains(ValidationErrors(validation), "ExecutionProvider");
        return Task.CompletedTask;
    }

    private static async Task ExecuteMissingImageFails()
    {
        var result = await Operator.ExecuteAsync(CreateDirectOperator(), []);
        Require(!result.IsSuccess, "Missing image should fail.");
        RequireContains(result.ErrorMessage, "Input image is required");
    }

    private static async Task ExecuteMissingModelFails()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "ModelPath", Path.Combine(Path.GetTempPath(), "missing-semantic-model.onnx"), "file");

        var result = await ExecuteIdentityAsync(op);
        try
        {
            Require(!result.IsSuccess, "Missing model should fail.");
            RequireContains(result.ErrorMessage, "Model file not found");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static async Task ExecuteBadMeanFails()
    {
        var op = CreateDirectOperator();
        SetParameter(op, "Mean", "0,0", "string");

        var result = await ExecuteIdentityAsync(op);
        try
        {
            Require(!result.IsSuccess, "Bad Mean should fail.");
            RequireContains(result.ErrorMessage, "Mean/Std");
        }
        finally
        {
            DisposeOutputImages(result.OutputData);
        }
    }

    private static Task ParseSizeAcceptsTrimmedPair()
    {
        var (ok, width, height) = TryParseSize(" 12, 34 ");
        Require(ok, "Expected valid size.");
        Require(width == 12 && height == 34, $"Expected 12x34, got {width}x{height}.");
        return Task.CompletedTask;
    }

    private static Task ParseSizeRejectsZero()
    {
        var (ok, _, _) = TryParseSize("0,34");
        Require(!ok, "Zero width should be rejected.");
        return Task.CompletedTask;
    }

    private static Task ParseFloatTripletAcceptsThreeValues()
    {
        var (ok, values) = TryParseFloatTriplet("1, 2.5, 3");
        Require(ok, "Expected valid triplet.");
        RequireClose(values[0], 1f, "Triplet[0]");
        RequireClose(values[1], 2.5f, "Triplet[1]");
        RequireClose(values[2], 3f, "Triplet[2]");
        return Task.CompletedTask;
    }

    private static Task ParseFloatTripletRejectsTwoValues()
    {
        var (ok, _) = TryParseFloatTriplet("1,2");
        Require(!ok, "Two-value triplet should be rejected.");
        return Task.CompletedTask;
    }

    private static Task ClassNamesJsonExpandsMissing()
    {
        var names = ParseClassNames("[\"road\"]", 3);
        RequireSequence(names, ["road", "class_1", "class_2"], "JSON class names should expand missing values.");
        return Task.CompletedTask;
    }

    private static Task ClassNamesCommaTruncatesExtra()
    {
        var names = ParseClassNames("red, green, blue, alpha", 3);
        RequireSequence(names, ["red", "green", "blue"], "Comma class names should truncate extras.");
        return Task.CompletedTask;
    }

    private static Task ClassNamesEmptyFallback()
    {
        var names = ParseClassNames("", 2);
        RequireSequence(names, ["class_0", "class_1"], "Empty class names should fallback to class IDs.");
        return Task.CompletedTask;
    }

    private static Task ClassNamesBadJsonFails()
    {
        var failed = false;
        try
        {
            _ = ParseClassNames("[\"red\",", 3);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            failed = true;
        }

        Require(failed, "Bad JSON class names should fail.");
        return Task.CompletedTask;
    }

    private static Task PreprocessRgbChannelOrder()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var tensor = Preprocess(mat, 1, 1, "RGB", [0f, 0f, 0f], [1f, 1f, 1f], scaleToUnitRange: false);

        RequireClose(tensor[0, 0, 0, 0], 30f, "RGB channel 0");
        RequireClose(tensor[0, 1, 0, 0], 20f, "RGB channel 1");
        RequireClose(tensor[0, 2, 0, 0], 10f, "RGB channel 2");
        return Task.CompletedTask;
    }

    private static Task PreprocessBgrChannelOrder()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC3, new Scalar(10, 20, 30));
        var tensor = Preprocess(mat, 1, 1, "BGR", [0f, 0f, 0f], [1f, 1f, 1f], scaleToUnitRange: false);

        RequireClose(tensor[0, 0, 0, 0], 10f, "BGR channel 0");
        RequireClose(tensor[0, 1, 0, 0], 20f, "BGR channel 1");
        RequireClose(tensor[0, 2, 0, 0], 30f, "BGR channel 2");
        return Task.CompletedTask;
    }

    private static Task PreprocessUnitRangeMeanStd()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC3, new Scalar(255, 128, 0));
        var tensor = Preprocess(mat, 1, 1, "RGB", [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f], scaleToUnitRange: true);

        RequireClose(tensor[0, 0, 0, 0], -1f, "Unit RGB channel 0");
        RequireClose(tensor[0, 1, 0, 0], ((128f / 255f) - 0.5f) / 0.5f, "Unit RGB channel 1");
        RequireClose(tensor[0, 2, 0, 0], 1f, "Unit RGB channel 2");
        return Task.CompletedTask;
    }

    private static Task PreprocessGrayscalePromotes()
    {
        using var mat = new Mat(1, 1, MatType.CV_8UC1, new Scalar(50));
        var tensor = Preprocess(mat, 1, 1, "RGB", [0f, 0f, 0f], [1f, 1f, 1f], scaleToUnitRange: false);

        RequireClose(tensor[0, 0, 0, 0], 50f, "Grayscale channel 0");
        RequireClose(tensor[0, 1, 0, 0], 50f, "Grayscale channel 1");
        RequireClose(tensor[0, 2, 0, 0], 50f, "Grayscale channel 2");
        return Task.CompletedTask;
    }

    private static Task PaletteColorStable()
    {
        var class0 = GetPaletteColor(0, 4);
        var class1 = GetPaletteColor(1, 4);
        var class4 = GetPaletteColor(4, 4);

        RequireVecEquals(class0, new Vec3b(35, 35, 255), "Class 0 palette baseline changed.");
        Require(class1.Item0 != class0.Item0 || class1.Item1 != class0.Item1 || class1.Item2 != class0.Item2, "Class 1 color should differ from class 0.");
        RequireVecEquals(class4, new Vec3b(35, 255, 240), "Class 4 palette baseline changed.");
        return Task.CompletedTask;
    }

    private static async Task<Acme.Product.Core.Operators.OperatorExecutionOutput> ExecuteIdentityAsync(Operator op)
    {
        var mat = Cv2.ImRead(IdentityInputPath(), ImreadModes.Color);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidOperationException("Failed to load identity input image.");
        }

        using var image = new ImageWrapper(mat);
        return await Operator.ExecuteAsync(op, new Dictionary<string, object> { ["Image"] = image });
    }

    private static Operator CreateDirectOperator()
    {
        var op = new Operator("semantic_contract", OperatorType.SemanticSegmentation, 0, 0);
        AddParameter(op, "ModelPath", IdentityModelPath(), "file");
        AddParameter(op, "ModelId", string.Empty, "string");
        AddParameter(op, "ModelCatalogPath", string.Empty, "file");
        AddParameter(op, "InputSize", "2,2", "string");
        AddParameter(op, "NumClasses", 3, "int");
        AddParameter(op, "ClassNames", "[\"red\",\"green\",\"blue\"]", "string");
        AddParameter(op, "ExecutionProvider", "cpu", "string");
        AddParameter(op, "ScaleToUnitRange", true, "bool");
        AddParameter(op, "ChannelOrder", "RGB", "string");
        AddParameter(op, "Mean", "0,0,0", "string");
        AddParameter(op, "Std", "1,1,1", "string");
        return op;
    }

    private static Operator CreateCatalogOperator()
    {
        var op = new Operator("semantic_catalog_contract", OperatorType.SemanticSegmentation, 0, 0);
        AddParameter(op, "ModelPath", string.Empty, "file");
        AddParameter(op, "ModelId", "semantic_identity_2x2", "string");
        AddParameter(op, "ModelCatalogPath", RepoPath("models/model_catalog.json"), "file");
        AddParameter(op, "InputSize", "512,512", "string");
        AddParameter(op, "NumClasses", 21, "int");
        AddParameter(op, "ClassNames", string.Empty, "string");
        AddParameter(op, "ExecutionProvider", "cpu", "string");
        AddParameter(op, "ScaleToUnitRange", true, "bool");
        AddParameter(op, "ChannelOrder", "RGB", "string");
        AddParameter(op, "Mean", "0,0,0", "string");
        AddParameter(op, "Std", "1,1,1", "string");
        return op;
    }

    private static void AddParameter(Operator op, string name, object? value, string dataType)
    {
        op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, dataType, value));
    }

    private static void SetParameter(Operator op, string name, object? value, string dataType)
    {
        var existing = op.Parameters.FirstOrDefault(x => x.Name == name);
        if (existing is null)
        {
            AddParameter(op, name, value, dataType);
            return;
        }

        existing.SetValue(value);
    }

    private static T GetOutput<T>(Dictionary<string, object> output, string key)
    {
        Require(output.TryGetValue(key, out var value), $"Missing output key '{key}'.");
        Require(value is T, $"Output key '{key}' expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
        return (T)value!;
    }

    private static void AssertMask(Dictionary<string, object> masks, string name, int[][] expected)
    {
        Require(masks.TryGetValue(name, out var value), $"Missing mask '{name}'.");
        Require(value is ImageWrapper, $"Mask '{name}' should be ImageWrapper.");
        var mat = ((ImageWrapper)value!).MatReadOnly;
        Require(mat.Type() == MatType.CV_8UC1, $"Mask '{name}' should be CV_8UC1.");
        Require(mat.Rows == expected.Length && mat.Cols == expected[0].Length, $"Mask '{name}' size mismatch.");

        var indexer = mat.GetGenericIndexer<byte>();
        for (var y = 0; y < expected.Length; y++)
        {
            for (var x = 0; x < expected[y].Length; x++)
            {
                Require(indexer[y, x] == expected[y][x], $"Mask '{name}' mismatch at ({x},{y}).");
            }
        }
    }

    private static DenseTensor<float> Preprocess(
        Mat image,
        int width,
        int height,
        string channelOrder,
        float[] mean,
        float[] std,
        bool scaleToUnitRange)
    {
        return (DenseTensor<float>)InvokeStatic(
            "PreprocessImage",
            image,
            width,
            height,
            ParseChannelOrder(channelOrder),
            mean,
            std,
            scaleToUnitRange)!;
    }

    private static object ParseChannelOrder(string value)
    {
        return Enum.Parse(ChannelOrderType, value, ignoreCase: false);
    }

    private static string[] ParseClassNames(string raw, int numClasses)
    {
        return (string[])InvokeStatic("ParseClassNames", raw, numClasses)!;
    }

    private static (bool ok, int width, int height) TryParseSize(string raw)
    {
        object?[] args = [raw, 0, 0];
        var ok = (bool)InvokeStatic("TryParseSize", args)!;
        return (ok, (int)args[1]!, (int)args[2]!);
    }

    private static (bool ok, float[] values) TryParseFloatTriplet(string raw)
    {
        object?[] args = [raw, Array.Empty<float>()];
        var ok = (bool)InvokeStatic("TryParseFloatTriplet", args)!;
        return (ok, (float[])args[1]!);
    }

    private static Vec3b GetPaletteColor(int classId, int numClasses)
    {
        return (Vec3b)InvokeStatic("GetPaletteColor", classId, numClasses)!;
    }

    private static object? InvokeStatic(string name, params object?[] args)
    {
        var method = typeof(SemanticSegmentationOperator).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(SemanticSegmentationOperator), name);
        return method.Invoke(null, args);
    }

    private static void RequireSuccess(Acme.Product.Core.Operators.OperatorExecutionOutput result)
    {
        Require(result.IsSuccess, result.ErrorMessage ?? "Expected operator execution success.");
        Require(result.OutputData is not null, "Expected output data.");
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

    private static string? ValidationErrors(Acme.Product.Core.Operators.ValidationResult validation)
    {
        return validation.Errors.Count == 0 ? null : string.Join("; ", validation.Errors);
    }

    private static void RequireClose(float actual, float expected, string label, float tolerance = 1e-4f)
    {
        if (Math.Abs(actual - expected) > tolerance)
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }

    private static void RequireSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected, string message)
    {
        Require(actual.Count == expected.Count && actual.SequenceEqual(expected, StringComparer.Ordinal), $"{message} Actual=[{string.Join(", ", actual)}].");
    }

    private static void RequireSetEquals(IEnumerable<string> actual, IEnumerable<string> expected, string message)
    {
        var actualSet = actual.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expectedSet = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Require(actualSet.SequenceEqual(expectedSet, StringComparer.Ordinal), $"{message} Actual=[{string.Join(", ", actualSet)}].");
    }

    private static void RequireVecEquals(Vec3b actual, Vec3b expected, string message)
    {
        Require(
            actual.Item0 == expected.Item0 && actual.Item1 == expected.Item1 && actual.Item2 == expected.Item2,
            $"{message} Expected=({expected.Item0},{expected.Item1},{expected.Item2}) Actual=({actual.Item0},{actual.Item1},{actual.Item2}).");
    }

    private static void DisposeOutputImages(Dictionary<string, object>? outputData)
    {
        if (outputData is null)
        {
            return;
        }

        foreach (var image in outputData.Values.OfType<ImageWrapper>())
        {
            image.Dispose();
        }

        if (outputData.TryGetValue("ClassMasks", out var classMasksObj) &&
            classMasksObj is Dictionary<string, object> classMasks)
        {
            foreach (var image in classMasks.Values.OfType<ImageWrapper>())
            {
                image.Dispose();
            }
        }
    }

    private static string IdentityModelPath()
    {
        return RepoPath("Acme.Product/tests/TestData/model_test_suite/identity_2x2/identity_2x2.onnx");
    }

    private static string IdentityInputPath()
    {
        return RepoPath("Acme.Product/tests/TestData/model_test_suite/identity_2x2/input.png");
    }

    private static string RepoPath(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.Name.Equals("ClearVision", StringComparison.OrdinalIgnoreCase))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new DirectoryNotFoundException("Failed to resolve repository root.");
        }

        return Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
        var outputPath = "quality/evals/reports/SemanticSegmentation_contract_baseline.json";
        string? reportPath = "quality/evals/reports/SemanticSegmentation_contract_baseline.md";
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
        Console.WriteLine("Usage: dotnet run --project quality/tools/SemanticSegmentationContractRunner/SemanticSegmentationContractRunner.csproj -- [--output PATH] [--report PATH] [--no-report]");
    }
}

internal static class MarkdownReport
{
    public static string Create(BaselineResult result)
    {
        var lines = new List<string>
        {
            "# SemanticSegmentation Contract Baseline",
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
            "- This is a contract baseline using the repo-local identity 2x2 ONNX segmentation model plus direct private-helper contract checks.",
            "- It validates class-map argmax behavior, mask generation, palette mapping, model catalog resolution, parser validation, failure paths, and preprocessing channel/range contracts.",
            "- It does not claim real segmentation accuracy; dataset quality should be evaluated separately with a public or field segmentation dataset."
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
