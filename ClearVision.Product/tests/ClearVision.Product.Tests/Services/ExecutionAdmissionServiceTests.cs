using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

/// <summary>
/// 执行准入矩阵测试：验证“正式运行”与“预览/调参”两类 surface 的语义边界。
/// 正式运行（Stored/StudioInspectionRun/StationRuntimeExecution）允许流程声明的真实 I/O；
/// 预览（NodePreview/OperatorPreview/AutoTunePreview）除已实现的 safe dry-run 外阻断真实 I/O；
/// LegacyWebMessageExecution 恒定禁用。
/// </summary>
public sealed class ExecutionAdmissionServiceTests
{
    // 预览 surface 下必须被阻断的副作用算子类型（覆盖写盘/网络/PLC/数据库/标定）。
    public static readonly OperatorType[] AlwaysBlockedInPreviewTypes =
    [
        OperatorType.ImageSave,
        OperatorType.TextSave,
        OperatorType.HttpRequest,
        OperatorType.MqttPublish,
        OperatorType.DatabaseWrite,
        OperatorType.TcpCommunication,
        OperatorType.ModbusCommunication,
        OperatorType.SiemensS7Communication,
        OperatorType.MitsubishiMcCommunication,
        OperatorType.CameraCalibration
    ];

    public static readonly ExecutionAdmissionSurface[] PreviewSurfaces =
    [
        ExecutionAdmissionSurface.NodePreview,
        ExecutionAdmissionSurface.OperatorPreview,
        ExecutionAdmissionSurface.AutoTunePreview
    ];

    public static readonly ExecutionAdmissionSurface[] OfficialSurfaces =
    [
        ExecutionAdmissionSurface.StoredProjectExecution,
        ExecutionAdmissionSurface.StudioInspectionRun,
        ExecutionAdmissionSurface.StationRuntimeExecution
    ];

    public static IEnumerable<object[]> PreviewSurfaceBlockedMatrix()
    {
        foreach (var surface in PreviewSurfaces)
        {
            foreach (var type in AlwaysBlockedInPreviewTypes)
            {
                yield return [surface, type];
            }
        }
    }

    public static IEnumerable<object[]> OfficialSurfaceAllowedMatrix()
    {
        // 正式运行必须允许的全部 I/O 算子（含写盘/相机/网络/PLC/数据库/标定）。
        var allowedTypes = new List<OperatorType>(AlwaysBlockedInPreviewTypes);
        foreach (var surface in OfficialSurfaces)
        {
            foreach (var type in allowedTypes)
            {
                yield return [surface, type];
            }
        }
    }

    public static IEnumerable<object[]> PreviewSurfaces_Data() =>
        PreviewSurfaces.Select(surface => new object[] { surface });

    public static IEnumerable<object[]> OfficialSurfaces_Data() =>
        OfficialSurfaces.Select(surface => new object[] { surface });

    [Theory]
    [MemberData(nameof(PreviewSurfaceBlockedMatrix))]
    public void ValidateFlowSideEffects_OnPreviewSurface_ShouldBlockSideEffectOperators(
        ExecutionAdmissionSurface surface,
        OperatorType blockedType)
    {
        var admission = CreateService();
        var flow = SingleOperatorFlow(blockedType);

        var result = admission.ValidateFlowSideEffects(flow, surface);

        result.IsAllowed.Should().BeFalse($"{blockedType} 在预览 surface {surface} 必须被阻断");
        result.Code.Should().Contain("PREVIEW_SIDE_EFFECT_BLOCKED");
        result.Message.Should().Contain("预览不会访问外部设备");
    }

    [Theory]
    [MemberData(nameof(PreviewSurfaces_Data))]
    public void ValidateFlowSideEffects_OnPreviewSurface_ShouldBlockResultOutputSaveToFile(
        ExecutionAdmissionSurface surface)
    {
        var admission = CreateService();
        var flow = SingleOperatorFlow(OperatorType.ResultOutput, ("SaveToFile", true));

        var result = admission.ValidateFlowSideEffects(flow, surface);

        result.IsAllowed.Should().BeFalse();
        result.Violations.Should().Contain(v =>
            v.OperatorType == OperatorType.ResultOutput && v.ParameterName == "SaveToFile");
    }

