using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Detection, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.ModelAsset, TestExpectedDuration.Long, TestFlakyPolicy.Blocking, "operator-quality")]
public sealed class DeepLearningMultiTaskOperatorTests
{
    private readonly DeepLearningOperator _sut = new(
        Substitute.For<ILogger<DeepLearningOperator>>());

    [Fact]
    public void MissingTaskType_ShouldResolveToHistoricalObjectDetectionDefault()
    {
        DeepLearningTaskResolver.TryParse(null, out var taskType).Should().BeTrue();
        taskType.Should().Be(DeepLearningTaskType.ObjectDetection);
    }

    [Theory]
    [InlineData(new[] { 1, 3 }, DeepLearningTaskType.ImageClassification)]
    [InlineData(new[] { 1, 84, 8400 }, DeepLearningTaskType.ObjectDetection)]
    [InlineData(new[] { 1, 3, 2, 2 }, DeepLearningTaskType.SemanticSegmentation)]
    public void AutoTaskResolution_WithUniqueOutputShape_ShouldResolveReliably(
        int[] shape,
        DeepLearningTaskType expected)
    {
        var resolution = DeepLearningTaskResolver.Resolve(
            DeepLearningTaskType.Auto,
            catalogType: null,
            [new OnnxOutputSignature("output", shape)]);

        resolution.TaskType.Should().Be(expected);
        resolution.Source.Should().Be("OutputShape");
    }

