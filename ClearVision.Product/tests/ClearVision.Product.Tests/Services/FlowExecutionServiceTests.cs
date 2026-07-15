// FlowExecutionServiceTests.cs
// FlowExecutionServiceTests测试
// 作者：蘅芜君

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Calibration;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.Operators;
using ClearVision.Product.Tests.TestData;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public class FlowExecutionServiceTests
{
    private readonly FlowExecutionService _sut;
    private readonly IOperatorExecutor _executor;
    private readonly ILogger<FlowExecutionService> _logger;
    private readonly IVariableContext _variableContext;

    public FlowExecutionServiceTests()
    {
        _executor = Substitute.For<IOperatorExecutor>();
        // Fix: Configure mock property BEFORE initializing service so dictionary key is correct
        _executor.OperatorType.Returns(OperatorType.Thresholding);

        _logger = Substitute.For<ILogger<FlowExecutionService>>();
        _variableContext = Substitute.For<IVariableContext>();

        var executors = new List<IOperatorExecutor> { _executor };
        _sut = new FlowExecutionService(executors, _logger, _variableContext);
    }

    private static (FlowExecutionService Sut, IOperatorExecutor Executor) CreateSingleExecutorService(OperatorType operatorType)
    {
        var executor = Substitute.For<IOperatorExecutor>();
        executor.OperatorType.Returns(operatorType);
        var logger = Substitute.For<ILogger<FlowExecutionService>>();
        var variableContext = Substitute.For<IVariableContext>();

        return (new FlowExecutionService(new List<IOperatorExecutor> { executor }, logger, variableContext), executor);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldRespectCancellation()
    {
        // Arrange
        var flow = new OperatorFlow("TestFlow");
        var op = new Operator(Guid.NewGuid(), "LongRunningOp", OperatorType.Thresholding, 0, 0);
        flow.AddOperator(op);
        var executorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Note: Executor properties are configured in constructor
        _executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var ct = x.Arg<CancellationToken>();
                executorStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return OperatorExecutionOutput.Success(new Dictionary<string, object>(), 500);
            });

        var cts = new CancellationTokenSource();

        // Act
        // Start the task but don't await it immediately
        var task = _sut.ExecuteFlowAsync(flow, cancellationToken: cts.Token);

        await executorStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        // Await the task, expecting it to complete (possibly with failure)
        var result = await task;

        // Assert
        result.IsSuccess.Should().BeFalse("Flow execution should be cancelled");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldForwardSpatialContextSidecarWithConnectedImageOutput()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.RoiManager);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        SpatialContextV1? spatialContext = null;
        var imagePayload = new byte[] { 1, 2, 3 };
        Dictionary<string, object>? targetInputs = null;

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Name.Equals("Source", StringComparison.Ordinal))
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = imagePayload,
                            [RoiManagerOperator.SpatialContextOutputKey] = spatialContext!
                        },
                        executionTimeMs: 5));
                }

                var inputs = callInfo.ArgAt<Dictionary<string, object>?>(1);
                targetInputs = inputs == null
                    ? null
                    : new Dictionary<string, object>(inputs, StringComparer.OrdinalIgnoreCase);

                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object>(),
                    executionTimeMs: 5));
            });

        var flow = new OperatorFlow("SpatialSidecarFlow");
        var source = new Operator("Source", OperatorType.RoiManager, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        spatialContext = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(
            source.Id,
            source.OutputPorts.Single(port => port.Name == "Image").Id,
            "Image"));
        var target = new Operator("Target", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(
            source.Id,
            source.OutputPorts.Single(port => port.Name == "Image").Id,
            target.Id,
            target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(spatialContext);
        targetInputs["Image"].Should().BeSameAs(imagePayload);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldSelectSpatialContextByConnectedSourcePortBinding()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.RoiManager);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("PortAwareSpatialSidecarFlow");
        var source = new Operator("Roi", OperatorType.RoiManager, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort("Mask", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var maskPort = source.OutputPorts.Single(port => port.Name == "Mask");
        var targetImagePort = target.InputPorts.Single(port => port.Name == "Image");

        var imageContext = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));
        var maskContext = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(source.Id, maskPort.Id, "Mask"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            ["Mask"] = new byte[] { 2 },
                            [RoiManagerOperator.SpatialContextOutputKey] = imageContext,
                            [RoiManagerOperator.MaskSpatialContextOutputKey] = maskContext
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!, StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, maskPort.Id, target.Id, targetImagePort.Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs!["Image"].Should().BeEquivalentTo(new byte[] { 2 });
        targetInputs[RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(maskContext);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldNotFallbackToMismatchedSpatialContextBinding()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.RoiManager);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("MismatchedSpatialSidecarFlow");
        var source = new Operator("Source", OperatorType.RoiManager, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort("OtherImage", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var otherPort = source.OutputPorts.Single(port => port.Name == "OtherImage");
        var targetImagePort = target.InputPorts.Single(port => port.Name == "Image");
        var mismatchedContext = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(source.Id, otherPort.Id, "OtherImage"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = mismatchedContext
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!, StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, targetImagePort.Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_BINDING_INVALID");
        result.OperatorResults.Should().ContainSingle(item =>
            item.OperatorId == target.Id &&
            item.IsSuccess == false &&
            item.ErrorMessage!.Contains("SPATIAL_CONTEXT_BINDING_INVALID", StringComparison.Ordinal));
        targetInputs.Should().BeNull("the target executor must not run with an invalid SpatialContext");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldFailClosedForDuplicateSpatialContextBindingMatches()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.RoiManager);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("DuplicateSpatialSidecarFlow");
        var source = new Operator("Source", OperatorType.RoiManager, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var targetImagePort = target.InputPorts.Single(port => port.Name == "Image");
        var first = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));
        var duplicate = SpatialContextV1.DefaultImageFull(SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = first,
                            ["AnotherSpatialContext"] = duplicate
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!, StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, targetImagePort.Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_BINDING_AMBIGUOUS");
        result.OperatorResults.Should().ContainSingle(item =>
            item.OperatorId == target.Id &&
            item.IsSuccess == false &&
            item.ErrorMessage!.Contains("SPATIAL_CONTEXT_BINDING_AMBIGUOUS", StringComparison.Ordinal));
        targetInputs.Should().BeNull("the target executor must not run with an ambiguous SpatialContext");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldFailClosedForMalformedSpatialContextSidecar()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.RoiManager);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("MalformedSpatialSidecarFlow");
        var source = new Operator("Source", OperatorType.RoiManager, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var targetImagePort = target.InputPorts.Single(port => port.Name == "Image");

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = "{not valid json"
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!, StringComparer.OrdinalIgnoreCase);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, targetImagePort.Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_MALFORMED");
        targetInputs.Should().BeNull("the target executor must not run with malformed SpatialContext");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldForwardSpatialContextSidecarToGenericImageTarget()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        var imagePayload = new byte[] { 1, 2, 3 };
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("GenericSpatialTargetFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("GenericTarget", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var context = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = imagePayload,
                            [RoiManagerOperator.SpatialContextOutputKey] = context
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(context);
        targetInputs["Image"].Should().BeSameAs(imagePayload);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldWriteSpatialContextToTargetPortScopedKey()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("BackgroundSpatialTargetFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("GenericTarget", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Background", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var context = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 7 },
                            [RoiManagerOperator.SpatialContextOutputKey] = context
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs!["BackgroundSpatialContext"].Should().BeSameAs(context);
        targetInputs.Should().NotContainKey(RoiManagerOperator.ImageSpatialContextInputKey);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldPropagateSpatialContextAcrossPointListConnection()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.PixelToWorldTransform);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("PointListSpatialTargetFlow");
        var source = new Operator("PixelToWorldA", OperatorType.PixelToWorldTransform, 0, 0);
        source.AddOutputPort("TransformedPoints", PortDataType.PointList);
        source.AddOutputPort("TransformedPointsSpatialContext", PortDataType.Any);
        var target = new Operator("PixelToWorldB", OperatorType.PixelToWorldTransform, 0, 0);
        target.AddInputPort("Points", PortDataType.PointList);
        var pointsPort = source.OutputPorts.Single(port => port.Name == "TransformedPoints");
        var context = new SpatialContextV1(
            FrameRefV1.World2D(),
            [SpatialTransform2DV1.Identity(FrameRefV1.World2D())],
            SpatialContextBindingV1.ForFlowOutput(source.Id, pointsPort.Id, "TransformedPoints"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["TransformedPoints"] = new List<Point3d> { new(1, 2, 0) },
                            ["TransformedPointsSpatialContext"] = context
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, pointsPort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs!["PointsSpatialContext"].Should().BeSameAs(context);
        targetInputs["Points"].Should().BeAssignableTo<List<Point3d>>();
        targetInputs.Should().NotContainKey(RoiManagerOperator.ImageSpatialContextInputKey);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldForwardDistinctSpatialContextsToMultipleImageInputs()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("MultiImageSpatialTargetFlow");
        var foreground = new Operator("Foreground", OperatorType.Thresholding, 0, 0);
        foreground.AddOutputPort("Image", PortDataType.Image);
        foreground.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var background = new Operator("Background", OperatorType.Thresholding, 0, 0);
        background.AddOutputPort("Image", PortDataType.Image);
        background.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Compositor", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        target.AddInputPort("Background", PortDataType.Image);

        var foregroundPort = foreground.OutputPorts.Single(port => port.Name == "Image");
        var backgroundPort = background.OutputPorts.Single(port => port.Name == "Image");
        var foregroundContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(foreground.Id, foregroundPort.Id, "Image"));
        var backgroundContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(background.Id, backgroundPort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == foreground.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = foregroundContext
                        },
                        executionTimeMs: 5));
                }

                if (op.Id == background.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 2 },
                            [RoiManagerOperator.SpatialContextOutputKey] = backgroundContext
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(foreground);
        flow.AddOperator(background);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(
            foreground.Id,
            foregroundPort.Id,
            target.Id,
            target.InputPorts.Single(port => port.Name == "Image").Id));
        flow.AddConnection(new OperatorConnection(
            background.Id,
            backgroundPort.Id,
            target.Id,
            target.InputPorts.Single(port => port.Name == "Background").Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(foregroundContext);
        targetInputs["BackgroundSpatialContext"].Should().BeSameAs(backgroundContext);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldKeepLegacyImageConnectionSuccessfulWhenSidecarAbsent()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("LegacyImageConnectionFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        var target = new Operator("Target", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single();

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object> { ["Image"] = new byte[] { 9 } },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs!.Should().ContainKey("Image");
        targetInputs.Should().NotContainKey(RoiManagerOperator.ImageSpatialContextInputKey);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldIgnoreMalformedMaskSidecarWhenConnectingImageOutput()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("ImageIgnoresMalformedMaskSpatialFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort("Mask", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        source.AddOutputPort(RoiManagerOperator.MaskSpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("ImageTarget", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var context = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            ["Mask"] = new byte[] { 2 },
                            [RoiManagerOperator.SpatialContextOutputKey] = context,
                            [RoiManagerOperator.MaskSpatialContextOutputKey] = "{not valid json"
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(context);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldIgnoreMalformedImageSidecarWhenConnectingMaskOutput()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("MaskIgnoresMalformedImageSpatialFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort("Mask", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        source.AddOutputPort(RoiManagerOperator.MaskSpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("MaskTarget", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var maskPort = source.OutputPorts.Single(port => port.Name == "Mask");
        var context = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, maskPort.Id, "Mask"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            ["Mask"] = new byte[] { 2 },
                            [RoiManagerOperator.SpatialContextOutputKey] = "{not valid json",
                            [RoiManagerOperator.MaskSpatialContextOutputKey] = context
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, maskPort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(context);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldFailClosedWhenSidecarKeyAndBindingPointToDifferentPorts()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var flow = new OperatorFlow("SpatialKeyBindingMismatchFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort("Mask", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.MaskSpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var context = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, imagePort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            ["Mask"] = new byte[] { 2 },
                            [RoiManagerOperator.MaskSpatialContextOutputKey] = context
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_BINDING_INVALID");
        targetInputs.Should().BeNull("the target executor must not run with key/binding mismatch");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldProduceSameSpatialInputsWhenConnectionOrderIsReversed()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        Dictionary<string, object>? targetInputs = null;

        var foreground = new Operator("Foreground", OperatorType.Thresholding, 0, 0);
        foreground.AddOutputPort("Image", PortDataType.Image);
        foreground.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var background = new Operator("Background", OperatorType.Thresholding, 0, 0);
        background.AddOutputPort("Image", PortDataType.Image);
        background.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Compositor", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        target.AddInputPort("Background", PortDataType.Image);

        var foregroundPort = foreground.OutputPorts.Single(port => port.Name == "Image");
        var backgroundPort = background.OutputPorts.Single(port => port.Name == "Image");
        var foregroundContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(foreground.Id, foregroundPort.Id, "Image"));
        var backgroundContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(background.Id, backgroundPort.Id, "Image"));

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == foreground.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = foregroundContext
                        },
                        executionTimeMs: 5));
                }

                if (op.Id == background.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 2 },
                            [RoiManagerOperator.SpatialContextOutputKey] = backgroundContext
                        },
                        executionTimeMs: 5));
                }

                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        async Task<Dictionary<string, object>> RunAsync(bool reversed)
        {
            var flow = new OperatorFlow(reversed ? "Reversed" : "Forward");
            flow.AddOperator(foreground);
            flow.AddOperator(background);
            flow.AddOperator(target);
            var imageConnection = new OperatorConnection(
                foreground.Id,
                foregroundPort.Id,
                target.Id,
                target.InputPorts.Single(port => port.Name == "Image").Id);
            var backgroundConnection = new OperatorConnection(
                background.Id,
                backgroundPort.Id,
                target.Id,
                target.InputPorts.Single(port => port.Name == "Background").Id);
            if (reversed)
            {
                flow.AddConnection(backgroundConnection);
                flow.AddConnection(imageConnection);
            }
            else
            {
                flow.AddConnection(imageConnection);
                flow.AddConnection(backgroundConnection);
            }

            targetInputs = null;
            var result = await sut.ExecuteFlowAsync(flow);
            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            targetInputs.Should().NotBeNull();
            return new Dictionary<string, object>(targetInputs!);
        }

        var forward = await RunAsync(reversed: false);
        var reversed = await RunAsync(reversed: true);

        forward[RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(reversed[RoiManagerOperator.ImageSpatialContextInputKey]);
        forward["BackgroundSpatialContext"].Should().BeSameAs(reversed["BackgroundSpatialContext"]);
        forward["Image"].Should().BeEquivalentTo(reversed["Image"]);
        forward["Background"].Should().BeEquivalentTo(reversed["Background"]);
    }

    [Fact]
    public async Task ExecuteFlowAsync_AutoSafeParallel_ShouldFailClosedForMalformedSpatialContextSidecar()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        var targetExecuteCount = 0;

        var flow = new OperatorFlow("ParallelMalformedSpatialFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = "{not valid json"
                        },
                        executionTimeMs: 5));
                }

                targetExecuteCount++;
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow, null, FlowExecutionMode.AutoSafeParallel);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_MALFORMED");
        targetExecuteCount.Should().Be(0, "parallel input preparation must fail before target executor invocation");
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_CachedUpstream_ShouldFailClosedForMalformedSpatialContextSidecar()
    {
        var (sut, executor) = CreateSingleExecutorService(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        var sourceExecuteCount = 0;
        var targetExecuteCount = 0;

        var flow = new OperatorFlow("DebugCachedMalformedSpatialFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var target = new Operator("Target", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);
        var imagePort = source.OutputPorts.Single(port => port.Name == "Image");

        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    sourceExecuteCount++;
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = "{not valid json"
                        },
                        executionTimeMs: 5));
                }

                targetExecuteCount++;
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, imagePort.Id, target.Id, target.InputPorts.Single().Id));

        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };

        var first = await sut.ExecuteFlowDebugAsync(flow, debugOptions);
        var second = await sut.ExecuteFlowDebugAsync(flow, debugOptions);

        first.IsSuccess.Should().BeFalse();
        second.IsSuccess.Should().BeFalse();
        second.ErrorMessage.Should().Contain("SPATIAL_CONTEXT_MALFORMED");
        sourceExecuteCount.Should().Be(1, "second debug run should reuse cached upstream output");
        targetExecuteCount.Should().Be(0, "debug input preparation must fail before target executor invocation");
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldAllowGenericImageOperatorToRelayScopedSpatialContextToRoiManager()
    {
        var imageExecutor = Substitute.For<IOperatorExecutor>();
        imageExecutor.OperatorType.Returns(OperatorType.Thresholding);
        imageExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        var roiExecutor = Substitute.For<IOperatorExecutor>();
        roiExecutor.OperatorType.Returns(OperatorType.RoiManager);
        roiExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        using var sut = new FlowExecutionService(new[] { imageExecutor, roiExecutor }, _logger, _variableContext);

        var flow = new OperatorFlow("GenericRelayToRoiManagerFlow");
        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);
        source.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        var middle = new Operator("GenericPassThrough", OperatorType.Thresholding, 0, 0);
        middle.AddInputPort("Image", PortDataType.Image);
        middle.AddOutputPort("Image", PortDataType.Image);
        middle.AddOutputPort(RoiManagerOperator.ImageSpatialContextInputKey, PortDataType.Any);
        var target = new Operator("RoiTarget", OperatorType.RoiManager, 0, 0);
        target.AddInputPort("Image", PortDataType.Image);

        var sourceImagePort = source.OutputPorts.Single(port => port.Name == "Image");
        var middleImagePort = middle.OutputPorts.Single(port => port.Name == "Image");
        var sourceContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(source.Id, sourceImagePort.Id, "Image"));
        var relayedContext = SpatialContextV1.DefaultImageFull(
            SpatialContextBindingV1.ForFlowOutput(middle.Id, middleImagePort.Id, "Image"));
        Dictionary<string, object>? middleInputs = null;
        Dictionary<string, object>? targetInputs = null;

        imageExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (op.Id == source.Id)
                {
                    return Task.FromResult(OperatorExecutionOutput.Success(
                        new Dictionary<string, object>
                        {
                            ["Image"] = new byte[] { 1 },
                            [RoiManagerOperator.SpatialContextOutputKey] = sourceContext
                        },
                        executionTimeMs: 5));
                }

                middleInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object>
                    {
                        ["Image"] = new byte[] { 2 },
                        [RoiManagerOperator.ImageSpatialContextInputKey] = relayedContext
                    },
                    executionTimeMs: 5));
            });

        roiExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                targetInputs = new Dictionary<string, object>(callInfo.ArgAt<Dictionary<string, object>?>(1)!);
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>(), executionTimeMs: 5));
            });

        flow.AddOperator(source);
        flow.AddOperator(middle);
        flow.AddOperator(target);
        flow.AddConnection(new OperatorConnection(source.Id, sourceImagePort.Id, middle.Id, middle.InputPorts.Single().Id));
        flow.AddConnection(new OperatorConnection(middle.Id, middleImagePort.Id, target.Id, target.InputPorts.Single().Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        middleInputs.Should().NotBeNull();
        middleInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(sourceContext);
        targetInputs.Should().NotBeNull();
        targetInputs![RoiManagerOperator.ImageSpatialContextInputKey].Should().BeSameAs(relayedContext);
    }

    [Fact]
    public async Task ExecuteFlowAsync_ShouldPropagateRoiCropSpatialContextIntoPixelToWorldChain()
    {
        var sourceExecutor = Substitute.For<IOperatorExecutor>();
        sourceExecutor.OperatorType.Returns(OperatorType.ImageAcquisition);
        sourceExecutor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        sourceExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(OperatorExecutionOutput.Success(
                new Dictionary<string, object>
                {
                    ["Image"] = TestHelpers.CreateTestImage(width: 80, height: 80)
                },
                executionTimeMs: 5)));

        var roiExecutor = new RoiManagerOperator(Substitute.For<ILogger<RoiManagerOperator>>());
        var pixelToWorldExecutor = new PixelToWorldTransformOperator(Substitute.For<ILogger<PixelToWorldTransformOperator>>());
        using var sut = new FlowExecutionService(
            new IOperatorExecutor[] { sourceExecutor, roiExecutor, pixelToWorldExecutor },
            _logger,
            _variableContext);

        var flow = new OperatorFlow("RoiCropToPixelToWorld");
        var source = new Operator("ImageSource", OperatorType.ImageAcquisition, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);

        var roi = new Operator("Crop", OperatorType.RoiManager, 0, 0);
        roi.AddInputPort("Image", PortDataType.Image);
        roi.AddOutputPort("Image", PortDataType.Image);
        roi.AddOutputPort(RoiManagerOperator.SpatialContextOutputKey, PortDataType.Any);
        roi.AddParameter(TestHelpers.CreateParameter("Operation", "Crop"));
        roi.AddParameter(TestHelpers.CreateParameter("Shape", "Rectangle"));
        roi.AddParameter(TestHelpers.CreateParameter("X", 10));
        roi.AddParameter(TestHelpers.CreateParameter("Y", 20));
        roi.AddParameter(TestHelpers.CreateParameter("Width", 40));
        roi.AddParameter(TestHelpers.CreateParameter("Height", 30));

        var pixelToWorld = new Operator("PixelToWorld", OperatorType.PixelToWorldTransform, 0, 0);
        pixelToWorld.AddInputPort("Image", PortDataType.Image, isRequired: false);
        pixelToWorld.AddOutputPort("Image", PortDataType.Image);
        pixelToWorld.AddOutputPort("TransformedPoints", PortDataType.PointList);
        pixelToWorld.AddOutputPort("TransformResult", PortDataType.Any);
        pixelToWorld.AddParameter(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        pixelToWorld.AddParameter(TestHelpers.CreateParameter("InputPointX", 2.0));
        pixelToWorld.AddParameter(TestHelpers.CreateParameter("InputPointY", 4.0));
        pixelToWorld.AddParameter(TestHelpers.CreateParameter("CalibrationData", CalibrationBundleV2TestData.CreateAcceptedScaleOffsetBundleJson()));

        flow.AddOperator(source);
        flow.AddOperator(roi);
        flow.AddOperator(pixelToWorld);
        flow.AddConnection(new OperatorConnection(
            source.Id,
            source.OutputPorts.Single(port => port.Name == "Image").Id,
            roi.Id,
            roi.InputPorts.Single(port => port.Name == "Image").Id));
        flow.AddConnection(new OperatorConnection(
            roi.Id,
            roi.OutputPorts.Single(port => port.Name == "Image").Id,
            pixelToWorld.Id,
            pixelToWorld.InputPorts.Single(port => port.Name == "Image").Id));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var pixelResult = result.OperatorResults.Single(item => item.OperatorId == pixelToWorld.Id);
        pixelResult.IsSuccess.Should().BeTrue(pixelResult.ErrorMessage);
        var worldPoint = ((List<Point3d>)pixelResult.OutputData!["TransformedPoints"]).Single();
        worldPoint.X.Should().BeApproximately(0.24, 1e-9);
        worldPoint.Y.Should().BeApproximately(0.48, 1e-9);
        var transformResult = Assert.IsType<Dictionary<string, object>>(pixelResult.OutputData["TransformResult"]);
        transformResult["InputFrame"].Should().Be($"roi.local.{roi.Id:N}.image");
        Convert.ToInt32(transformResult["AppliedSpatialTransformCount"]).Should().Be(1);
        var pointsContext = Assert.IsType<SpatialContextV1>(pixelResult.OutputData["TransformedPointsSpatialContext"]);
        pointsContext.Binding.SourceOperatorId.Should().Be(pixelToWorld.Id);
        pointsContext.Binding.OutputPortId.Should().Be(pixelToWorld.OutputPorts.Single(port => port.Name == "TransformedPoints").Id);
    }

    [Theory]
    [InlineData("cm")]
    [InlineData("m")]
    [InlineData("mm")]
    public async Task ExecuteFlowAsync_ShouldPropagatePointListWorldUnitContextBetweenRealPixelToWorldOperators(string upstreamUnit)
    {
        var pixelToWorldExecutor = new PixelToWorldTransformOperator(Substitute.For<ILogger<PixelToWorldTransformOperator>>());
        using var sut = new FlowExecutionService(
            new IOperatorExecutor[] { pixelToWorldExecutor },
            _logger,
            _variableContext);
        var (flow, downstream) = CreatePixelToWorldPointListChain(upstreamUnit);

        var sequential = await sut.ExecuteFlowAsync(flow, null, FlowExecutionMode.Sequential);
        AssertPixelToWorldPointListChainResult(sequential, downstream, upstreamUnit);

        var autoSafeParallel = await sut.ExecuteFlowAsync(flow, null, FlowExecutionMode.AutoSafeParallel);
        AssertPixelToWorldPointListChainResult(autoSafeParallel, downstream, upstreamUnit);

        var debugOptions = new DebugOptions
        {
            DebugSessionId = Guid.NewGuid(),
            EnableIntermediateCache = true
        };
        var firstDebug = await sut.ExecuteFlowDebugAsync(flow, debugOptions);
        var secondDebug = await sut.ExecuteFlowDebugAsync(flow, debugOptions);
        AssertPixelToWorldPointListChainResult(firstDebug, downstream, upstreamUnit);
        AssertPixelToWorldPointListChainResult(secondDebug, downstream, upstreamUnit);
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_ReusesCachedUpstreamOutputs_WhenOnlyTargetParametersChange()
    {
        var callCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        _executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        _executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                callCounts.AddOrUpdate(op.Name, 1, (_, current) => current + 1);

                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object>
                    {
                        ["Image"] = new byte[] { (byte)callCounts[op.Name] },
                        ["BlobCount"] = op.Parameters.FirstOrDefault(p => p.Name == "Threshold")?.GetValue() ?? 0
                    },
                    executionTimeMs: 5));
            });

        var flow = new OperatorFlow("DebugFlow");
        var upstream = CreateOperatorWithPorts("Upstream", OperatorType.Thresholding);
        var target = CreateOperatorWithPorts("Target", OperatorType.Thresholding);
        target.AddParameter(new Parameter(Guid.NewGuid(), "Threshold", "Threshold", string.Empty, "int", 128, 0, 255, true));

        flow.AddOperator(upstream);
        flow.AddOperator(target);
        flow.AddConnection(CreateConnection(upstream, target));

        var debugSessionId = Guid.NewGuid();
        var inputData = new Dictionary<string, object> { ["Image"] = new byte[] { 1, 2, 3 } };

        var firstResult = await _sut.ExecuteFlowDebugAsync(
            flow,
            new DebugOptions
            {
                DebugSessionId = debugSessionId,
                EnableIntermediateCache = true,
                BreakAtOperatorId = target.Id
            },
            inputData);

        target.Parameters.Single(parameter => parameter.Name == "Threshold").SetValue(180);

        var secondResult = await _sut.ExecuteFlowDebugAsync(
            flow,
            new DebugOptions
            {
                DebugSessionId = debugSessionId,
                EnableIntermediateCache = true,
                BreakAtOperatorId = target.Id
            },
            inputData);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        callCounts["Upstream"].Should().Be(1, "upstream outputs should be reused from the debug cache");
        callCounts["Target"].Should().Be(2, "the edited target node still needs to run again");
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_CachedIntermediateResult_ShouldStayIsolatedFromExternalMutation()
    {
        var executeCount = 0;
        _executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        _executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                executeCount++;
                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object>
                    {
                        ["Image"] = new byte[] { 10, 20, 30 },
                        ["Score"] = 7
                    },
                    executionTimeMs: 3));
            });

        var flow = new OperatorFlow("CacheIsolationFlow");
        var op = CreateOperatorWithPorts("Single", OperatorType.Thresholding);
        flow.AddOperator(op);

        var debugSessionId = Guid.NewGuid();
        var debugOptions = new DebugOptions
        {
            DebugSessionId = debugSessionId,
            EnableIntermediateCache = true
        };
        var inputData = new Dictionary<string, object> { ["Image"] = new byte[] { 1, 2, 3 } };

        var firstResult = await _sut.ExecuteFlowDebugAsync(flow, debugOptions, inputData);
        var firstSnapshotBytes = (byte[])firstResult.DebugOperatorResults.Single().OutputSnapshot!["Image"];
        firstSnapshotBytes[0] = 99;

        var firstIntermediateBytes = (byte[])firstResult.IntermediateResults[op.Id]["Image"];
        firstIntermediateBytes[1] = 88;

        var externalCacheRead = _sut.GetDebugIntermediateResult(debugSessionId, op.Id)!;
        ((byte[])externalCacheRead["Image"])[2] = 77;

        var secondResult = await _sut.ExecuteFlowDebugAsync(flow, debugOptions, inputData);

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        executeCount.Should().Be(1, "second debug run should hit intermediate cache");

        var secondSnapshotBytes = (byte[])secondResult.DebugOperatorResults.Single().OutputSnapshot!["Image"];
        secondSnapshotBytes.Should().Equal(10, 20, 30);

        var secondCacheRead = _sut.GetDebugIntermediateResult(debugSessionId, op.Id)!;
        ((byte[])secondCacheRead["Image"]).Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenDebugCacheByteBudgetExceeded_ShouldEvictOldEntries()
    {
        var executor = Substitute.For<IOperatorExecutor>();
        executor.OperatorType.Returns(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                var fill = string.Equals(op.Name, "First", StringComparison.Ordinal) ? (byte)1 : (byte)2;
                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object> { ["Image"] = Enumerable.Repeat(fill, 60).ToArray() },
                    executionTimeMs: 1));
            });

        using var sut = new FlowExecutionService(
            new[] { executor },
            _logger,
            _variableContext,
            debugCacheMaxBytes: 90,
            debugCacheMaxEntries: 10,
            debugCacheMaxEntryBytes: 80);

        var flow = new OperatorFlow("DebugCacheBudgetFlow");
        var first = CreateOperatorWithPorts("First", OperatorType.Thresholding);
        var second = CreateOperatorWithPorts("Second", OperatorType.Thresholding);
        flow.AddOperator(first);
        flow.AddOperator(second);

        var debugSessionId = Guid.NewGuid();
        var result = await sut.ExecuteFlowDebugAsync(
            flow,
            new DebugOptions
            {
                DebugSessionId = debugSessionId,
                EnableIntermediateCache = true
            });

        result.IsSuccess.Should().BeTrue();
        result.IntermediateResults.Should().ContainKeys(first.Id, second.Id);
        sut.GetDebugIntermediateResult(debugSessionId, first.Id).Should().BeNull();
        ((byte[])sut.GetDebugIntermediateResult(debugSessionId, second.Id)!["Image"]).Should().Equal(Enumerable.Repeat((byte)2, 60));
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenDebugCacheEntryExceedsLimit_ShouldSkipRetainedCacheAndRemoveStaleEntry()
    {
        var executor = Substitute.For<IOperatorExecutor>();
        executor.OperatorType.Returns(OperatorType.Thresholding);
        executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                var size = Convert.ToInt32(op.Parameters.Single(parameter => parameter.Name == "Size").GetValue());
                return Task.FromResult(OperatorExecutionOutput.Success(
                    new Dictionary<string, object> { ["Image"] = new byte[size] },
                    executionTimeMs: 1));
            });

        using var sut = new FlowExecutionService(
            new[] { executor },
            _logger,
            _variableContext,
            debugCacheMaxBytes: 128,
            debugCacheMaxEntries: 10,
            debugCacheMaxEntryBytes: 80);

        var flow = new OperatorFlow("DebugCacheEntryBudgetFlow");
        var op = CreateOperatorWithPorts("Single", OperatorType.Thresholding);
        op.AddParameter(new Parameter(Guid.NewGuid(), "Size", "Size", string.Empty, "int", 40, 0, 200, true));
        flow.AddOperator(op);

        var debugSessionId = Guid.NewGuid();
        var debugOptions = new DebugOptions
        {
            DebugSessionId = debugSessionId,
            EnableIntermediateCache = true
        };

        var firstResult = await sut.ExecuteFlowDebugAsync(flow, debugOptions);
        firstResult.IsSuccess.Should().BeTrue();
        sut.GetDebugIntermediateResult(debugSessionId, op.Id).Should().NotBeNull();

        op.Parameters.Single(parameter => parameter.Name == "Size").SetValue(100);
        var secondResult = await sut.ExecuteFlowDebugAsync(flow, debugOptions);

        secondResult.IsSuccess.Should().BeTrue();
        secondResult.IntermediateResults[op.Id].Should().ContainKey("Image");
        sut.GetDebugIntermediateResult(debugSessionId, op.Id).Should().BeNull();
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenParallelLayerFails_CancelsSiblingOperators()
    {
        var slowStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        _executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                var ct = callInfo.ArgAt<CancellationToken>(2);
                if (string.Equals(op.Name, "Fail", StringComparison.Ordinal))
                {
                    return WaitForSiblingToStartThenFailAsync(slowStartedTcs);
                }

                slowStartedTcs.TrySetResult(true);
                return WaitForOperatorCancellationAsync(ct, canceledTcs);
            });

        var flow = new OperatorFlow("ParallelFailureFlow");
        flow.AddOperator(CreateOperatorWithPorts("Slow", OperatorType.Thresholding));
        flow.AddOperator(CreateOperatorWithPorts("Fail", OperatorType.Thresholding));

        var result = await _sut.ExecuteFlowAsync(flow, enableParallel: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Fail");
        await canceledTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var slowResult = result.OperatorResults.Single(r => r.OperatorName == "Slow");
        slowResult.IsSuccess.Should().BeFalse();
        slowResult.ErrorMessage.Should().Contain("canceled");
        result.OperatorResults.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenParallelFailuresRace_UsesFirstSignaledFailureForErrorMessage()
    {
        var slowReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFailuresTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor.ValidateParameters(Arg.Any<Operator>()).Returns(new ValidationResult { IsValid = true });
        _executor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                if (string.Equals(op.Name, "Fail", StringComparison.Ordinal))
                {
                    return ReleasePrimaryFailureAsync(slowReadyTcs, releaseFailuresTcs);
                }

                return ReleaseSecondaryFailureAsync(releaseFailuresTcs, slowReadyTcs);
            });

        var flow = new OperatorFlow("ParallelFailureRaceFlow");
        flow.AddOperator(CreateOperatorWithPorts("Slow", OperatorType.Thresholding));
        flow.AddOperator(CreateOperatorWithPorts("Fail", OperatorType.Thresholding));

        var result = await _sut.ExecuteFlowAsync(flow, enableParallel: true);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Fail");
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenResultOutputFeedsSideEffect_ShouldReturnResultOutputPayload()
    {
        var thresholdExecutor = Substitute.For<IOperatorExecutor>();
        thresholdExecutor.OperatorType.Returns(OperatorType.Thresholding);
        thresholdExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var op = callInfo.Arg<Operator>();
                return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
                {
                    ["Source"] = op.Name
                }));
            });

        var resultOutputExecutor = Substitute.For<IOperatorExecutor>();
        resultOutputExecutor.OperatorType.Returns(OperatorType.ResultOutput);
        resultOutputExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["Source"] = "result-output",
                ["JudgmentResult"] = "OK"
            })));

        using var sut = new FlowExecutionService(
            new[] { thresholdExecutor, resultOutputExecutor },
            _logger,
            _variableContext);

        var flow = new OperatorFlow("ResultOutputContractFlow");
        var detector = CreateOperatorWithPorts("Detector", OperatorType.Thresholding);
        var resultOutput = CreateOperatorWithPorts("FinalResult", OperatorType.ResultOutput);
        var sideEffect = CreateOperatorWithPorts("Notify", OperatorType.Thresholding);
        flow.AddOperator(detector);
        flow.AddOperator(resultOutput);
        flow.AddOperator(sideEffect);
        flow.AddConnection(CreateConnection(detector, resultOutput));
        flow.AddConnection(CreateConnection(resultOutput, sideEffect));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue();
        result.OutputData.Should().NotBeNull();
        result.OutputData!["Source"].Should().Be("result-output");
        result.OutputData["JudgmentResult"].Should().Be("OK");
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenOperatorShortCircuits_ShouldSkipDownstreamOperators()
    {
        var gateExecutor = Substitute.For<IOperatorExecutor>();
        gateExecutor.OperatorType.Returns(OperatorType.TriggerModule);
        gateExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.ShortCircuit(new Dictionary<string, object>
            {
                ["NoMaterialFrame"] = true
            })));

        var downstreamExecutor = Substitute.For<IOperatorExecutor>();
        downstreamExecutor.OperatorType.Returns(OperatorType.Thresholding);
        downstreamExecutor.ExecuteAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["ShouldNotRun"] = true
            })));

        using var sut = new FlowExecutionService(
            new[] { gateExecutor, downstreamExecutor },
            _logger,
            _variableContext);

        var flow = new OperatorFlow("ShortCircuitFlow");
        flow.AddOperator(CreateOperatorWithPorts("Gate", OperatorType.TriggerModule));
        flow.AddOperator(CreateOperatorWithPorts("Detector", OperatorType.Thresholding));

        var result = await sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue();
        result.WasShortCircuited.Should().BeTrue();
        result.OutputData.Should().ContainKey("NoMaterialFrame");
        await downstreamExecutor.DidNotReceive().ExecuteAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteFlowAsync_WhenOperatorDisabled_ShouldSkipExecutor()
    {
        var flow = new OperatorFlow("DisabledOperatorFlow");
        var op = CreateOperatorWithPorts("Disabled", OperatorType.Thresholding);
        op.Disable();
        flow.AddOperator(op);

        var result = await _sut.ExecuteFlowAsync(flow);

        result.IsSuccess.Should().BeTrue();
        result.OperatorResults.Should().ContainSingle();
        result.OperatorResults.Single().OperatorId.Should().Be(op.Id);
        result.OperatorResults.Single().IsSuccess.Should().BeTrue();
        result.OutputData.Should().BeNull();
        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteFlowDebugAsync_WhenOperatorDisabled_ShouldSkipExecutorAndExposeEmptyPreviewResult()
    {
        var flow = new OperatorFlow("DisabledDebugFlow");
        var op = CreateOperatorWithPorts("DisabledPreview", OperatorType.Thresholding);
        op.Disable();
        flow.AddOperator(op);

        var result = await _sut.ExecuteFlowDebugAsync(
            flow,
            new DebugOptions
            {
                DebugSessionId = Guid.NewGuid(),
                EnableIntermediateCache = true,
                BreakAtOperatorId = op.Id
            });

        result.IsSuccess.Should().BeTrue();
        result.DebugOperatorResults.Should().ContainSingle();
        result.DebugOperatorResults.Single().OperatorId.Should().Be(op.Id);
        result.IntermediateResults.Should().ContainKey(op.Id);
        result.IntermediateResults[op.Id].Should().BeEmpty();
        await _executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void PrepareOperatorInputs_LargeGraph_ShouldPreserveSemantics_AndHitIndexLookups()
    {
        // Arrange: build a large graph with many unrelated nodes/connections.
        var flow = new OperatorFlow("LargeGraph");

        var source = new Operator("Source", OperatorType.Thresholding, 0, 0);
        source.AddOutputPort("Image", PortDataType.Image);

        var branch = new Operator("Branch", OperatorType.ConditionalBranch, 0, 0);
        branch.AddOutputPort("True", PortDataType.Image);
        branch.AddOutputPort("False", PortDataType.Image);

        var target = new Operator("Target", OperatorType.Thresholding, 0, 0);
        target.AddInputPort("Foreground", PortDataType.Image, true);
        target.AddInputPort("DecisionInput", PortDataType.Image, true);

        flow.AddOperator(source);
        flow.AddOperator(branch);
        flow.AddOperator(target);

        var previousNoise = new Operator("Noise-0", OperatorType.Thresholding, 0, 0);
        previousNoise.AddInputPort("Input", PortDataType.Image, true);
        previousNoise.AddOutputPort("Output", PortDataType.Image);
        flow.AddOperator(previousNoise);
        flow.AddConnection(CreateConnection(source, previousNoise));

        for (var i = 1; i < 180; i++)
        {
            var currentNoise = new Operator($"Noise-{i}", OperatorType.Thresholding, 0, 0);
            currentNoise.AddInputPort("Input", PortDataType.Image, true);
            currentNoise.AddOutputPort("Output", PortDataType.Image);
            flow.AddOperator(currentNoise);
            flow.AddConnection(CreateConnection(previousNoise, currentNoise));
            previousNoise = currentNoise;
        }

        flow.AddConnection(new OperatorConnection(
            source.Id,
            source.OutputPorts.Single(p => p.Name == "Image").Id,
            target.Id,
            target.InputPorts.Single(p => p.Name == "Foreground").Id));

        flow.AddConnection(new OperatorConnection(
            branch.Id,
            branch.OutputPorts.Single(p => p.Name == "True").Id,
            target.Id,
            target.InputPorts.Single(p => p.Name == "DecisionInput").Id));

        var operatorOutputs = new Dictionary<Guid, Dictionary<string, object>>
        {
            [source.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Image"] = "image-from-source",
                ["Metadata"] = "meta-from-source"
            },
            [branch.Id] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["True"] = "true-branch-image",
                ["False"] = null!,
                ["Result"] = true,
                ["Condition"] = "Score > 0.5",
                ["ActualValue"] = 0.82d
            }
        };

        var buildIndexMethod = typeof(FlowExecutionService)
            .GetMethod("BuildFlowInputPreparationIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        var index = buildIndexMethod.Invoke(null, [flow]);

        var prepareMethod = typeof(FlowExecutionService)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name == "PrepareOperatorInputs" && method.GetParameters().Length == 4);

        // Act
        var inputs = (Dictionary<string, object>)prepareMethod.Invoke(_sut, [flow, target, operatorOutputs, index])!;

        // Assert: mapping and branch routing semantics remain unchanged.
        inputs["Foreground"].Should().Be("image-from-source");
        inputs["DecisionInput"].Should().Be("true-branch-image");
        inputs["True"].Should().Be("true-branch-image");
        inputs["ConditionResult"].Should().Be(true);
        inputs["Condition"].Should().Be("Score > 0.5");
        inputs["ActualValue"].Should().Be(0.82d);
        inputs["Metadata"].Should().Be("meta-from-source");
        inputs.ContainsKey("False").Should().BeFalse("null branch payload should not be propagated");

        // Assert: indexed lookups are exercised in large graph path.
        ReadIndexLookupCount(index!, "IncomingConnectionLookupCount").Should().BeGreaterThan(0);
        ReadIndexLookupCount(index!, "SourceOperatorLookupCount").Should().BeGreaterThan(0);
        ReadIndexLookupCount(index!, "SourcePortLookupCount").Should().BeGreaterThan(0);
        ReadIndexLookupCount(index!, "TargetPortLookupCount").Should().BeGreaterThan(0);
    }

    [Fact]
    public void CreateDebugCacheFingerprint_SameTextDifferentScalarTypes_ShouldNotCollide()
    {
        var method = typeof(FlowExecutionService).GetMethod(
            "CreateDebugCacheFingerprint",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var op = CreateOperatorWithPorts("FingerprintTarget", OperatorType.Thresholding);
        var intInputs = new Dictionary<string, object> { ["Value"] = 1 };
        var stringInputs = new Dictionary<string, object> { ["Value"] = "1" };

        var intFingerprint = method!.Invoke(null, new object?[] { op, intInputs }) as string;
        var stringFingerprint = method.Invoke(null, new object?[] { op, stringInputs }) as string;

        intFingerprint.Should().NotBeNullOrWhiteSpace();
        stringFingerprint.Should().NotBeNullOrWhiteSpace();
        intFingerprint.Should().NotBe(stringFingerprint);
    }

    private static Operator CreateOperatorWithPorts(string name, OperatorType type)
    {
        var op = new Operator(name, type, 0, 0);
        op.AddInputPort("Input", PortDataType.Image, true);
        op.AddOutputPort("Output", PortDataType.Image);
        return op;
    }

    private static OperatorConnection CreateConnection(Operator source, Operator target)
    {
        return new OperatorConnection(
            source.Id,
            source.OutputPorts.First().Id,
            target.Id,
            target.InputPorts.First().Id);
    }

    private static (OperatorFlow Flow, Operator Downstream) CreatePixelToWorldPointListChain(string upstreamUnit)
    {
        var flow = new OperatorFlow($"PixelToWorldPointListUnit-{upstreamUnit}");
        var upstream = new Operator("PixelToWorld-A", OperatorType.PixelToWorldTransform, 0, 0);
        upstream.AddOutputPort("Image", PortDataType.Image);
        upstream.AddOutputPort("TransformedPoints", PortDataType.PointList);
        upstream.AddOutputPort("TransformResult", PortDataType.Any);
        upstream.AddParameter(TestHelpers.CreateParameter("TransformMode", "PixelToWorld"));
        upstream.AddParameter(TestHelpers.CreateParameter("InputPointX", 100.0));
        upstream.AddParameter(TestHelpers.CreateParameter("InputPointY", 50.0));
        upstream.AddParameter(TestHelpers.CreateParameter("CalibrationData", CreateScaleOffsetBundleJson($"bundle-upstream-{upstreamUnit}", upstreamUnit)));

        var downstream = new Operator("PixelToWorld-B", OperatorType.PixelToWorldTransform, 0, 0);
        downstream.AddInputPort("Points", PortDataType.PointList);
        downstream.AddOutputPort("Image", PortDataType.Image);
        downstream.AddOutputPort("TransformedPoints", PortDataType.PointList);
        downstream.AddOutputPort("TransformResult", PortDataType.Any);
        downstream.AddParameter(TestHelpers.CreateParameter("TransformMode", "WorldToPixel"));
        downstream.AddParameter(TestHelpers.CreateParameter("CalibrationData", CreateScaleOffsetBundleJson("bundle-downstream-mm", "mm")));

        flow.AddOperator(upstream);
        flow.AddOperator(downstream);
        flow.AddConnection(new OperatorConnection(
            upstream.Id,
            upstream.OutputPorts.Single(port => port.Name == "TransformedPoints").Id,
            downstream.Id,
            downstream.InputPorts.Single(port => port.Name == "Points").Id));

        return (flow, downstream);
    }

    private static void AssertPixelToWorldPointListChainResult(
        FlowExecutionResult result,
        Operator downstream,
        string upstreamUnit)
    {
        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var downstreamResult = result.OperatorResults.Single(item => item.OperatorId == downstream.Id);
        downstreamResult.IsSuccess.Should().BeTrue(downstreamResult.ErrorMessage);
        var pixels = ReadPositions(downstreamResult.OutputData!["TransformedPoints"]);
        pixels.Single().X.Should().BeApproximately(100.0, 1e-9);
        pixels.Single().Y.Should().BeApproximately(50.0, 1e-9);

        var transformResult = Assert.IsType<Dictionary<string, object>>(downstreamResult.OutputData["TransformResult"]);
        transformResult["InputUnit"].Should().Be(upstreamUnit);
        var diagnostics = ReadStrings(transformResult["Diagnostics"]);
        diagnostics.Should().Contain(item => item.Contains("PointsSpatialContext", StringComparison.Ordinal));
    }

    private static List<string> ReadStrings(object raw)
    {
        return raw switch
        {
            IEnumerable<string> typed => typed.ToList(),
            IEnumerable<object> objects => objects.Select(item => item?.ToString() ?? string.Empty).ToList(),
            _ => throw new InvalidOperationException($"Unexpected string list type {raw.GetType().Name}.")
        };
    }

    private static List<Position> ReadPositions(object raw)
    {
        return raw switch
        {
            List<Position> typed => typed,
            IEnumerable<object> objects => objects.Select(ReadPosition).ToList(),
            _ => throw new InvalidOperationException($"Unexpected point list type {raw.GetType().Name}.")
        };
    }

    private static Position ReadPosition(object raw)
    {
        switch (raw)
        {
            case Position position:
                return position;
            case IDictionary<string, object> dictionary:
                return new Position(
                    Convert.ToDouble(dictionary["X"]),
                    Convert.ToDouble(dictionary["Y"]));
            case JsonElement element:
                return new Position(
                    element.GetProperty("X").GetDouble(),
                    element.GetProperty("Y").GetDouble());
            default:
                var type = raw.GetType();
                var x = type.GetProperty("X")?.GetValue(raw);
                var y = type.GetProperty("Y")?.GetValue(raw);
                if (x != null && y != null)
                {
                    return new Position(Convert.ToDouble(x), Convert.ToDouble(y));
                }

                throw new InvalidOperationException($"Unexpected point item type {raw.GetType().Name}.");
        }
    }

    private static string CreateScaleOffsetBundleJson(string bundleId, string unit)
    {
        return $$"""
                 {
                   "schemaVersion": 2,
                   "bundleId": "{{bundleId}}",
                   "calibrationVersion": "v-test",
                   "datasetFingerprint": "dataset-test",
                   "checksumSha256": "0123456789abcdef",
                   "calibrationKind": "rigidTransform2D",
                   "transformModel": "scaleOffset",
                   "sourceFrame": "image",
                   "targetFrame": "world",
                   "unit": "{{unit}}",
                   "transform2D": {
                     "model": "scaleOffset",
                     "matrix": [
                       [0.02, 0.0, 0.0],
                       [0.0, 0.02, 0.0]
                     ],
                     "pixelSizeX": 0.02,
                     "pixelSizeY": 0.02
                   },
                   "quality": {
                     "accepted": true,
                     "meanError": 0.05,
                     "maxError": 0.09,
                     "inlierCount": 8,
                     "totalSampleCount": 8,
                     "diagnostics": []
                   },
                   "producerOperator": "FlowExecutionServiceTests"
                 }
                 """;
    }

    private static async Task<OperatorExecutionOutput> WaitForOperatorCancellationAsync(
        CancellationToken cancellationToken,
        TaskCompletionSource<bool> canceledTcs)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return OperatorExecutionOutput.Success(new Dictionary<string, object>());
        }
        catch (OperationCanceledException)
        {
            canceledTcs.TrySetResult(true);
            throw;
        }
    }

    private static async Task<OperatorExecutionOutput> WaitForSiblingToStartThenFailAsync(
        TaskCompletionSource<bool> slowStartedTcs)
    {
        await slowStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        return OperatorExecutionOutput.Failure("boom");
    }

    private static async Task<OperatorExecutionOutput> ReleasePrimaryFailureAsync(
        TaskCompletionSource<bool> slowReadyTcs,
        TaskCompletionSource<bool> releaseFailuresTcs)
    {
        await slowReadyTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        releaseFailuresTcs.TrySetResult(true);
        return OperatorExecutionOutput.Failure("primary boom");
    }

    private static async Task<OperatorExecutionOutput> ReleaseSecondaryFailureAsync(
        TaskCompletionSource<bool> releaseFailuresTcs,
        TaskCompletionSource<bool> slowReadyTcs)
    {
        slowReadyTcs.TrySetResult(true);
        await releaseFailuresTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        return OperatorExecutionOutput.Failure("secondary boom");
    }

    private static int ReadIndexLookupCount(object index, string propertyName)
    {
        var property = index.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!;
        return (int)property.GetValue(index)!;
    }
}