    [Theory]
    [MemberData(nameof(PreviewSurfaces_Data))]
    public void ValidateFlowSideEffects_OnPreviewSurface_ShouldAllowResultOutputWithoutSaveToFile(
        ExecutionAdmissionSurface surface)
    {
        var admission = CreateService();
        var flow = SingleOperatorFlow(OperatorType.ResultOutput, ("SaveToFile", false));

        admission.ValidateFlowSideEffects(flow, surface).IsAllowed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(PreviewSurfaces_Data))]
    public void ValidateFlowSideEffects_OnPreviewSurface_ShouldBlockCameraImageAcquisition(
        ExecutionAdmissionSurface surface)
    {
        var admission = CreateService();
        var flow = SingleOperatorFlow(OperatorType.ImageAcquisition, ("SourceType", "Camera"));

        var result = admission.ValidateFlowSideEffects(flow, surface);

        result.IsAllowed.Should().BeFalse("相机采集在任何预览 surface 都必须被阻断");
        result.Violations.Should().Contain(v => v.OperatorType == OperatorType.ImageAcquisition);
    }

    [Theory]
    [InlineData(ExecutionAdmissionSurface.NodePreview, true)]
    [InlineData(ExecutionAdmissionSurface.AutoTunePreview, true)]
    [InlineData(ExecutionAdmissionSurface.OperatorPreview, false)]
    public void ValidateFlowSideEffects_FileImageAcquisition_ShouldFollowPerSurfacePolicy(
        ExecutionAdmissionSurface surface,
        bool expectedAllowed)
    {
        // 产品策略：节点预览与线序预览允许读本地样张生成预览图；单算子预览只接受显式输入图，故阻断 File 图源。
        var admission = CreateService();
        var flow = SingleOperatorFlow(
            OperatorType.ImageAcquisition,
            ("SourceType", "File"),
            ("FilePath", @"C:\images\sample.png"));

        admission.ValidateFlowSideEffects(flow, surface).IsAllowed.Should().Be(expectedAllowed);
    }

    [Theory]
    [MemberData(nameof(OfficialSurfaceAllowedMatrix))]
    public void ValidateFlowSideEffects_OnOfficialSurface_ShouldAllowDeclaredIo(
        ExecutionAdmissionSurface surface,
        OperatorType ioType)
    {
        var admission = CreateService();
        var flow = SingleOperatorFlow(ioType);

        admission.ValidateFlowSideEffects(flow, surface).IsAllowed
            .Should().BeTrue($"{ioType} 在正式运行 surface {surface} 必须允许真实 I/O");
    }

    [Theory]
    [MemberData(nameof(OfficialSurfaces_Data))]
    public void ValidateFlowSideEffects_OnOfficialSurface_ShouldAllowFileAndCameraImageAcquisition(
        ExecutionAdmissionSurface surface)
    {
        var admission = CreateService();
        var fileFlow = SingleOperatorFlow(
            OperatorType.ImageAcquisition,
            ("SourceType", "File"),
            ("FilePath", @"C:\images\sample.png"));
        var cameraFlow = SingleOperatorFlow(OperatorType.ImageAcquisition, ("SourceType", "Camera"));
        var resultOutputFlow = SingleOperatorFlow(OperatorType.ResultOutput, ("SaveToFile", true));

        admission.ValidateFlowSideEffects(fileFlow, surface).IsAllowed.Should().BeTrue();
        admission.ValidateFlowSideEffects(cameraFlow, surface).IsAllowed.Should().BeTrue();
        admission.ValidateFlowSideEffects(resultOutputFlow, surface).IsAllowed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(OfficialSurfaces_Data))]
    public async Task ValidateFlowAsync_OnOfficialSurface_WithActiveProject_ShouldAllowSideEffectFlow(
        ExecutionAdmissionSurface surface)
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-official-project"));
        var admission = new ExecutionAdmissionService(repository);
        var flow = SingleOperatorFlow(OperatorType.ImageSave);

        var result = await admission.ValidateFlowAsync(projectId, flow, surface);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateFlowAsync_OfficialSurfaceWithoutDecisionBinding_ShouldRejectSynchronously()
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var flow = new OperatorFlow("missing-binding");
        flow.AddOperator(new Operator(Guid.NewGuid(), "Output", OperatorType.ResultOutput, 0, 0));

        var result = await new ExecutionAdmissionService(repository).ValidateFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StudioInspectionRun);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("ADMISSION_DECISION_BINDING_REQUIRED");
        result.Violations.Should().ContainSingle(item => item.Code == "DECISION_BINDING_REQUIRED");
    }

    [Fact]
    public async Task ValidateFlowAsync_ResultOutputTextBinding_ShouldRejectBeforeOfficialExecution()
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var flow = new OperatorFlow("formatted-result-binding");
        var output = new Operator(Guid.NewGuid(), "Result Output", OperatorType.ResultOutput, 0, 0);
        output.AddOutputPort("Text", PortDataType.String);
        flow.AddOperator(output);
        var port = output.OutputPorts.Single();
        flow.DecisionConfiguration = new ClearVision.Product.Core.Decisions.DecisionConfiguration
        {
            FinalDecisionBinding = new ClearVision.Product.Core.Decisions.FinalDecisionBinding
            {
                SourceOperatorId = output.Id,
                SourceOutputPortId = port.Id,
                SourceOutputName = port.Name,
                DataType = ClearVision.Product.Core.Decisions.DecisionValueType.String,
                Rule = ClearVision.Product.Core.Decisions.DecisionInterpretationRule.StringMap,
                OkValue = "OK",
                NgValue = "NG"
            }
        };

        var result = await new ExecutionAdmissionService(repository).ValidateFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StudioInspectionRun);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("ADMISSION_DECISION_SOURCE_OUTPUT_INELIGIBLE");
        result.Message.Should().NotContain("{\"detections\"");
    }

    [Fact]
    public async Task ValidateFlowAsync_PreviewSurfaceWithoutDecisionBinding_ShouldRemainAllowed()
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var flow = new OperatorFlow("preview-without-binding");
        flow.AddOperator(new Operator(Guid.NewGuid(), "Threshold", OperatorType.Thresholding, 0, 0));

        var result = await new ExecutionAdmissionService(repository).ValidateFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.NodePreview);

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(RuntimeStatus.Starting)]
    [InlineData(RuntimeStatus.Running)]
    [InlineData(RuntimeStatus.Stopping)]
    public async Task ValidateFlowAsync_OfficialSurfaceWithActiveRuntime_ShouldRejectWithStableCode(RuntimeStatus status)
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        coordinator.GetState(projectId).Returns(new RuntimeState
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Status = status,
            StartedAt = DateTime.UtcNow
        });
        var flow = SingleOperatorFlow(OperatorType.ResultOutput);

        var result = await new ExecutionAdmissionService(repository, coordinator).ValidateFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StoredProjectExecution);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("ADMISSION_RUNTIME_ALREADY_ACTIVE");
    }

    [Theory]
    [InlineData(RuntimeStatus.Stopped)]
    [InlineData(RuntimeStatus.Faulted)]
    public async Task ValidateFlowAsync_OfficialSurfaceWithTerminalRuntimeResidue_ShouldAllowCoordinatorToDecide(RuntimeStatus status)
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
        coordinator.GetState(projectId).Returns(new RuntimeState
        {
            ProjectId = projectId,
            SessionId = Guid.NewGuid(),
            Status = status,
            StartedAt = DateTime.UtcNow,
            StoppedAt = DateTime.UtcNow
        });
        var flow = SingleOperatorFlow(OperatorType.ResultOutput);

        var result = await new ExecutionAdmissionService(repository, coordinator).ValidateFlowAsync(
            projectId,
            flow,
            ExecutionAdmissionSurface.StoredProjectExecution);

        result.IsAllowed.Should().BeTrue();
        result.Code.Should().Be("ADMISSION_ALLOWED");
    }

    [Theory]
    [MemberData(nameof(OfficialSurfaces_Data))]
    public async Task ValidateFlowAsync_OnOfficialSurface_WithMissingProject_ShouldStillEnforceProjectGate(
        ExecutionAdmissionSurface surface)
    {
        // 正式运行仍保留非 I/O 安全校验：项目不存在必须拒绝。
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns((Project?)null);
        var admission = new ExecutionAdmissionService(repository);
        var flow = SingleOperatorFlow(OperatorType.ImageSave);

        var result = await admission.ValidateFlowAsync(projectId, flow, surface);

        result.IsAllowed.Should().BeFalse();
        result.Code.Should().Be("ADMISSION_PROJECT_NOT_ACTIVE");
        // 正式运行的拒绝文案不得套用“预览”话术。
        result.Message.Should().NotContain("预览");
    }

    [Fact]
    public async Task LegacyWebMessageExecution_ShouldAlwaysBeRejected()
    {
        var projectId = Guid.NewGuid();
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(projectId).Returns(new Project("active-project"));
        var admission = new ExecutionAdmissionService(repository);
        var flow = SingleOperatorFlow(OperatorType.ImageSave);

        var flowResult = await admission.ValidateFlowAsync(
            projectId, flow, ExecutionAdmissionSurface.LegacyWebMessageExecution);
        var sideEffectResult = admission.ValidateFlowSideEffects(
            flow, ExecutionAdmissionSurface.LegacyWebMessageExecution);
        var operatorResult = admission.ValidateOperator(
            flow.Operators[0], ExecutionAdmissionSurface.LegacyWebMessageExecution);
        var explicitResult = admission.ValidateLegacyWebMessage("StartInspectionCommand");

        flowResult.IsAllowed.Should().BeFalse();
        flowResult.Code.Should().Be("ADMISSION_LEGACY_WEBMESSAGE_DISABLED");
        sideEffectResult.IsAllowed.Should().BeFalse();
        operatorResult.IsAllowed.Should().BeFalse();
        explicitResult.IsAllowed.Should().BeFalse();
        explicitResult.Code.Should().Be("ADMISSION_LEGACY_WEBMESSAGE_DISABLED");
    }

    [Theory]
    [InlineData(ExecutionAdmissionSurface.StoredProjectExecution, true)]
    [InlineData(ExecutionAdmissionSurface.StudioInspectionRun, true)]
    [InlineData(ExecutionAdmissionSurface.StationRuntimeExecution, true)]
    [InlineData(ExecutionAdmissionSurface.NodePreview, false)]
    [InlineData(ExecutionAdmissionSurface.OperatorPreview, false)]
    [InlineData(ExecutionAdmissionSurface.AutoTunePreview, false)]
    [InlineData(ExecutionAdmissionSurface.LegacyWebMessageExecution, false)]
    public void IsOfficialExecutionSurface_ShouldClassifySurfacesCorrectly(
        ExecutionAdmissionSurface surface,
        bool expectedOfficial)
    {
        ExecutionAdmissionService.IsOfficialExecutionSurface(surface).Should().Be(expectedOfficial);
    }

    private static ExecutionAdmissionService CreateService() =>
        new(Substitute.For<IProjectRepository>());

    private static OperatorFlow SingleOperatorFlow(
        OperatorType type,
        params (string Name, object Value)[] parameters)
    {
        var flow = new OperatorFlow($"admission-{type}-flow");
        var op = new Operator(Guid.NewGuid(), type.ToString(), type, 0, 0);
        foreach (var (name, value) in parameters)
        {
            op.AddParameter(new Parameter(Guid.NewGuid(), name, name, string.Empty, InferDataType(value), value));
        }

        flow.AddOperator(op);
        return flow.BindStringDecision(op);
    }

    private static string InferDataType(object value) =>
        value switch
        {
            bool => "bool",
            int => "int",
            _ => "string"
        };
}

