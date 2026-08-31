using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "camera-authority", Suites = "ServicesRegression")]
public sealed class IntelligentDetectionServiceAuthorityTests
{
    [Theory]
    [InlineData("missing-binding")]
    [InlineData("SN-AUTHORIZED")]
    [InlineData("disabled-binding")]
    [InlineData("unbound-camera")]
    public async Task ExecuteWithRetryAsync_WhenCameraBindingIsNotAuthoritative_ShouldNotAcquireOrExecute(
        string requestedId)
    {
        var admission = CreateAllowedAdmission();
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(new List<CameraBindingConfig>
        {
            new()
            {
                Id = "authorized-binding",
                SerialNumber = "SN-AUTHORIZED",
                IsEnabled = true
            },
            new()
            {
                Id = "disabled-binding",
                SerialNumber = "SN-DISABLED",
                IsEnabled = false
            },
            new()
            {
                Id = "unbound-camera",
                SerialNumber = string.Empty,
                IsEnabled = true
            }
        });
        var flowService = Substitute.For<IFlowExecutionService>();
        flowService.ValidateSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowValidationResult { IsValid = true }));
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);
        var snapshot = CreateStoredSnapshot(requestedId);

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            requestedId,
            flowService,
            snapshot,
            new RetryPolicy { MaxRetries = 0 });

        result.IsSuccess.Should().BeFalse();
        await cameraManager.DidNotReceive().GetOrCreateCameraAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        cameraManager.DidNotReceive().GetCamera(Arg.Any<string>());
        await flowService.Received(1).ValidateSnapshotAsync(
            snapshot,
            Arg.Any<CancellationToken>());
        await flowService.DidNotReceive().ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_LegacyRawFlowOverload_ShouldFailClosedBeforeAdmissionOrCameraAccess()
    {
        var admission = Substitute.For<IExecutionAdmissionService>();
        var cameraManager = Substitute.For<ICameraManager>();
        var flowService = Substitute.For<IFlowExecutionService>();
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            "client-camera-id",
            flowService,
            new OperatorFlow("legacy-draft"),
            new RetryPolicy { MaxRetries = 0 });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_AUTHORITATIVE_SNAPSHOT_REQUIRED");
        admission.ReceivedCalls().Should().BeEmpty();
        cameraManager.ReceivedCalls().Should().BeEmpty();
        flowService.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenFlowValidationRejects_ShouldNotInspectOrLeaseCamera()
    {
        var admission = CreateAllowedAdmission();
        var cameraManager = Substitute.For<ICameraManager>();
        var flowService = Substitute.For<IFlowExecutionService>();
        flowService.ValidateSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowValidationResult
            {
                IsValid = false,
                Errors = ["invalid flow"]
            }));
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            "authorized-binding",
            flowService,
            CreateStoredSnapshot("authorized-binding"),
            new RetryPolicy { MaxRetries = 0 });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_FLOW_INVALID");
        cameraManager.DidNotReceive().GetBindings();
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await flowService.DidNotReceive().ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_RawCameraTargetDiffersFromSnapshotBinding_ShouldRejectBeforeCameraLookup()
    {
        var admission = CreateAllowedAdmission();
        var cameraManager = Substitute.For<ICameraManager>();
        var flowService = Substitute.For<IFlowExecutionService>();
        flowService.ValidateSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowValidationResult { IsValid = true }));
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            "SN-CLIENT-FORGED",
            flowService,
            CreateStoredSnapshot("authorized-binding"),
            new RetryPolicy { MaxRetries = 0 });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_CAMERA_BINDING_REQUIRED");
        cameraManager.DidNotReceive().GetBindings();
        await cameraManager.DidNotReceive().AcquireByBindingLeaseAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await flowService.DidNotReceive().ExecuteWithSnapshotAsync(
            Arg.Any<ExecutionSnapshot>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_UnboundedRetryPolicy_ShouldRejectBeforeAdmissionOrCameraAccess()
    {
        var admission = Substitute.For<IExecutionAdmissionService>();
        var cameraManager = Substitute.For<ICameraManager>();
        var flowService = Substitute.For<IFlowExecutionService>();
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            "authorized-binding",
            flowService,
            CreateStoredSnapshot("authorized-binding"),
            new RetryPolicy { MaxRetries = int.MaxValue });

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_RETRY_POLICY_INVALID");
        admission.ReceivedCalls().Should().BeEmpty();
        cameraManager.ReceivedCalls().Should().BeEmpty();
        flowService.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_AuthoritativeBinding_ShouldHoldLeaseThroughAcquisitionAndExecution()
    {
        var admission = CreateAllowedAdmission();
        var cameraManager = Substitute.For<ICameraManager>();
        cameraManager.GetBindings().Returns(
        [
            new CameraBindingConfig
            {
                Id = "authorized-binding",
                SerialNumber = "SN-AUTHORIZED",
                IsEnabled = true
            }
        ]);
        var camera = Substitute.For<ICamera>();
        camera.AcquireSingleFrameAsync().Returns(Task.FromResult(new byte[] { 1, 2, 3 }));
        var lease = Substitute.For<ICameraLease>();
        lease.Camera.Returns(camera);
        cameraManager.AcquireByBindingLeaseAsync(
                "authorized-binding",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(lease));
        var flowService = Substitute.For<IFlowExecutionService>();
        flowService.ValidateSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowValidationResult { IsValid = true }));
        flowService.ExecuteWithSnapshotAsync(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FlowExecutionResult { IsSuccess = true }));
        var sut = new IntelligentDetectionService(
            NullLogger<IntelligentDetectionService>.Instance,
            admission);
        var snapshot = CreateStoredSnapshot("authorized-binding");

        var result = await sut.ExecuteWithRetryAsync(
            cameraManager,
            "authorized-binding",
            flowService,
            snapshot,
            new RetryPolicy { MaxRetries = 0 });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await cameraManager.Received(1).AcquireByBindingLeaseAsync(
            "authorized-binding",
            Arg.Any<CancellationToken>());
        await camera.Received(1).AcquireSingleFrameAsync();
        await flowService.Received(1).ExecuteWithSnapshotAsync(
            snapshot,
            Arg.Is<Dictionary<string, object>?>(inputs =>
                inputs != null && inputs.ContainsKey("Image")),
            false,
            Arg.Any<CancellationToken>());
        await lease.Received(1).DisposeAsync();
        await cameraManager.DidNotReceive().GetOrCreateByBindingAsync(Arg.Any<string>());
        await cameraManager.DidNotReceive().GetOrCreateCameraAsync(Arg.Any<string>());
    }

    private static IExecutionAdmissionService CreateAllowedAdmission()
    {
        var admission = Substitute.For<IExecutionAdmissionService>();
        admission.ValidateProjectAsync(
                Arg.Any<Guid>(),
                Arg.Any<ExecutionAdmissionSurface>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExecutionAdmissionResult.Allow()));
        admission.ValidateSnapshot(
                Arg.Any<ExecutionSnapshot>(),
                Arg.Any<ExecutionAdmissionSurface>())
            .Returns(ExecutionAdmissionResult.Allow());
        return admission;
    }

    private static ExecutionSnapshot CreateStoredSnapshot(string cameraBindingId)
    {
        const long revision = 7;
        var flow = new OperatorFlow("authority-test");
        var acquisition = new Operator(
            Guid.NewGuid(),
            "Bound camera acquisition",
            OperatorType.ImageAcquisition,
            0,
            0);
        acquisition.AddParameter(new Parameter(
            Guid.NewGuid(),
            "SourceType",
            "SourceType",
            string.Empty,
            "string",
            "Camera"));
        acquisition.AddParameter(new Parameter(
            Guid.NewGuid(),
            "CameraId",
            "CameraId",
            string.Empty,
            "string",
            cameraBindingId));
        flow.AddOperator(acquisition);
        var seed = new Dictionary<string, string>
        {
            ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [IntelligentDetectionService.CameraBindingResourceKey] = cameraBindingId
        };
        var bindings = ExecutionResourceBindingManifest.Build(flow, "StoredProject", seed);
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            bindings,
            principal: ExecutionPrincipal.System("intelligent-detection-test"));
    }
}
