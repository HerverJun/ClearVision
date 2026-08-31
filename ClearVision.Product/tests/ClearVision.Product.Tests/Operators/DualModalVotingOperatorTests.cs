using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;
using VisionDetection = ClearVision.Product.Core.ValueObjects.DetectionResult;
using VisionDetectionList = ClearVision.Product.Core.ValueObjects.DetectionList;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product")]
public class DualModalVotingOperatorTests
{
    private readonly ILogger<DualModalVotingOperator> _loggerMock;
    private readonly DualModalVotingOperator _operator;
    private readonly Operator _operatorEntity;

    public DualModalVotingOperatorTests()
    {
        _loggerMock = Substitute.For<ILogger<DualModalVotingOperator>>();
        _operator = new DualModalVotingOperator(_loggerMock);
        _operatorEntity = new Operator("DualModalVoting", OperatorType.DualModalVoting, 0, 0);
    }

    [Fact]
    public async Task Execute_WithDetectionResultObjects_ShouldVoteUsingOkProbability()
    {
        var dlResult = DetectionResult.Success(true, 0.9);
        var traditionalResult = DetectionResult.Success(false, 0.4);

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlResult },
            { "TraditionalResult", traditionalResult }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(true);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.78, 0.001);
        result.OutputData["JudgmentValue"].Should().Be("1");
    }

    [Fact]
    public async Task Execute_WithDictionaryInputs_ShouldExtractAndVote()
    {
        var dlDict = new Dictionary<string, object>
        {
            { "IsOk", true },
            { "Confidence", 0.8 }
        };

        var traditionalDict = new Dictionary<string, object>
        {
            { "IsOk", true },
            { "Confidence", 0.7 }
        };

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlDict },
            { "TraditionalResult", traditionalDict }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(true);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.76, 0.001);
    }

    [Fact]
    public async Task Execute_WithDeepLearningDefectCountFormat_ShouldInferLabelAndProbability()
    {
        var dlDict = new Dictionary<string, object>
        {
            { "DefectCount", 0 },
            { "Defects", new List<object>() }
        };

        var traditionalDict = new Dictionary<string, object>
        {
            {
                "DefectCount",
                1
            },
            {
                "Defects",
                new List<object>
                {
                    new Dictionary<string, object> { { "Confidence", 0.95 } }
                }
            }
        };

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlDict },
            { "TraditionalResult", traditionalDict }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(true);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.62, 0.001);
    }

    [Fact]
    public async Task Execute_WithRealDetectionListOutput_ShouldUseCanonicalAdapter()
    {
        _operatorEntity.AddParameter(TestHelpers.CreateParameter(
            "VotingStrategy",
            "PrioritizeDeepLearning",
            "string"));

        var deepLearningOutput = new VisionDetectionList(
        [
            new VisionDetection("wire-swap", 0.93f, 12f, 8f, 16f, 10f)
        ]);
        var traditionalOutput = new VisionDetectionList();

        var result = await _operator.ExecuteAsync(
            _operatorEntity,
            new Dictionary<string, object>
            {
                ["DLResult"] = deepLearningOutput,
                ["TraditionalResult"] = traditionalOutput
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(false);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.93, 0.001);
        result.OutputData["JudgmentValue"].Should().Be("0");
    }

    [Fact]
    public async Task Execute_WithActualDeepLearningOperatorOutput_ShouldVoteUsingDirectDetectionListContract()
    {
        var fixtureDirectory = Path.Combine(
            Path.GetTempPath(),
            "ClearVision.DualModalVoting.Tests",
            Guid.NewGuid().ToString("N"));
        var modelPath = Path.Combine(fixtureDirectory, "constant-detection.onnx");
        ImageWrapper? outputImage = null;
        ImageWrapper? originalImage = null;
        try
        {
            Directory.CreateDirectory(fixtureDirectory);
            await File.WriteAllBytesAsync(modelPath, Convert.FromBase64String(ConstantDetectionModelBase64));

            var deepLearningOperator = new DeepLearningOperator(
                Substitute.For<ILogger<DeepLearningOperator>>());
            var deepLearningDefinition = new Operator("deep-learning", OperatorType.DeepLearning, 0, 0);
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("TaskType", "ObjectDetection", "enum"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("ModelPath", modelPath, "file"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("ExecutionProvider", "CPU", "enum"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("InputSize", 640, "int"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("Confidence", 0.5d, "double"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("NmsIouThreshold", 0.45d, "double"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("OutputFormat", "EndToEndNms", "enum"));
            deepLearningDefinition.AddParameter(TestHelpers.CreateParameter("DetectionMode", "Defect", "enum"));

            using var source = new ImageWrapper(new Mat(8, 8, MatType.CV_8UC3, Scalar.Black));
            var deepLearningResult = await deepLearningOperator.ExecuteAsync(
                deepLearningDefinition,
                TestHelpers.CreateImageInputs(source),
                CancellationToken.None);

            deepLearningResult.IsSuccess.Should().BeTrue(deepLearningResult.ErrorMessage);
            deepLearningResult.OutputData.Should().NotBeNull();
            deepLearningResult.OutputData!["Defects"].Should().BeOfType<VisionDetectionList>();
            ((VisionDetectionList)deepLearningResult.OutputData["Defects"]).Detections
                .Should().ContainSingle(detection => detection.Label == "wire-swap");
            outputImage = deepLearningResult.OutputData.GetValueOrDefault("Image") as ImageWrapper;
            originalImage = deepLearningResult.OutputData.GetValueOrDefault("OriginalImage") as ImageWrapper;

            _operatorEntity.AddParameter(TestHelpers.CreateParameter(
                "VotingStrategy",
                "PrioritizeDeepLearning",
                "string"));
            var vote = await _operator.ExecuteAsync(
                _operatorEntity,
                new Dictionary<string, object>
                {
                    ["DLResult"] = deepLearningResult.OutputData["Defects"],
                    ["TraditionalResult"] = new VisionDetectionList()
                },
                CancellationToken.None);

            vote.IsSuccess.Should().BeTrue(vote.ErrorMessage);
            vote.OutputData!["IsOk"].Should().Be(false);
            ((double)vote.OutputData["Confidence"]).Should().BeApproximately(0.95d, 0.001d);
            vote.OutputData["JudgmentValue"].Should().Be("0");
        }
        finally
        {
            outputImage?.Dispose();
            if (originalImage != null && !ReferenceEquals(originalImage, outputImage))
            {
                originalImage.Dispose();
            }

            if (Directory.Exists(fixtureDirectory))
            {
                Directory.Delete(fixtureDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WithDefectCountWithoutConfidence_ShouldStayConservativeNg()
    {
        var dlDict = new Dictionary<string, object>
        {
            { "DefectCount", 1 },
            { "Defects", new List<object>() }
        };

        var traditionalDict = new Dictionary<string, object>
        {
            { "DefectCount", 1 }
        };

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlDict },
            { "TraditionalResult", traditionalDict }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(false);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task Execute_WithWeightedAverage_ShouldNotAverageHighConfidenceNgIntoOk()
    {
        var dlResult = DetectionResult.Success(true, 0.51);
        var traditionalResult = DetectionResult.Success(false, 0.95);

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlResult },
            { "TraditionalResult", traditionalResult }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(false);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.674, 0.001);
        result.OutputData["JudgmentValue"].Should().Be("0");
    }

    [Fact]
    public async Task Execute_WithWeightedAverage_ShouldReturnConfidenceForFinalNgDecision()
    {
        var dlResult = DetectionResult.Success(true, 0.1);
        var traditionalResult = DetectionResult.Success(false, 0.9);

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlResult },
            { "TraditionalResult", traditionalResult }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(false);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.9, 0.001);
    }

    [Fact]
    public async Task Execute_WithUnanimousStrategy_BothMustBeOk()
    {
        _operatorEntity.AddParameter(TestHelpers.CreateParameter(
            "VotingStrategy",
            "Unanimous",
            "string"));

        var dlResult = DetectionResult.Success(true, 0.9);
        var traditionalResult = DetectionResult.Success(false, 0.4);

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", dlResult },
            { "TraditionalResult", traditionalResult }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["IsOk"].Should().Be(false);
        result.OutputData["JudgmentValue"].Should().Be("0");
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.4, 0.001);
    }

    [Fact]
    public async Task Execute_WithLowercaseStrategy_ShouldMatchValidation()
    {
        _operatorEntity.AddParameter(TestHelpers.CreateParameter("VotingStrategy", "weightedaverage", "string"));

        var validation = _operator.ValidateParameters(_operatorEntity);
        validation.IsValid.Should().BeTrue();

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", DetectionResult.Success(true, 0.9) },
            { "TraditionalResult", DetectionResult.Success(false, 0.4) }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.OutputData!["IsOk"].Should().Be(true);
        ((double)result.OutputData["Confidence"]).Should().BeApproximately(0.78, 0.001);
    }

    [Fact]
    public async Task Execute_WithWeightedAverageAndZeroWeights_ShouldFail()
    {
        _operatorEntity.AddParameter(TestHelpers.CreateParameter("VotingStrategy", "WeightedAverage", "string"));
        _operatorEntity.AddParameter(TestHelpers.CreateParameter("DLWeight", 0.0, "double"));
        _operatorEntity.AddParameter(TestHelpers.CreateParameter("TraditionalWeight", 0.0, "double"));

        var inputs = new Dictionary<string, object>
        {
            { "DLResult", DetectionResult.Success(true, 0.9) },
            { "TraditionalResult", DetectionResult.Success(false, 0.1) }
        };

        var result = await _operator.ExecuteAsync(_operatorEntity, inputs, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("DLWeight + TraditionalWeight > 0");
    }

    // ONNX IR v7 / opset 11: one constant end-to-end NMS row [x1,y1,x2,y2,score,class]
    // plus empty rows. The model metadata supplies the canonical wire-swap label.
    private const string ConstantDetectionModelBase64 =
        "CAcSJkNsZWFyVmlzaW9uIGRldGVybWluaXN0aWMgdGVzdCBmaXh0dXJlOqwECsMDEgpkZXRlY3Rpb25zIghDb25zdGFudCqqAwoFdmFsdWUqnQMIAQgGCBAQAUIQZGV0ZWN0aW9uc192YWx1ZUqAA83MzD0AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADNzMw9AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAZmZmPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGZmZj8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAzM3M/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKABBBIeY2xlYXJ2aXNpb25fY29uc3RhbnRfZGV0ZWN0aW9uWiIKBmltYWdlcxIYChYIARISCgIIAQoCCAMKAwiABQoDCIAFYiAKCmRldGVjdGlvbnMSEgoQCAESDAoCCAEKAggGCgIIEEIECgAQC3IWCgVuYW1lcxINWyJ3aXJlLXN3YXAiXQ==";
}
