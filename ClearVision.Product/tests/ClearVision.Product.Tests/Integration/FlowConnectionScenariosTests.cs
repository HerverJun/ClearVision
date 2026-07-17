// FlowConnectionScenariosTests.cs
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.General, TestPurpose.Integration, TestLane.Nightly, TestEvidenceType.IntegrationEvidence, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "product")]
public class FlowConnectionScenariosTests
{
    [Fact]
    public async Task ExecuteFlowAsync_ImageToCommentToResult_WithPortRemap_ShouldReturnImageBytes()
    {
        var service = CreateFlowService(
            new ImageAcquisitionOperator(Substitute.For<ILogger<ImageAcquisitionOperator>>(), Substitute.For<ICameraManager>()),
            new CommentOperator(Substitute.For<ILogger<CommentOperator>>()),
            new ResultOutputOperator(Substitute.For<ILogger<ResultOutputOperator>>()));

        var flow = new OperatorFlow();
        var acquisition = CreateOperator("acquisition", OperatorType.ImageAcquisition, outputPorts: [("Image", PortDataType.Image)]);
        var comment = CreateOperator("comment", OperatorType.Comment, inputPorts: [("Input", PortDataType.Any)], outputPorts: [("Output", PortDataType.Any)]);
        var result = CreateOperator("result", OperatorType.ResultOutput, inputPorts: [("Image", PortDataType.Image)], outputPorts: [("Output", PortDataType.Any)]);

        flow.AddOperator(acquisition);
        flow.AddOperator(comment);
        flow.AddOperator(result);
        flow.AddConnection(CreateConnection(acquisition, "Image", comment, "Input"));
        flow.AddConnection(CreateConnection(comment, "Output", result, "Image"));

        var output = await service.ExecuteFlowAsync(flow, new Dictionary<string, object>
        {
            { "Image", CreateTestImageBytes() }
        });

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("Image");
        output.OutputData!["Image"].Should().BeAssignableTo<byte[]>();
        ((byte[])output.OutputData["Image"]).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ImageToResultPort_ShouldPreserveImageWrapperLifetime()
    {
        var service = CreateFlowService(
            new ImageAcquisitionOperator(Substitute.For<ILogger<ImageAcquisitionOperator>>(), Substitute.For<ICameraManager>()),
            new ResultOutputOperator(Substitute.For<ILogger<ResultOutputOperator>>()));

        var flow = new OperatorFlow();
        var acquisition = CreateOperator("acquisition", OperatorType.ImageAcquisition, outputPorts: [("Image", PortDataType.Image)]);
        var result = CreateOperator(
            "result",
            OperatorType.ResultOutput,
            inputPorts: [("Result", PortDataType.Any)],
            outputPorts: [("Output", PortDataType.Any)]);

        flow.AddOperator(acquisition);
        flow.AddOperator(result);
        flow.AddConnection(CreateConnection(acquisition, "Image", result, "Result"));

        var output = await service.ExecuteFlowAsync(flow, new Dictionary<string, object>
        {
            { "Image", CreateTestImageBytes() }
        });

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("Result");
        output.OutputData!["Result"].Should().BeAssignableTo<byte[]>();
        ((byte[])output.OutputData["Result"]).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ConditionalTrueBranchToResult_ShouldRouteImageSuccessfully()
    {
        var service = CreateFlowService(
            new ImageAcquisitionOperator(Substitute.For<ILogger<ImageAcquisitionOperator>>(), Substitute.For<ICameraManager>()),
            new ConditionalBranchOperator(Substitute.For<ILogger<ConditionalBranchOperator>>()),
            new ResultOutputOperator(Substitute.For<ILogger<ResultOutputOperator>>()));

        var flow = new OperatorFlow();
        var acquisition = CreateOperator("acquisition", OperatorType.ImageAcquisition, outputPorts: [("Image", PortDataType.Image)]);
        var branch = CreateOperator(
            "branch",
            OperatorType.ConditionalBranch,
            inputPorts: [("Value", PortDataType.Any)],
            outputPorts: [("True", PortDataType.Any), ("False", PortDataType.Any)]);
        var result = CreateOperator("result", OperatorType.ResultOutput, inputPorts: [("Image", PortDataType.Image)], outputPorts: [("Output", PortDataType.Any)]);

        branch.AddParameter(new Parameter(Guid.NewGuid(), "Condition", "Condition", string.Empty, "string", "Contains", null, null, true));
        branch.AddParameter(new Parameter(Guid.NewGuid(), "CompareValue", "CompareValue", string.Empty, "string", "ImageWrapper", null, null, true));

        flow.AddOperator(acquisition);
        flow.AddOperator(branch);
        flow.AddOperator(result);
        flow.AddConnection(CreateConnection(acquisition, "Image", branch, "Value"));
        flow.AddConnection(CreateConnection(branch, "True", result, "Image"));

        var output = await service.ExecuteFlowAsync(flow, new Dictionary<string, object>
        {
            { "Image", CreateTestImageBytes() }
        });

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("Image");
        output.OutputData!["Image"].Should().BeAssignableTo<byte[]>();
        ((byte[])output.OutputData["Image"]).Length.Should().BeGreaterThan(0);
        output.OutputData.Should().ContainKey("ConditionResult");
        output.OutputData["ConditionResult"].Should().Be(true);
    }

    [Fact]
    public async Task ExecuteFlowAsync_DualInputSameConsumer_ShouldNotOverRetainSourceImage()
    {
        var sourceExecutor = new TestImageSourceOperator(NullLogger<TestImageSourceOperator>.Instance);
        var consumerExecutor = new TestDualInputConsumerOperator(NullLogger<TestDualInputConsumerOperator>.Instance);
        var service = CreateFlowService(sourceExecutor, consumerExecutor);

        var flow = new OperatorFlow();
        var source = CreateOperator("source", OperatorType.ImageAcquisition, outputPorts: [("Image", PortDataType.Image)]);
        var consumer = CreateOperator(
            "consumer",
            OperatorType.ImageDiff,
            inputPorts: [("InputA", PortDataType.Image), ("InputB", PortDataType.Image)],
            outputPorts: [("Output", PortDataType.Any)]);

        flow.AddOperator(source);
        flow.AddOperator(consumer);
        flow.AddConnection(CreateConnection(source, "Image", consumer, "InputA"));
        flow.AddConnection(CreateConnection(source, "Image", consumer, "InputB"));

        var output = await service.ExecuteFlowAsync(flow);

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("SameRef");
        output.OutputData!["SameRef"].Should().Be(true);
        sourceExecutor.LastOutputImage.Should().NotBeNull();
        sourceExecutor.LastOutputImage!.RefCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenImageConsumerFails_ShouldKeepOriginalFailureMessage()
    {
        var sourceExecutor = new TestImageSourceOperator(NullLogger<TestImageSourceOperator>.Instance);
        var consumerExecutor = new TestFailingImageConsumerOperator(NullLogger<TestFailingImageConsumerOperator>.Instance);
        var service = CreateFlowService(sourceExecutor, consumerExecutor);

        var flow = new OperatorFlow();
        var source = CreateOperator("source", OperatorType.ImageAcquisition, outputPorts: [("Image", PortDataType.Image)]);
        var consumer = CreateOperator(
            "consumer",
            OperatorType.ImageDiff,
            inputPorts: [("Image", PortDataType.Image)],
            outputPorts: [("Output", PortDataType.Any)]);

        flow.AddOperator(source);
        flow.AddOperator(consumer);
        flow.AddConnection(CreateConnection(source, "Image", consumer, "Image"));

        var output = await service.ExecuteFlowAsync(flow);

        output.IsSuccess.Should().BeFalse();
        output.ErrorMessage.Should().Contain("synthetic image consumer failure");
        output.ErrorMessage.Should().NotContain("disposed", because: "flow output normalization must not mask the operator failure");
        sourceExecutor.LastOutputImage.Should().NotBeNull();
        sourceExecutor.LastOutputImage!.RefCount.Should().Be(0);

        var debugOutput = await service.ExecuteFlowDebugAsync(flow, new DebugOptions { EnableIntermediateCache = false });

        debugOutput.IsSuccess.Should().BeFalse();
        debugOutput.ErrorMessage.Should().Contain("synthetic image consumer failure");
        debugOutput.ErrorMessage.Should().NotContain("disposed", because: "preview/debug output normalization must not mask the operator failure");
    }

    [Fact]
    public async Task ExecuteFlowAsync_NonImageConnections_ShouldNotImplicitlyForwardImageWrapper()
    {
        var sourceExecutor = new TestYoloLikeSourceOperator(NullLogger<TestYoloLikeSourceOperator>.Instance);
        var service = CreateFlowService(
            sourceExecutor,
            new ConditionalBranchOperator(Substitute.For<ILogger<ConditionalBranchOperator>>()),
            new ResultOutputOperator(Substitute.For<ILogger<ResultOutputOperator>>()));

        var flow = new OperatorFlow();
        var source = CreateOperator(
            "yolo",
            OperatorType.ImageAcquisition,
            outputPorts:
            [
                ("Image", PortDataType.Image),
                ("DefectCount", PortDataType.Integer),
                ("Defects", PortDataType.DetectionList)
            ]);
        var branch = CreateOperator(
            "branch",
            OperatorType.ConditionalBranch,
            inputPorts: [("Value", PortDataType.Any)],
            outputPorts: [("True", PortDataType.Any), ("False", PortDataType.Any)]);
        var result = CreateOperator(
            "result",
            OperatorType.ResultOutput,
            inputPorts: [("Result", PortDataType.Any), ("Data", PortDataType.Any)],
            outputPorts: [("Output", PortDataType.Any)]);

        branch.AddParameter(new Parameter(Guid.NewGuid(), "Condition", "Condition", string.Empty, "string", "GreaterThan", null, null, true));
        branch.AddParameter(new Parameter(Guid.NewGuid(), "CompareValue", "CompareValue", string.Empty, "string", "0", null, null, true));

        flow.AddOperator(source);
        flow.AddOperator(branch);
        flow.AddOperator(result);

        // Only non-image ports are explicitly connected. Image should not be implicitly propagated.
        flow.AddConnection(CreateConnection(source, "DefectCount", branch, "Value"));
        flow.AddConnection(CreateConnection(source, "Defects", result, "Result"));
        flow.AddConnection(CreateConnection(source, "DefectCount", result, "Data"));

        var output = await service.ExecuteFlowAsync(flow);

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("Result");
        output.OutputData!["Result"].Should().NotBeNull();
        output.OutputData.Should().ContainKey("Data");
        output.OutputData["Data"].Should().Be(1);
        output.OutputData.Should().NotContainKey("Image");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ImageAcquisitionToMeasurement_ShouldRetainInspectionInputSnapshotWithoutPollutingOutputData()
    {
        var sourceExecutor = new TestImageSourceOperator(NullLogger<TestImageSourceOperator>.Instance);
        var measurementExecutor = new TestInitialImageConsumerOperator(NullLogger<TestInitialImageConsumerOperator>.Instance);
        var service = CreateFlowService(sourceExecutor, measurementExecutor);

        var flow = new OperatorFlow();
        var source = CreateOperator(
            "source",
            OperatorType.ImageAcquisition,
            outputPorts: [("Image", PortDataType.Image)]);
        var measurement = CreateOperator(
            "measurement",
            OperatorType.ImageDiff,
            inputPorts: [("Image", PortDataType.Image)],
            outputPorts: [("Width", PortDataType.Integer), ("Height", PortDataType.Integer)]);

        flow.AddOperator(source);
        flow.AddOperator(measurement);
        flow.AddConnection(CreateConnection(source, "Image", measurement, "Image"));

        var output = await service.ExecuteFlowAsync(flow);

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        output.OutputData.Should().ContainKey("Width");
        output.OutputData.Should().ContainKey("Height");
        output.OutputData.Should().NotContainKey("Image");
        output.InputImage.Should().NotBeNullOrEmpty();
        sourceExecutor.LastOutputImage.Should().NotBeNull();
        sourceExecutor.LastOutputImage!.RefCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteFlowAsync_MultipleRootConsumers_SharedInitialImageWrapper_ShouldNotDoubleRelease(bool enableParallel)
    {
        using var inputImage = new ImageWrapper(new Mat(12, 12, MatType.CV_8UC3, Scalar.All(9)));
        var consumerExecutor = new TestInitialImageConsumerOperator(NullLogger<TestInitialImageConsumerOperator>.Instance);
        var service = CreateFlowService(consumerExecutor);

        var flow = new OperatorFlow();
        var consumerA = CreateOperator("consumer-a", OperatorType.ImageDiff, inputPorts: [("Image", PortDataType.Image)]);
        var consumerB = CreateOperator("consumer-b", OperatorType.ImageDiff, inputPorts: [("Image", PortDataType.Image)]);

        flow.AddOperator(consumerA);
        flow.AddOperator(consumerB);

        var output = await service.ExecuteFlowAsync(
            flow,
            new Dictionary<string, object>
            {
                ["Image"] = inputImage
            },
            enableParallel: enableParallel);

        output.IsSuccess.Should().BeTrue(output.ErrorMessage);
        consumerExecutor.ExecutionCount.Should().Be(2);
        inputImage.RefCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenRootConsumerFails_ShouldReleaseReservedInitialImageRefs()
    {
        using var inputImage = new ImageWrapper(new Mat(12, 12, MatType.CV_8UC3, Scalar.All(9)));
        var failingExecutor = new TestFailingCommentImageConsumerOperator(NullLogger<TestFailingCommentImageConsumerOperator>.Instance);
        var skippedExecutor = new TestInitialImageConsumerOperator(NullLogger<TestInitialImageConsumerOperator>.Instance);
        var service = CreateFlowService(failingExecutor, skippedExecutor);

        var flow = new OperatorFlow();
        var failingRoot = CreateOperator("failing-root", OperatorType.Comment, inputPorts: [("Image", PortDataType.Image)]);
        var skippedRoot = CreateOperator("skipped-root", OperatorType.ImageDiff, inputPorts: [("Image", PortDataType.Image)]);

        flow.AddOperator(failingRoot);
        flow.AddOperator(skippedRoot);

        var output = await service.ExecuteFlowAsync(
            flow,
            new Dictionary<string, object>
            {
                ["Image"] = inputImage
            },
            enableParallel: false);

        output.IsSuccess.Should().BeFalse();
        output.ErrorMessage.Should().Contain("synthetic root consumer failure");
        skippedExecutor.ExecutionCount.Should().Be(0);
        inputImage.RefCount.Should().Be(0);
    }

    private static IFlowExecutionEngine CreateFlowService(params IOperatorExecutor[] executors)
    {
        return new FlowExecutionService(
            executors,
            Substitute.For<ILogger<FlowExecutionService>>(),
            Substitute.For<IVariableContext>());
    }

    private static Operator CreateOperator(
        string name,
        OperatorType type,
        IEnumerable<(string Name, PortDataType Type)>? inputPorts = null,
        IEnumerable<(string Name, PortDataType Type)>? outputPorts = null)
    {
        var op = new Operator(name, type, 0, 0);

        if (inputPorts != null)
        {
            foreach (var (portName, portType) in inputPorts)
            {
                op.AddInputPort(portName, portType, isRequired: false);
            }
        }

        if (outputPorts != null)
        {
            foreach (var (portName, portType) in outputPorts)
            {
                op.AddOutputPort(portName, portType);
            }
        }

        return op;
    }

    private static OperatorConnection CreateConnection(Operator source, string sourcePortName, Operator target, string targetPortName)
    {
        var sourcePort = source.OutputPorts.Single(p => p.Name == sourcePortName);
        var targetPort = target.InputPorts.Single(p => p.Name == targetPortName);
        return new OperatorConnection(source.Id, sourcePort.Id, target.Id, targetPort.Id);
    }

    private static byte[] CreateTestImageBytes()
    {
        const string base64Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        return Convert.FromBase64String(base64Png);
    }

    private sealed class TestImageSourceOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.ImageAcquisition;
        public ImageWrapper? LastOutputImage { get; private set; }

        public TestImageSourceOperator(ILogger<TestImageSourceOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            var mat = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(255));
            var output = CreateImageOutput(mat);
            LastOutputImage = (ImageWrapper)output["Image"];
            return Task.FromResult(OperatorExecutionOutput.Success(output));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TestDualInputConsumerOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.ImageDiff;

        public TestDualInputConsumerOperator(ILogger<TestDualInputConsumerOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            if (!TryGetInputImage(inputs, "InputA", out var inputA) || inputA == null)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("missing InputA"));
            }

            if (!TryGetInputImage(inputs, "InputB", out var inputB) || inputB == null)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("missing InputB"));
            }

            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                { "SameRef", ReferenceEquals(inputA, inputB) }
            }));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TestFailingImageConsumerOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.ImageDiff;

