using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
public sealed class ExecutionAuthoritySafetyTests
{
    [Theory]
    [InlineData("Operator", ExecutionSnapshotSource.Draft, ExecutionRunMode.Preview, false, "ADMISSION_OPERATOR_AUTHORITATIVE_SOURCE_REQUIRED")]
    [InlineData("Operator", ExecutionSnapshotSource.PersistedProject, ExecutionRunMode.FormalPrimary, true, "EXECUTION_AUTHORITY_ALLOWED")]
    [InlineData("Operator", ExecutionSnapshotSource.RuntimePackage, ExecutionRunMode.StationRuntime, true, "EXECUTION_AUTHORITY_ALLOWED")]
    [InlineData("Engineer", ExecutionSnapshotSource.Draft, ExecutionRunMode.Preview, true, "EXECUTION_AUTHORITY_ALLOWED")]
    [InlineData("Admin", ExecutionSnapshotSource.Draft, ExecutionRunMode.Debug, true, "EXECUTION_AUTHORITY_ALLOWED")]
    [InlineData("Engineer", ExecutionSnapshotSource.PersistedProject, ExecutionRunMode.Preview, false, "ADMISSION_SOURCE_MODE_MISMATCH")]
    public void RoleSourceModeMatrix_ShouldFailClosed(
        string role,
        ExecutionSnapshotSource source,
        ExecutionRunMode runMode,
        bool expectedAllowed,
        string expectedCode)
    {
        var snapshot = CreateSnapshot(
            source,
            runMode,
            new ExecutionPrincipal($"{role.ToLowerInvariant()}-1", role, role, true));

        var decision = ExecutionAuthorityMatrix.Validate(snapshot);

        decision.Allowed.Should().Be(expectedAllowed);
        decision.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("missing-revision", "ADMISSION_DRAFT_REVISION_REQUIRED")]
    [InlineData("stale-revision", "ADMISSION_DRAFT_REVISION_REQUIRED")]
    [InlineData("implicit-capability", "ADMISSION_DRAFT_CAPABILITY_CONFIRMATION_REQUIRED")]
    [InlineData("missing-confirmation", "ADMISSION_DRAFT_CONFIRMATION_REQUIRED")]
    [InlineData("same-audit", "ADMISSION_DRAFT_CONFIRMATION_REQUIRED")]
    [InlineData("forged-flow-hash", "ADMISSION_DRAFT_RESOURCE_BINDING_REQUIRED")]
    [InlineData("forged-project-revision", "ADMISSION_DRAFT_RESOURCE_BINDING_REQUIRED")]
    public void DraftEvidenceMatrix_ShouldRejectMissingStaleOrForgedEvidence(
        string variant,
        string expectedCode)
    {
        var snapshot = CreateDraftEvidenceVariant(variant);

        var decision = ExecutionAuthorityMatrix.Validate(snapshot);

        decision.Allowed.Should().BeFalse();
        decision.Code.Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("missing-revision")]
    [InlineData("stale-revision")]
    [InlineData("forged-flow-hash")]
    [InlineData("wrong-package-id")]
    public void RuntimePackageEvidenceMatrix_ShouldRejectUnboundRevisionOrIdentity(string variant)
    {
        const long revision = 12;
        const string packageId = "runtime-package-12";
        var flow = CreateFlow();
        var flowHash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        var bindings = new Dictionary<string, string>
        {
            ["PackageRoot"] = "C:\\ClearVision\\Packages\\runtime-package-12",
            ["PackageId"] = variant == "wrong-package-id" ? "other-package" : packageId,
            ["PackageRevision"] = variant == "stale-revision" ? "11" : revision.ToString(),
            ["PackageFlowHash"] = variant == "forged-flow-hash" ? "FORGED" : flowHash
        };
        if (variant == "missing-revision")
        {
            bindings.Remove("PackageRevision");
        }

        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.RuntimePackage,
            ExecutionRunMode.StationRuntime,
            bindings,
            runtimePackageId: packageId,
            principal: ExecutionPrincipal.System(),
            capabilityManifest: ExecutionCapabilityManifest.Derive(flow));