    [Fact]
    public void AutoTaskResolution_WithUnknownShape_ShouldFailInsteadOfGuessing()
    {
        var action = () => DeepLearningTaskResolver.Resolve(
            DeepLearningTaskType.Auto,
            catalogType: null,
            [new OnnxOutputSignature("embedding", [1, 1, 5])]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not reliably resolve*");
    }

    [Fact]
    public void AutoTaskResolution_WithDynamicRankTwoDetectionShape_ShouldFailInsteadOfClassifying()
    {
        var action = () => DeepLearningTaskResolver.Resolve(
            DeepLearningTaskType.Auto,
            catalogType: null,
            [new OnnxOutputSignature("detections", [-1, 6])]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not reliably resolve*");
    }

    [Fact]
    public void AutoTaskResolution_WithSingleSixValueRow_ShouldFailAsAmbiguous()
    {
        var action = () => DeepLearningTaskResolver.Resolve(
            DeepLearningTaskType.Auto,
            catalogType: null,
            [new OnnxOutputSignature("output", [1, 6])]);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*could not reliably resolve*");
    }

    [Fact]
    public void AutoTaskResolution_WithMultipleDetectionRows_ShouldResolveDetection()
    {
        var resolution = DeepLearningTaskResolver.Resolve(
            DeepLearningTaskType.Auto,
            catalogType: null,
            [new OnnxOutputSignature("detections", [20, 6])]);

        resolution.TaskType.Should().Be(DeepLearningTaskType.ObjectDetection);
        resolution.Source.Should().Be("OutputShape");
    }

    [Theory]
    [InlineData(true, new[] { 1, 3, 16, 16 }, 3)]
    [InlineData(false, new[] { 1, 16, 16, 5 }, 5)]
    public void SegmentationClassCountInference_ShouldRespectTensorLayout(
        bool channelsFirst,
        int[] outputShape,
        int expectedClassCount)
    {
        var resolved = DeepLearningOperator.InferSegmentationClassCountFromOutputShapes(
            fallback: 21,
            channelsFirst,
            [new OnnxOutputSignature("segmentation", outputShape)]);

        resolved.Should().Be(expectedClassCount);
    }

    [Fact]
    public void SegmentationClassCountInference_WithConflictingOutputs_ShouldKeepFallback()
    {
        var resolved = DeepLearningOperator.InferSegmentationClassCountFromOutputShapes(
            fallback: 21,
            channelsFirst: true,
            [
                new OnnxOutputSignature("primary", [1, 3, 16, 16]),
                new OnnxOutputSignature("auxiliary", [1, 5, 8, 8])
            ]);

        resolved.Should().Be(21);
    }

    [Fact]
    public void ClassificationPostprocess_Logits_ShouldReturnOrderedSoftmaxTopK()
    {
        var result = DeepLearningOperator.PostprocessClassification(
            [0f, 2f, 1f],
            ["red", "green", "blue"],
            topK: 2,
            scoreMode: "Logits");

        result.ResolvedScoreMode.Should().Be("Logits");
        result.TopPrediction.Label.Should().Be("green");
        result.Predictions.Should().HaveCount(2);
        result.Predictions.Sum(item => item.Confidence).Should().BeLessThan(1.0);
    }

    [Fact]
    public void ClassificationPostprocess_InvalidProbabilityVector_ShouldFail()
    {
        var action = () => DeepLearningOperator.PostprocessClassification(
            [0.2f, 0.2f, 0.2f],
            ["red", "green", "blue"],
            topK: 3,
            scoreMode: "Probabilities");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*sum is approximately 1*");
    }

    [Fact]
    public async Task ClassificationExplicitTask_ShouldRunRealOnnxInference()
    {
        var op = CreateDeepLearningOperator("ImageClassification", ClassificationModelPath());
        using var image = SolidImage(new Scalar(0, 0, 255));

        var result = await _sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        try
        {
            result.OutputData!["TaskType"].Should().Be("ImageClassification");
            result.OutputData["RequestedTaskType"].Should().Be("ImageClassification");
            result.OutputData["TaskResolutionSource"].Should().Be("Explicit");
            result.OutputData["TopClassLabel"].Should().Be("red");
            Convert.ToDouble(result.OutputData["TopClassConfidence"]).Should().BeGreaterThan(0.99);
            result.OutputData["ClassificationTopK"]
                .Should().BeAssignableTo<IReadOnlyCollection<Dictionary<string, object>>>()
                .Which.Should().HaveCount(3);
            result.OutputData.Should().NotContainKey("DetectionList");
        }
        finally
        {
            DisposeImageOutputs(result.OutputData);
        }
    }

    [Fact]
    public async Task ClassificationAutoFromCatalog_ShouldUseCatalogTaskAndInputSize()
    {
        var op = CreateDeepLearningOperator(
            "Auto",
            modelPath: string.Empty,
            modelId: "classification_color_mean_2x2");
        using var image = SolidImage(new Scalar(0, 255, 0));

        var result = await _sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        try
        {
            result.OutputData!["TaskType"].Should().Be("ImageClassification");
            result.OutputData["TaskResolutionSource"].Should().Be("ModelCatalog");
            result.OutputData["TopClassLabel"].Should().Be("green");
            result.OutputData["ResolvedModelId"].Should().Be("classification_color_mean_2x2");
            var diagnostics = result.OutputData["PostprocessDiagnostics"]
                .Should().BeAssignableTo<IDictionary<string, object>>().Subject;
            diagnostics["InputSizeSource"].Should().Be("ModelCatalog");
        }
        finally
        {
            DisposeImageOutputs(result.OutputData);
        }
    }

    [Fact]
    public async Task SemanticSegmentation_ShouldMatchProfessionalOperatorCore()
    {
        var unified = CreateDeepLearningOperator("SemanticSegmentation", SegmentationModelPath());
        var professional = CreateSegmentationOperator();
        var professionalExecutor = new SemanticSegmentationOperator(
            Substitute.For<ILogger<SemanticSegmentationOperator>>());
        using var unifiedImage = LoadSegmentationInput();
        using var professionalImage = LoadSegmentationInput();

        var unifiedResult = await _sut.ExecuteAsync(unified, TestHelpers.CreateImageInputs(unifiedImage));
        var professionalResult = await professionalExecutor.ExecuteAsync(
            professional,
            TestHelpers.CreateImageInputs(professionalImage));

        unifiedResult.IsSuccess.Should().BeTrue(unifiedResult.ErrorMessage);
        professionalResult.IsSuccess.Should().BeTrue(professionalResult.ErrorMessage);
        try
        {
            unifiedResult.OutputData!["TaskType"].Should().Be("SemanticSegmentation");
            var unifiedMap = unifiedResult.OutputData["SegmentationMap"].Should().BeOfType<ImageWrapper>().Subject;
            var professionalMap = professionalResult.OutputData!["SegmentationMap"].Should().BeOfType<ImageWrapper>().Subject;
            Cv2.Norm(unifiedMap.MatReadOnly, professionalMap.MatReadOnly, NormTypes.L1).Should().Be(0.0);
            var unifiedColored = unifiedResult.OutputData["ColoredMap"].Should().BeOfType<ImageWrapper>().Subject;
            var professionalColored = professionalResult.OutputData["ColoredMap"].Should().BeOfType<ImageWrapper>().Subject;
            Cv2.Norm(unifiedColored.MatReadOnly, professionalColored.MatReadOnly, NormTypes.L1).Should().Be(0.0);
            unifiedResult.OutputData["PresentClasses"].Should().BeOfType<string[]>().Subject
                .Should().Equal(professionalResult.OutputData["PresentClasses"].Should().BeOfType<string[]>().Subject);
        }
        finally
        {
            DisposeImageOutputs(unifiedResult.OutputData);
            DisposeImageOutputs(professionalResult.OutputData);
        }
    }

    [Fact]
    public async Task IncompatibleExplicitTask_ShouldFailWithoutFallback()
    {
        var op = CreateDeepLearningOperator("ImageClassification", SegmentationModelPath());
        using var image = LoadSegmentationInput();

        var result = await _sut.ExecuteAsync(op, TestHelpers.CreateImageInputs(image));

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("exactly one");
    }

    [Fact]
    public void ClassificationValidation_ShouldIgnoreDetectionOnlyParameters()
    {
        var op = CreateDeepLearningOperator("ImageClassification", ClassificationModelPath());
        ReplaceParameter(op, "Confidence", 2.0);
        ReplaceParameter(op, "NmsIouThreshold", 2.0);
        ReplaceParameter(op, "OutputFormat", "InvalidDetectionFormat");

        var result = _sut.ValidateParameters(op);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validation_WithLegacyJsonBooleanUseGpu_ShouldPreserveHistoricalConversion()
    {
        var op = CreateDeepLearningOperator("ImageClassification", ClassificationModelPath());
        ReplaceParameter(op, "ExecutionProvider", "Auto");
        using var json = JsonDocument.Parse("false");
        ReplaceParameter(op, "UseGpu", json.RootElement.Clone());

        var result = _sut.ValidateParameters(op);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    private static Operator CreateDeepLearningOperator(
        string taskType,
        string modelPath,
        string modelId = "")
    {
        var op = new Operator("deep-learning", OperatorType.DeepLearning, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("TaskType", taskType, "enum"));
        op.AddParameter(TestHelpers.CreateParameter("ModelPath", modelPath, "file"));
        op.AddParameter(TestHelpers.CreateParameter("ModelId", modelId, "string"));
        op.AddParameter(TestHelpers.CreateParameter("ModelCatalogPath", ModelCatalogPath(), "file"));
        op.AddParameter(TestHelpers.CreateParameter("UseGpu", false, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ExecutionProvider", "CPU", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("InputSize", 640, "int"));
        op.AddParameter(TestHelpers.CreateParameter("Confidence", 0.5, "double"));
        op.AddParameter(TestHelpers.CreateParameter("NmsIouThreshold", 0.45, "double"));
        op.AddParameter(TestHelpers.CreateParameter("OutputFormat", "Auto", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("ModelVersion", "Auto", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("DetectionMode", "Defect", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("ClassificationInputSize", "Auto", "string"));
        op.AddParameter(TestHelpers.CreateParameter("ClassificationScoreMode", "Probabilities", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("TopK", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("SegmentationInputSize", "Auto", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumClasses", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ClassNames", "[\"red\",\"green\",\"blue\"]", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxClassMasks", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ScaleToUnitRange", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ChannelOrder", "RGB", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("Mean", "0,0,0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Std", "1,1,1", "string"));
        return op;
    }

    private static Operator CreateSegmentationOperator()
    {
        var op = new Operator("semantic", OperatorType.SemanticSegmentation, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("ModelPath", SegmentationModelPath(), "file"));
        op.AddParameter(TestHelpers.CreateParameter("InputSize", "2,2", "string"));
        op.AddParameter(TestHelpers.CreateParameter("NumClasses", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ClassNames", "[\"red\",\"green\",\"blue\"]", "string"));
        op.AddParameter(TestHelpers.CreateParameter("MaxClassMasks", 3, "int"));
        op.AddParameter(TestHelpers.CreateParameter("ExecutionProvider", "cpu", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("ScaleToUnitRange", true, "bool"));
        op.AddParameter(TestHelpers.CreateParameter("ChannelOrder", "RGB", "enum"));
        op.AddParameter(TestHelpers.CreateParameter("Mean", "0,0,0", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Std", "1,1,1", "string"));
        return op;
    }

    private static void ReplaceParameter(Operator op, string name, object value)
    {
        op.Parameters.Single(parameter => parameter.Name == name).SetValue(value);
    }

    private static ImageWrapper SolidImage(Scalar color)
    {
        using var mat = new Mat(8, 8, MatType.CV_8UC3, color);
        return new ImageWrapper(mat.Clone());
    }

    private static ImageWrapper LoadSegmentationInput()
    {
        return new ImageWrapper(Cv2.ImRead(
            ResolveRepoPath("ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/input.png"),
            ImreadModes.Color));
    }

    private static void DisposeImageOutputs(IDictionary<string, object>? output)
    {
        if (output == null)
        {
            return;
        }

        var wrappers = new HashSet<ImageWrapper>(ReferenceEqualityComparer.Instance);
        foreach (var wrapper in output.Values.OfType<ImageWrapper>())
        {
            wrappers.Add(wrapper);
        }

        if (output.TryGetValue("ClassMasks", out var masksValue) &&
            masksValue is IDictionary<string, object> masks)
        {
            foreach (var wrapper in masks.Values.OfType<ImageWrapper>())
            {
                wrappers.Add(wrapper);
            }
        }

        foreach (var wrapper in wrappers)
        {
            wrapper.Dispose();
        }
    }

    private static string ClassificationModelPath() => ResolveRepoPath(
        "ClearVision.Product/tests/TestData/model_test_suite/classification_color_mean_2x2/classification_color_mean_2x2.onnx");

    private static string SegmentationModelPath() => ResolveRepoPath(
        "ClearVision.Product/tests/TestData/model_test_suite/identity_2x2/identity_2x2.onnx");

    private static string ModelCatalogPath() => ResolveRepoPath("models/model_catalog.json");

    private static string ResolveRepoPath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               (!File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) ||
                !Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product"))))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return Path.GetFullPath(Path.Combine(directory!.FullName, relativePath));
    }
}