        public TestFailingImageConsumerOperator(ILogger<TestFailingImageConsumerOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            if (!TryGetInputImage(inputs, "Image", out _))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("missing image"));
            }

            return Task.FromResult(OperatorExecutionOutput.Failure("synthetic image consumer failure"));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TestFailingCommentImageConsumerOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.Comment;

        public TestFailingCommentImageConsumerOperator(ILogger<TestFailingCommentImageConsumerOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            if (!TryGetInputImage(inputs, "Image", out _))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("missing image"));
            }

            return Task.FromResult(OperatorExecutionOutput.Failure("synthetic root consumer failure"));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TestYoloLikeSourceOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.ImageAcquisition;

        public TestYoloLikeSourceOperator(ILogger<TestYoloLikeSourceOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            var mat = new Mat(16, 16, MatType.CV_8UC3, Scalar.All(128));
            var output = CreateImageOutput(mat, new Dictionary<string, object>
            {
                { "DefectCount", 1 },
                { "Defects", new List<string> { "remote" } }
            });

            return Task.FromResult(OperatorExecutionOutput.Success(output));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }

    private sealed class TestInitialImageConsumerOperator : OperatorBase
    {
        public override OperatorType OperatorType => OperatorType.ImageDiff;

        public int ExecutionCount { get; private set; }

        public TestInitialImageConsumerOperator(ILogger<TestInitialImageConsumerOperator> logger) : base(logger)
        {
        }

        protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
            Operator @operator,
            Dictionary<string, object>? inputs,
            CancellationToken cancellationToken)
        {
            if (!TryGetInputImage(inputs, "Image", out var image) || image == null)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("missing image"));
            }

            ExecutionCount++;
            return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Width"] = image.Width,
                ["Height"] = image.Height
            }));
        }

        public override ValidationResult ValidateParameters(Operator @operator)
        {
            return ValidationResult.Valid();
        }
    }
}