        var decision = ExecutionAuthorityMatrix.Validate(snapshot);

        decision.Allowed.Should().BeFalse();
        decision.Code.Should().Be("ADMISSION_RUNTIME_PACKAGE_BINDING_INVALID");
    }

    [Theory]
    [InlineData(OperatorType.ImageAcquisition, ExecutionSideEffect.FileRead)]
    [InlineData(OperatorType.TextSave, ExecutionSideEffect.FileWrite)]
    [InlineData(OperatorType.HttpRequest, ExecutionSideEffect.NetworkWrite)]
    [InlineData(OperatorType.DatabaseWrite, ExecutionSideEffect.NetworkWrite)]
    [InlineData(OperatorType.TcpCommunication, ExecutionSideEffect.NetworkWrite)]
    [InlineData(OperatorType.SerialCommunication, ExecutionSideEffect.NetworkWrite)]
    [InlineData(OperatorType.CameraCalibration, ExecutionSideEffect.DeviceWrite)]
    [InlineData(OperatorType.DeepLearning, ExecutionSideEffect.FileRead)]
    [InlineData(OperatorType.SemanticSegmentation, ExecutionSideEffect.FileRead)]
    public void PreviewResourceMatrix_ShouldRejectExternalCapabilities(
        OperatorType operatorType,
        ExecutionSideEffect expectedCapability)
    {
        var @operator = new OperatorFactory().CreateOperator(operatorType, operatorType.ToString(), 0, 0);
        if (operatorType == OperatorType.ImageAcquisition)
        {
            SetParameter(@operator, "SourceType", "File");
            SetParameter(@operator, "FilePath", "C:\\authority-test\\input.png");
        }

        var snapshot = CreateSnapshot(
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            Engineer(),
            @operator);

        snapshot.CapabilityManifest.Capabilities.Should().HaveFlag(expectedCapability);
        var decision = ExecutionAuthorityMatrix.Validate(snapshot);

        decision.Allowed.Should().BeFalse();
        decision.Code.Should().Be("ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED");
    }

    [Theory]
    [InlineData(ExecutionRunMode.FormalPrimary, true, "EXECUTION_AUTHORITY_ALLOWED")]
    [InlineData(ExecutionRunMode.Preview, false, "ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED")]
    [InlineData(ExecutionRunMode.Debug, false, "ADMISSION_DRAFT_PREVIEW_SIDE_EFFECT_BLOCKED")]
    public void DraftExternalCameraAuthority_ShouldAllowOnlyConfirmedFormalRun(
        ExecutionRunMode runMode,
        bool expectedAllowed,
        string expectedCode)
    {
        const long revision = 17;
        const string cameraBindingId = "camera-line-1";
        var flow = CreateFlow();
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "Draft",
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = revision.ToString(),
                ["FlowHash"] = ExecutionFlowIdentity.ComputeFlowHash(flow)
            },
            new ExecutionExternalResourceManifest(cameraBindingId));
        var flowCapabilities = ExecutionCapabilityManifest.Derive(flow).Capabilities;
        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.Draft,
            runMode,
            bindings,
            principal: Engineer(),
            capabilityManifest: new ExecutionCapabilityManifest(
                flowCapabilities | ExecutionSideEffect.DeviceRead,
                isExplicit: true),
            expectedProjectRevision: revision,
            confirmationId: "external-camera-confirmation",
            auditId: "external-camera-audit",
            externalCapabilities: ExecutionSideEffect.DeviceRead);

        var decision = ExecutionAuthorityMatrix.Validate(snapshot);

        decision.Allowed.Should().Be(expectedAllowed);
        decision.Code.Should().Be(expectedCode);
    }

    [Fact]
    public void ResourceManifest_SeedCannotMintExternalCameraAuthority()
    {
        var flow = CreateFlow();
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "StoredProject",
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = "1",
                ["CameraBindingId"] = "client-camera",
                ["ExternalResource:Camera"] = "StoredProject:FORGED"
            });

        bindings.Should().NotContainKey("ExternalResource:Camera");
        bindings.Should().ContainKey("CameraBindingId");
    }

    [Fact]
    public async Task RejectedAuthority_ShouldNotValidateOrDispatchRawEngine()
    {
        var engine = Substitute.For<IFlowExecutionEngine>();
        var service = new GovernedFlowExecutionService(engine);
        var snapshot = CreateSnapshot(
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            new ExecutionPrincipal("operator-1", "Operator", "Operator", true));

        var result = await service.ExecuteWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_OPERATOR_AUTHORITATIVE_SOURCE_REQUIRED");
        engine.DidNotReceiveWithAnyArgs().ValidateFlow(Arg.Any<OperatorFlow>());
        await engine.DidNotReceiveWithAnyArgs().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectedMultiOperatorSnapshot_ShouldReturnFailureInsteadOfThrowing()
    {
        var engine = Substitute.For<IFlowExecutionEngine>();
        var service = new GovernedFlowExecutionService(engine);
        var flow = CreateFlow();
        flow.AddOperator(new OperatorFactory().CreateOperator(OperatorType.Comparator, "Comparator", 10, 10));
        var snapshot = new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            1,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            principal: new ExecutionPrincipal("operator-1", "Operator", "Operator", true),
            capabilityManifest: new ExecutionCapabilityManifest(
                ExecutionCapabilityManifest.Derive(flow).Capabilities,
                isExplicit: true));

        var result = await service.ExecuteOperatorWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_OPERATOR_AUTHORITATIVE_SOURCE_REQUIRED");
        engine.DidNotReceiveWithAnyArgs().ValidateFlow(Arg.Any<OperatorFlow>());
    }

    [Fact]
    public void ResourceManifest_ShouldBindEveryModelTemplateAndCalibrationPathField()
    {
        var names = new[]
        {
            "ModelPath",
            "LabelsPath",
            "ModelCatalogPath",
            "TemplatePath",
            "FeatureBankPath",
            "FolderPath",
            "ImageFolder",
            "LeftImageFolder",
            "RightImageFolder",
            "CalibrationOutputPath",
            "CalibrationAssetId"
        };
        var @operator = new Operator(Guid.NewGuid(), "Authority paths", OperatorType.CameraCalibration, 0, 0);
        foreach (var name in names)
        {
            @operator.AddParameter(new Parameter(
                Guid.NewGuid(),
                name,
                name,
                string.Empty,
                "string",
                string.Empty));
        }

        var authorityFields = ExecutionResourceBindingManifest.AuthorityFieldNames(@operator);

        authorityFields.Should().Contain(names);
    }

    [Fact]
    public async Task InvalidFlow_ShouldNotDispatchRawEngine()
    {
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult
        {
            IsValid = false,
            Errors = ["invalid parameters"]
        });
        var service = new GovernedFlowExecutionService(engine);
        var snapshot = CreateSnapshot(
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            Engineer());

        var result = await service.ExecuteWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("FLOW_VALIDATION_FAILED");
        await engine.DidNotReceiveWithAnyArgs().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBoundary_WhenProjectRevisionAdvances_ShouldRejectStaleSnapshotBeforeExecutor()
    {
        var currentProject = new Project("Current project");
        currentProject.SetPersistenceRevision(6);
        var snapshot = CreateProjectBackedSnapshot(currentProject.Id, persistenceRevision: 5);
        var repository = Substitute.For<IProjectRepository>();
        var repositoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRepository = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.GetByIdFreshAsync(currentProject.Id).Returns(async _ =>
        {
            repositoryEntered.TrySetResult(true);
            await releaseRepository.Task;
            return currentProject;
        });
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult { IsValid = true });
        var service = new GovernedFlowExecutionService(engine, projectRepository: repository);

        var execution = service.ExecuteWithSnapshotAsync(snapshot);
        await repositoryEntered.Task;
        releaseRepository.TrySetResult(true);
        var result = await execution;

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_PROJECT_REVISION_STALE");
        await engine.DidNotReceiveWithAnyArgs().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBoundary_WhenProjectIsDeleted_ShouldRejectBeforeExecutor()
    {
        var currentProject = new Project("Deleted project");
        currentProject.SetPersistenceRevision(5);
        var snapshot = CreateProjectBackedSnapshot(currentProject.Id, currentProject.PersistenceRevision);
        var repository = Substitute.For<IProjectRepository>();
        var repositoryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRepository = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.GetByIdFreshAsync(currentProject.Id).Returns(async _ =>
        {
            repositoryEntered.TrySetResult(true);
            await releaseRepository.Task;
            return null;
        });
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult { IsValid = true });
        var service = new GovernedFlowExecutionService(engine, projectRepository: repository);

        var execution = service.ExecuteWithSnapshotAsync(snapshot);
        await repositoryEntered.Task;
        releaseRepository.TrySetResult(true);
        var result = await execution;

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_PROJECT_DELETED");
        await engine.DidNotReceiveWithAnyArgs().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBoundary_WhenRepositoryReturnsSoftDeletedProject_ShouldRejectBeforeExecutor()
    {
        var deletedProject = new Project("Soft deleted project");
        deletedProject.SetPersistenceRevision(5);
        deletedProject.MarkAsDeleted();
        var snapshot = CreateProjectBackedSnapshot(deletedProject.Id, deletedProject.PersistenceRevision);
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(deletedProject.Id).Returns(Task.FromResult<Project?>(deletedProject));
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult { IsValid = true });
        var service = new GovernedFlowExecutionService(engine, projectRepository: repository);

        var result = await service.ExecuteWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ADMISSION_PROJECT_DELETED");
        await engine.DidNotReceiveWithAnyArgs().ExecuteFlowAsync(
            Arg.Any<OperatorFlow>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBoundary_ShouldHoldProjectAccessLeaseUntilExecutorCompletes()
    {
        var project = new Project("Lease-bound project");
        project.SetPersistenceRevision(5);
        var snapshot = CreateProjectBackedSnapshot(project.Id, project.PersistenceRevision);
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdFreshAsync(project.Id).Returns(Task.FromResult<Project?>(project));
        var transactionRoot = Path.Combine(Path.GetTempPath(), $"cv-execution-lease-{Guid.NewGuid():N}");
        var coordinator = new ProjectSaveCoordinator(
            repository,
            Substitute.For<IProjectFlowStorage>(),
            transactionRoot: transactionRoot);
        var engineEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEngine = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult { IsValid = true });
        engine.ExecuteFlowAsync(
                Arg.Any<OperatorFlow>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                engineEntered.TrySetResult(true);
                await releaseEngine.Task;
                return new FlowExecutionResult { IsSuccess = true };
            });
        var service = new GovernedFlowExecutionService(
            engine,
            projectRepository: repository,
            projectSaveCoordinator: coordinator);

        try
        {
            var execution = service.ExecuteWithSnapshotAsync(snapshot);
            await engineEntered.Task;

            var competingMutationLease = coordinator.AcquireProjectAccessAsync(project.Id);
            await Task.Yield();
            competingMutationLease.IsCompleted.Should().BeFalse(
                "a project mutation must not cross the validated execution dispatch");

            releaseEngine.TrySetResult(true);
            (await execution).IsSuccess.Should().BeTrue();
            await using var acquiredAfterExecution = await competingMutationLease;
        }
        finally
        {
            releaseEngine.TrySetResult(true);
            if (Directory.Exists(transactionRoot))
            {
                Directory.Delete(transactionRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SingleOperatorSnapshot_ShouldValidateBeforeDispatchAndInstallCompositeScope()
    {
        var engine = Substitute.For<IFlowExecutionEngine>();
        engine.ValidateFlow(Arg.Any<OperatorFlow>()).Returns(new FlowValidationResult { IsValid = true });
        ExecutionAuthorityScope? observedScope = null;
        engine.ExecuteOperatorAsync(
                Arg.Any<Operator>(),
                Arg.Any<Dictionary<string, object>?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                observedScope = ExecutionAuthorityContext.Current;
                var @operator = callInfo.ArgAt<Operator>(0);
                return Task.FromResult(new OperatorExecutionResult
                {
                    OperatorId = @operator.Id,
                    OperatorName = @operator.Name,
                    IsSuccess = true
                });
            });
        var service = new GovernedFlowExecutionService(engine);
        var snapshot = CreateSnapshot(
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            Engineer());

        var result = await service.ExecuteOperatorWithSnapshotAsync(snapshot);

        result.IsSuccess.Should().BeTrue();
        observedScope.Should().NotBeNull();
        observedScope!.ProjectId.Should().Be(snapshot.ProjectId);
        observedScope.SessionId.Should().Be(snapshot.SessionId);
        observedScope.FlowId.Should().Be(snapshot.FlowId);
        observedScope.RunId.Should().Be(snapshot.RunId);
        ExecutionAuthorityContext.Current.Should().BeNull();
        await engine.Received(1).ExecuteOperatorAsync(
            Arg.Any<Operator>(),
            Arg.Any<Dictionary<string, object>?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ExecutionStateKey_ShouldRequireGovernedScopeAndSeparateEveryRunDimension()
    {
        var operatorId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var flowId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var principal = Engineer();

        var action = () => ExecutionStateKey.ForOperator(operatorId);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*EXECUTION_STATE_AUTHORITY_REQUIRED*");

        ExecutionStateKey baseline;
        using (ExecutionAuthorityContext.Enter(new ExecutionAuthorityScope(
                   Guid.NewGuid(), projectId, sessionId, flowId, runId,
                   ExecutionSnapshotSource.PersistedProject, ExecutionRunMode.FormalPrimary,
                   "flow-hash", principal, new Dictionary<string, string>())))
        {
            baseline = ExecutionStateKey.ForOperator(operatorId);
        }

        var alternatives = new[]
        {
            baseline with { ProjectId = Guid.NewGuid() },
            baseline with { SessionId = Guid.NewGuid() },
            baseline with { FlowId = Guid.NewGuid() },
            baseline with { RunId = Guid.NewGuid() },
            baseline with { OperatorId = Guid.NewGuid() },
            baseline with { Source = ExecutionSnapshotSource.Draft }
        };
        alternatives.Should().OnlyContain(item => item != baseline);
    }

    [Fact]
    public async Task StatefulOperator_ShouldNotShareFormalStateWithDraftUsingSameOperatorId()
    {
        var executor = new StatisticsOperator(Substitute.For<Microsoft.Extensions.Logging.ILogger<StatisticsOperator>>());
        var @operator = new OperatorFactory().CreateOperator(OperatorType.Statistics, "Statistics", 0, 0);
        var projectId = Guid.NewGuid();
        var flowId = Guid.NewGuid();
        var formalScope = new ExecutionAuthorityScope(
            Guid.NewGuid(), projectId, Guid.NewGuid(), flowId, Guid.NewGuid(),
            ExecutionSnapshotSource.PersistedProject, ExecutionRunMode.FormalPrimary,
            "formal-flow", Engineer(), new Dictionary<string, string>());
        var draftScope = new ExecutionAuthorityScope(
            Guid.NewGuid(), projectId, Guid.NewGuid(), flowId, Guid.NewGuid(),
            ExecutionSnapshotSource.Draft, ExecutionRunMode.Preview,
            "draft-flow", Engineer(), new Dictionary<string, string>());

        OperatorExecutionOutput firstFormal;
        using (ExecutionAuthorityContext.Enter(formalScope))
        {
            firstFormal = await executor.ExecuteAsync(
                @operator,
                new Dictionary<string, object> { ["Value"] = 10d });
        }

        OperatorExecutionOutput firstDraft;
        using (ExecutionAuthorityContext.Enter(draftScope))
        {
            firstDraft = await executor.ExecuteAsync(
                @operator,
                new Dictionary<string, object> { ["Value"] = 99d });
        }

        OperatorExecutionOutput secondFormal;
        using (ExecutionAuthorityContext.Enter(formalScope))
        {
            secondFormal = await executor.ExecuteAsync(
                @operator,
                new Dictionary<string, object> { ["Value"] = 20d });
        }

        Convert.ToInt32(firstFormal.OutputData!["Count"]).Should().Be(1);
        Convert.ToInt32(firstDraft.OutputData!["Count"]).Should().Be(1);
        Convert.ToInt32(secondFormal.OutputData!["Count"]).Should().Be(2);
        Convert.ToDouble(secondFormal.OutputData["Mean"]).Should().Be(15d);
    }

    [Fact]
    public void CanonicalPathSafety_ShouldRejectTraversalOutsideApprovedRoot()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"clearvision-authority-{Guid.NewGuid():N}");
        var approvedRoot = Path.Combine(baseDirectory, "approved");
        Directory.CreateDirectory(approvedRoot);
        try
        {
            var escaped = Path.Combine(approvedRoot, "..", "outside.txt");

            var allowed = CanonicalPathSafety.TryValidateWithinRoots(
                escaped,
                [approvedRoot],
                out _,
                out var code,
                out _);

            allowed.Should().BeFalse();
            code.Should().Be("RESOURCE_PATH_OUTSIDE_APPROVED_ROOT");
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalPathSafety_ShouldRejectReparsePointEscape()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), $"clearvision-reparse-{Guid.NewGuid():N}");
        var approvedRoot = Path.Combine(baseDirectory, "approved");
        var externalRoot = Path.Combine(baseDirectory, "external");
        var link = Path.Combine(approvedRoot, "link");
        Directory.CreateDirectory(approvedRoot);
        Directory.CreateDirectory(externalRoot);
        File.WriteAllText(Path.Combine(externalRoot, "model.bin"), "test");
        Directory.CreateSymbolicLink(link, externalRoot);
        try
        {
            var allowed = CanonicalPathSafety.TryValidateWithinRoots(
                Path.Combine(link, "model.bin"),
                [approvedRoot],
                out _,
                out var code,
                out _);

            allowed.Should().BeFalse();
            code.Should().Be("RESOURCE_PATH_REPARSE_POINT_FORBIDDEN");
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void CanonicalPathSafety_ShouldRejectApprovedRootBelowParentLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var baseDirectory = Path.Combine(Path.GetTempPath(), $"clearvision-root-link-{Guid.NewGuid():N}");
        var physicalParent = Path.Combine(baseDirectory, "physical-parent");
        var physicalApproved = Path.Combine(physicalParent, "approved");
        var linkedParent = Path.Combine(baseDirectory, "linked-parent");
        Directory.CreateDirectory(physicalApproved);
        File.WriteAllText(Path.Combine(physicalApproved, "model.bin"), "test");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkedParent, physicalParent);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var approvedRootBelowLink = Path.Combine(linkedParent, "approved");
            var allowed = CanonicalPathSafety.TryValidateWithinRoots(
                Path.Combine(approvedRootBelowLink, "model.bin"),
                [approvedRootBelowLink],
                out _,
                out var code,
                out _);

            allowed.Should().BeFalse();
            code.Should().Be("RESOURCE_PATH_REPARSE_POINT_FORBIDDEN");
        }
        finally
        {
            if (Directory.Exists(linkedParent))
            {
                Directory.Delete(linkedParent);
            }

            if (Directory.Exists(baseDirectory))
            {
                Directory.Delete(baseDirectory, recursive: true);
            }
        }
    }

    private static ExecutionSnapshot CreateDraftEvidenceVariant(string variant)
    {
        const long revision = 9;
        var flow = CreateFlow();
        var hash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        var expectedRevision = variant switch
        {
            "missing-revision" => (long?)null,
            "stale-revision" => revision - 1,
            _ => revision
        };
        var confirmation = variant == "missing-confirmation" ? null : Guid.NewGuid().ToString("D");
        var audit = variant == "same-audit" ? confirmation : Guid.NewGuid().ToString("D");
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            ExecutionSnapshotSource.Draft,
            ExecutionRunMode.Preview,
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = variant == "forged-project-revision" ? "10" : "9",
                ["FlowHash"] = variant == "forged-flow-hash" ? "FORGED" : hash
            },
            principal: Engineer(),
            capabilityManifest: new ExecutionCapabilityManifest(
                ExecutionSideEffect.None,
                isExplicit: variant != "implicit-capability"),
            expectedProjectRevision: expectedRevision,
            confirmationId: confirmation,
            auditId: audit);
    }

    private static ExecutionSnapshot CreateSnapshot(
        ExecutionSnapshotSource source,
        ExecutionRunMode runMode,
        ExecutionPrincipal principal,
        Operator? @operator = null,
        ExecutionSideEffect? declaredCapabilities = null)
    {
        const long revision = 5;
        var flow = CreateFlow(@operator);
        var hash = ExecutionFlowIdentity.ComputeFlowHash(flow);
        var bindings = new Dictionary<string, string>
        {
            ["ProjectRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FlowHash"] = hash
        };
        string? packageId = null;
        if (source == ExecutionSnapshotSource.RuntimePackage)
        {
            packageId = "package-1";
            bindings["PackageRoot"] = "C:\\ClearVision\\Package";
            bindings["PackageId"] = packageId;
            bindings["PackageRevision"] = revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            bindings["PackageFlowHash"] = hash;
        }

        var capabilities = declaredCapabilities ?? ExecutionCapabilityManifest.Derive(flow).Capabilities;
        return new ExecutionSnapshot(
            Guid.NewGuid(),
            flow,
            revision,
            source,
            runMode,
            bindings,
            runtimePackageId: packageId,
            principal: principal,
            capabilityManifest: new ExecutionCapabilityManifest(
                capabilities,
                isExplicit: source == ExecutionSnapshotSource.Draft),
            expectedProjectRevision: source == ExecutionSnapshotSource.Draft ? revision : null,
            confirmationId: source == ExecutionSnapshotSource.Draft ? Guid.NewGuid().ToString("D") : null,
            auditId: source == ExecutionSnapshotSource.Draft ? Guid.NewGuid().ToString("D") : null);
    }

    private static ExecutionSnapshot CreateProjectBackedSnapshot(Guid projectId, long persistenceRevision)
    {
        var flow = CreateFlow();
        var bindings = ExecutionResourceBindingManifest.Build(
            flow,
            "StoredProject",
            new Dictionary<string, string>
            {
                ["ProjectRevision"] = persistenceRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        return new ExecutionSnapshot(
            projectId,
            flow,
            persistenceRevision,
            ExecutionSnapshotSource.PersistedProject,
            ExecutionRunMode.FormalPrimary,
            bindings,
            principal: ExecutionPrincipal.System(),
            capabilityManifest: ExecutionCapabilityManifest.Derive(flow));
    }

    private static OperatorFlow CreateFlow(Operator? @operator = null)
    {
        var flow = new OperatorFlow("Authority matrix");
        flow.AddOperator(@operator ?? new OperatorFactory().CreateOperator(OperatorType.ResultOutput, "Result", 0, 0));
        return flow;
    }

    private static ExecutionPrincipal Engineer() =>
        new("engineer-1", "Engineer", "Engineer", true);

    private static void SetParameter(Operator @operator, string name, object value)
    {
        var parameter = @operator.Parameters.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        parameter.Should().NotBeNull($"{@operator.Type} should declare {name}");
        parameter!.SetValue(value);
    }
}
