using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Tests.Services;

[TestClassification(TestDomain.General, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "ServicesRegression")]
[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
public sealed class ProjectMutationAuthorityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Fact]
    public async Task MutateAsync_MetadataPatch_ShouldLoadOneAuthoritativeSnapshotWithoutTouchingFlowBytesOrMetadata()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("before", "before-description");
            var flow = CreateFlow("authoritative-flow");
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, SerializeFlow(flow), project.PersistenceRevision);
            var originalFlowJson = flowStorage.FlowJson;
            var originalFlowMetadata = flowStorage.Metadata;
            var repository = CreateRepository(project);
            var assets = new RecordingProjectAssetStorage();
            var runtime = Substitute.For<IInspectionRuntimeCoordinator>();
            var authority = CreateAuthority(repository, flowStorage, root, runtime, assets);

            var result = await authority.MutateAsync(
                project.Id,
                0,
                ProjectMutationPatch.Metadata(
                    ProjectPatchValue<string>.Present("after"),
                    ProjectPatchValue<string?>.Present("after-description")));

            result.Changed.Should().BeTrue();
            result.Diff.Should().Be(new ProjectMutationDiff(true, false, false, false));
            result.Project.PersistenceRevision.Should().Be(1);
            result.Project.Name.Should().Be("after");
            result.Project.Description.Should().Be("after-description");
            flowStorage.FlowJson.Should().Be(originalFlowJson);
            flowStorage.Metadata.Should().Be(originalFlowMetadata);
            flowStorage.SaveCount.Should().Be(0);
            flowStorage.LoadJsonCount.Should().BeGreaterThan(0);
            flowStorage.LoadMetadataCount.Should().Be(1);
            assets.LoadAssetsCount.Should().BeGreaterThan(0);
            assets.LoadMetadataCount.Should().Be(1);
            await runtime.DidNotReceiveWithAnyArgs().TryAcquireMutationLeaseAsync(
                default,
                default!,
                default);
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_IdenticalExplicitFlowAndSchema_ShouldHaveNoActualDiffLeaseOrRevisionIncrement()
    {
        var root = CreateTempPath();
        try
        {
            var variableId = Guid.NewGuid();
            var project = new Project("project", "description");
            var schema = CreateSchema(variableId, "stats.count");
            project.UpdateGlobalVariables(schema);
            var flow = CreateFlow("authoritative-flow");
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, SerializeFlow(flow), 0);
            var repository = CreateRepository(project);
            var runtime = Substitute.For<IInspectionRuntimeCoordinator>();
            var authority = CreateAuthority(repository, flowStorage, root, runtime);

            var flowResult = await authority.MutateAsync(
                project.Id,
                0,
                ProjectMutationPatch.FlowOnly(flow));
            var schemaResult = await authority.MutateAsync(
                project.Id,
                0,
                ProjectMutationPatch.GlobalVariableSchema(schema));

            flowResult.Changed.Should().BeFalse();
            flowResult.Diff.HasChanges.Should().BeFalse();
            schemaResult.Changed.Should().BeFalse();
            schemaResult.Diff.HasChanges.Should().BeFalse();
            project.PersistenceRevision.Should().Be(0);
            flowStorage.SaveCount.Should().Be(0);
            await repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
            await runtime.DidNotReceiveWithAnyArgs().TryAcquireMutationLeaseAsync(
                default,
                default!,
                default);
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_StaleRevision_ShouldFailBeforeParticipantReadOrWrite()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("project");
            project.SetPersistenceRevision(4);
            var repository = CreateRepository(project);
            var flowStorage = new RecordingProjectFlowStorage();
            var authority = CreateAuthority(
                repository,
                flowStorage,
                root,
                Substitute.For<IInspectionRuntimeCoordinator>());

            var act = async () => await authority.MutateAsync(
                project.Id,
                3,
                ProjectMutationPatch.Metadata(
                    ProjectPatchValue<string>.Present("stale"),
                    ProjectPatchValue<string?>.Absent()));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("PSV011:*");
            project.Name.Should().Be("project");
            project.PersistenceRevision.Should().Be(4);
            flowStorage.LoadJsonCount.Should().Be(0);
            flowStorage.LoadMetadataCount.Should().Be(0);
            flowStorage.SaveCount.Should().Be(0);
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_WhenRuntimeIsActive_ShouldAllowMetadataButRejectActualFlowAndSchemaChanges()
    {
        var root = CreateTempPath();
        using var runtime = new InspectionRuntimeCoordinator(NullLogger<InspectionRuntimeCoordinator>.Instance);
        try
        {
            var project = new Project("project", "before");
            var flow = CreateFlow("before-flow");
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, SerializeFlow(flow), 0);
            var repository = CreateRepository(project);
            var authority = CreateAuthority(repository, flowStorage, root, runtime);
            (await runtime.TryStartAsync(project.Id, Guid.NewGuid(), CancellationToken.None))
                .Should().Be(StartResult.Success);

            var metadataResult = await authority.MutateAsync(
                project.Id,
                0,
                ProjectMutationPatch.Metadata(
                    ProjectPatchValue<string>.Absent(),
                    ProjectPatchValue<string?>.Present("metadata-while-running")));
            var flowAct = async () => await authority.MutateAsync(
                project.Id,
                1,
                ProjectMutationPatch.FlowOnly(CreateFlow("changed-flow")));
            var schemaAct = async () => await authority.MutateAsync(
                project.Id,
                1,
                ProjectMutationPatch.GlobalVariableSchema(CreateSchema(Guid.NewGuid(), "stats.count")));

            metadataResult.Project.PersistenceRevision.Should().Be(1);
            metadataResult.Project.Description.Should().Be("metadata-while-running");
            await flowAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("PMU001:*");
            await schemaAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("PMU001:*");
            project.PersistenceRevision.Should().Be(1);
            flowStorage.FlowJson.Should().Be(SerializeFlow(flow));
            flowStorage.SaveCount.Should().Be(0);
            project.GlobalVariables.Variables.Should().BeEmpty();
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_MetadataThenSchema_ShouldPreserveAuthoritativeMetadataAndFlow()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("before", "before-description");
            var flow = CreateFlow("authoritative-flow");
            var flowJson = SerializeFlow(flow);
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, flowJson, 0);
            var repository = CreateRepository(project);
            var runtime = CreateAllowingRuntime();
            var authority = CreateAuthority(repository, flowStorage, root, runtime);

            await authority.MutateAsync(
                project.Id,
                0,
                ProjectMutationPatch.Metadata(
                    ProjectPatchValue<string>.Present("authoritative-name"),
                    ProjectPatchValue<string?>.Present("authoritative-description")));
            var schema = CreateSchema(Guid.NewGuid(), "stats.count");
            var result = await authority.MutateAsync(
                project.Id,
                1,
                ProjectMutationPatch.GlobalVariableSchema(schema));

            result.Project.PersistenceRevision.Should().Be(2);
            result.Project.Name.Should().Be("authoritative-name");
            result.Project.Description.Should().Be("authoritative-description");
            result.Project.GlobalVariables.Variables.Should().ContainSingle();
            flowStorage.FlowJson.Should().Be(flowJson);
            flowStorage.SaveCount.Should().Be(0);
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_ConcurrentMetadataAndSchemaAtSameRevision_ShouldCommitOneThenRetryWithoutLostUpdate()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("before", "description");
            var flow = CreateFlow("authoritative-flow");
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, SerializeFlow(flow), 0);
            var repository = CreateRepository(project);
            var authority = CreateAuthority(repository, flowStorage, root, CreateAllowingRuntime());
            var schema = CreateSchema(Guid.NewGuid(), "stats.count");
            var metadataPatch = ProjectMutationPatch.Metadata(
                ProjectPatchValue<string>.Present("metadata-name"),
                ProjectPatchValue<string?>.Absent());
            var schemaPatch = ProjectMutationPatch.GlobalVariableSchema(schema);

            var metadataTask = CaptureAsync(() => authority.MutateAsync(project.Id, 0, metadataPatch));
            var schemaTask = CaptureAsync(() => authority.MutateAsync(project.Id, 0, schemaPatch));
            var outcomes = await Task.WhenAll(metadataTask, schemaTask).WaitAsync(TimeSpan.FromSeconds(3));

            outcomes.Count(item => item.Error == null).Should().Be(1);
            outcomes.Count(item => item.Error?.Message.StartsWith("PSV011:", StringComparison.Ordinal) == true).Should().Be(1);
            project.PersistenceRevision.Should().Be(1);

            if (outcomes[0].Error == null)
            {
                await authority.MutateAsync(project.Id, 1, schemaPatch);
            }
            else
            {
                await authority.MutateAsync(project.Id, 1, metadataPatch);
            }

            project.PersistenceRevision.Should().Be(2);
            project.Name.Should().Be("metadata-name");
            project.GlobalVariables.Variables.Should().ContainSingle(item => item.Name == "stats.count");
            flowStorage.FlowJson.Should().Be(SerializeFlow(flow));
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    [Fact]
    public async Task MutateAsync_ConcurrentFlowAndSchemaAtSameRevision_ShouldCommitOneThenRetryWithoutDeadlockOrLostUpdate()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("authoritative-name", "authoritative-description");
            var initialFlow = CreateFlow("initial-flow");
            var flowStorage = new RecordingProjectFlowStorage();
            flowStorage.Seed(project.Id, SerializeFlow(initialFlow), 0);
            var repository = CreateRepository(project);
            var authority = CreateAuthority(repository, flowStorage, root, CreateAllowingRuntime());
            var schemaPatch = ProjectMutationPatch.GlobalVariableSchema(
                CreateSchema(Guid.NewGuid(), "stats.count"));
            var flowPatch = ProjectMutationPatch.FlowOnly(CreateFlow("changed-flow"));

            var flowTask = CaptureAsync(() => authority.MutateAsync(project.Id, 0, flowPatch));
            var schemaTask = CaptureAsync(() => authority.MutateAsync(project.Id, 0, schemaPatch));
            var outcomes = await Task.WhenAll(flowTask, schemaTask).WaitAsync(TimeSpan.FromSeconds(3));

            outcomes.Count(item => item.Error == null).Should().Be(1);
            outcomes.Count(item => item.Error?.Message.StartsWith("PSV011:", StringComparison.Ordinal) == true).Should().Be(1);

            if (outcomes[0].Error == null)
            {
                await authority.MutateAsync(project.Id, 1, schemaPatch);
            }
            else
            {
                await authority.MutateAsync(project.Id, 1, flowPatch);
            }

            project.PersistenceRevision.Should().Be(2);
            project.Name.Should().Be("authoritative-name");
            project.Description.Should().Be("authoritative-description");
            project.GlobalVariables.Variables.Should().ContainSingle(item => item.Name == "stats.count");
            JsonSerializer.Deserialize<OperatorFlowDto>(flowStorage.FlowJson!, JsonOptions)!.Name.Should().Be("changed-flow");
            flowStorage.SaveCount.Should().Be(1);
        }
        finally
        {
            ResetAndDelete(root);
        }
    }

    private static async Task<MutationOutcome> CaptureAsync(Func<Task<ProjectMutationResult>> mutation)
    {
        try
        {
            return new MutationOutcome(await mutation(), null);
        }
        catch (Exception ex)
        {
            return new MutationOutcome(null, ex);
        }
    }

    private static ProjectMutationAuthority CreateAuthority(
        IProjectRepository repository,
        RecordingProjectFlowStorage flowStorage,
        string transactionRoot,
        IInspectionRuntimeCoordinator runtime,
        IProjectAssetStorage? assets = null)
    {
        var coordinator = new ProjectSaveCoordinator(
            repository,
            flowStorage,
            transactionRoot: transactionRoot,
            projectAssetStorage: assets);
        return new ProjectMutationAuthority(repository, flowStorage, coordinator, runtime, assets);
    }

    private static IProjectRepository CreateRepository(Project project)
    {
        var repository = Substitute.For<IProjectRepository>();
        repository.GetByIdForUpdateAsync(project.Id).Returns(_ => Task.FromResult<Project?>(project));
        repository.GetByIdAsync(project.Id).Returns(_ => Task.FromResult<Project?>(project));
        repository.GetByIdFreshAsync(project.Id).Returns(_ => Task.FromResult<Project?>(project));
        repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);
        return repository;
    }

    private static IInspectionRuntimeCoordinator CreateAllowingRuntime()
    {
        var runtime = Substitute.For<IInspectionRuntimeCoordinator>();
        runtime.TryAcquireMutationLeaseAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<ProjectMutationLease?>(new ProjectMutationLease(
                call.ArgAt<Guid>(0),
                call.ArgAt<string>(1),
                () => ValueTask.CompletedTask)));
        return runtime;
    }

    private static OperatorFlowDto CreateFlow(string name) => new()
    {
        Name = name,
        Operators = [],
        Connections = []
    };

    private static string SerializeFlow(OperatorFlowDto flow) =>
        JsonSerializer.Serialize(flow, JsonOptions);

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, string name) => new()
    {
        Variables =
        [
            new ProjectGlobalVariableDefinition
            {
                Id = variableId,
                Name = name,
                DisplayName = "Count",
                ValueType = ProjectGlobalVariableValueType.Int64,
                InitialValue = JsonSerializer.SerializeToElement(0L),
                ManualWriteAllowed = true
            }
        ]
    };

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "ClearVision.ProjectMutationAuthority.Tests", Guid.NewGuid().ToString("N"));

    private static void ResetAndDelete(string root)
    {
        ProjectSaveCoordinator.ResetStaticStateForTests();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record MutationOutcome(ProjectMutationResult? Result, Exception? Error);

    private sealed class RecordingProjectFlowStorage : IProjectFlowStorage
    {
        public string? FlowJson { get; private set; }

        public ProjectFlowStorageMetadata? Metadata { get; private set; }

        public int SaveCount { get; private set; }

        public int LoadJsonCount { get; private set; }

        public int LoadMetadataCount { get; private set; }

        public void Seed(Guid projectId, string flowJson, long revision)
        {
            FlowJson = flowJson;
            Metadata = CreateMetadata(projectId, flowJson, revision);
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            SaveCount += 1;
            Seed(projectId, flowJson, 0);
            return Task.CompletedTask;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            SaveCount += 1;
            Seed(projectId, flowJson, persistenceRevision);
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId)
        {
            LoadJsonCount += 1;
            return Task.FromResult(FlowJson);
        }

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId)
        {
            LoadMetadataCount += 1;
            return Task.FromResult(Metadata);
        }

        public Task DeleteFlowJsonAsync(Guid projectId)
        {
            FlowJson = null;
            Metadata = null;
            return Task.CompletedTask;
        }

        private static ProjectFlowStorageMetadata CreateMetadata(Guid projectId, string flowJson, long revision) =>
            new(1, projectId, revision, ComputeSha256(flowJson), DateTimeOffset.UtcNow);

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    private sealed class RecordingProjectAssetStorage : IProjectAssetStorage
    {
        public int LoadAssetsCount { get; private set; }

        public int LoadMetadataCount { get; private set; }

        public Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId)
        {
            LoadAssetsCount += 1;
            return Task.FromResult(new ProjectAssetsDto());
        }

        public Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId)
        {
            LoadMetadataCount += 1;
            return Task.FromResult<ProjectAssetStorageMetadata?>(null);
        }

        public Task SaveAssetsAsync(
            Guid projectId,
            ProjectAssetsDto assets,
            long persistenceRevision,
            Guid saveId,
            string assetsHash) => Task.CompletedTask;

        public Task DeleteAssetsAsync(Guid projectId) => Task.CompletedTask;
    }
}
