using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;

namespace ClearVision.Product.Tests.Services;

public sealed class ProjectSaveCoordinatorTests
{
    [Fact]
    public async Task SaveExistingProjectAsync_WhenFlowApplyFailsOnceAfterCommitIntent_ShouldRecoverForward()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var previousFlow = SerializeFlow("old");
            var nextFlow = CreateFlow("new");
            var nextFlowJson = JsonSerializer.Serialize(nextFlow);
            await flowStorage.SaveFlowJsonAsync(project.Id, previousFlow, 0);
            flowStorage.ResetCounts();
            flowStorage.FailRevisionedSaves = 1;
            var sut = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);

            var result = await sut.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                "updated",
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                previousFlow,
                nextFlow,
                nextFlowJson));

            result.Changed.Should().BeTrue();
            project.Name.Should().Be("renamed");
            project.Description.Should().Be("updated");
            project.PersistenceRevision.Should().Be(1);
            flowStorage.FlowJson.Should().Be(nextFlowJson);
            flowStorage.Metadata!.PersistenceRevision.Should().Be(1);
            flowStorage.RevisionedSaveCount.Should().Be(2);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenAfterCommitIntentFailsOnce_ShouldRecoverImmediatelyAndReturnSuccess()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var previousFlow = SerializeFlow("old");
            var nextFlow = CreateFlow("new");
            var nextFlowJson = JsonSerializer.Serialize(nextFlow);
            await flowStorage.SaveFlowJsonAsync(project.Id, previousFlow, 0);
            var crash = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterCommitIntent, failAlways: false);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: crash);

            var result = await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                previousFlow,
                nextFlow,
                nextFlowJson));

            result.Changed.Should().BeTrue();
            project.Name.Should().Be("renamed");
            project.PersistenceRevision.Should().Be(1);
            flowStorage.FlowJson.Should().Be(nextFlowJson);
            flowStorage.Metadata!.PersistenceRevision.Should().Be(1);
            Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task EnsureProjectAvailable_WhenCommittedRecoveryFails_ShouldFenceProjectAccess()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var failure = new ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply, failAlways: true);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: failure);

            var act = async () => await coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*injected*");
            coordinator.Invoking(item => item.EnsureProjectAvailable(project.Id))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*PSV001*");

            var service = new ProjectService(repository, flowStorage, new OperatorFactory(), null, null, coordinator);
            var read = async () => await service.GetByIdAsync(project.Id);
            await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV001*");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task SaveExistingProjectAsync_WhenExpectedRevisionIsStale_ShouldFailBeforeCommitIntent()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            project.SetPersistenceRevision(1);
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var sut = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root);

            var act = async () => await sut.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                0,
                "stale",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PSV011*");
            project.Name.Should().Be("demo");
            project.PersistenceRevision.Should().Be(1);
            Directory.Exists(root).Should().BeFalse();
            sut.Invoking(item => item.EnsureProjectAvailable(project.Id)).Should().NotThrow();
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task AcquireProjectAccessAsync_WhenSaveIsInProgress_ShouldWaitForProjectGate()
    {
        var root = CreateTempPath();
        try
        {
            var project = new Project("demo");
            var repository = new InMemoryProjectRepository(project);
            var flowStorage = new InMemoryProjectFlowStorage();
            var blocker = new BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint.AfterProjectApply);
            var coordinator = new ProjectSaveCoordinator(repository, flowStorage, transactionRoot: root, failureInjector: blocker);

            var saveTask = coordinator.SaveExistingProjectAsync(new ProjectSaveRequest(
                project,
                project.PersistenceRevision,
                "renamed",
                null,
                new ProjectGlobalVariableSchema(),
                new ProjectGlobalVariableSchema(),
                null,
                null,
                null));
            await blocker.WaitUntilHitAsync();

            var accessTask = coordinator.AcquireProjectAccessAsync(project.Id);
            await Task.Delay(75);
            accessTask.IsCompleted.Should().BeFalse();

            blocker.Release();
            await saveTask;
            await using var access = await accessTask;
            project.Name.Should().Be("renamed");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static OperatorFlowDto CreateFlow(string name) => new()
    {
        Name = name,
        Operators = [],
        Connections = []
    };

    private static string SerializeFlow(string name) => JsonSerializer.Serialize(CreateFlow(name));

    private static string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), "ClearVision.ProjectSaveCoordinator.Tests", Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ThrowingProjectSaveFailureInjector : IProjectSaveFailureInjector
    {
        private readonly ProjectSaveFailurePoint _point;
        private readonly bool _failAlways;
        private bool _hasFailed;

        public ThrowingProjectSaveFailureInjector(ProjectSaveFailurePoint point, bool failAlways)
        {
            _point = point;
            _failAlways = failAlways;
        }

        public Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest)
        {
            if (point == _point && (_failAlways || !_hasFailed))
            {
                _hasFailed = true;
                throw new InvalidOperationException($"injected failure at {point}");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BlockingProjectSaveFailureInjector : IProjectSaveFailureInjector
    {
        private readonly ProjectSaveFailurePoint _point;
        private readonly TaskCompletionSource _hit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingProjectSaveFailureInjector(ProjectSaveFailurePoint point)
        {
            _point = point;
        }

        public Task WaitUntilHitAsync() => _hit.Task;

        public void Release() => _release.TrySetResult();

        public async Task OnPointAsync(ProjectSaveFailurePoint point, ProjectSaveManifest manifest)
        {
            if (point != _point)
            {
                return;
            }

            _hit.TrySetResult();
            await _release.Task;
        }
    }

    private sealed class InMemoryProjectFlowStorage : IProjectFlowStorage
    {
        public string? FlowJson { get; private set; }

        public ProjectFlowStorageMetadata? Metadata { get; private set; }

        public int FailRevisionedSaves { get; set; }

        public int RevisionedSaveCount { get; private set; }

        public void ResetCounts()
        {
            RevisionedSaveCount = 0;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            FlowJson = flowJson;
            Metadata = CreateMetadata(projectId, flowJson, 0);
            return Task.CompletedTask;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            RevisionedSaveCount += 1;
            if (FailRevisionedSaves > 0)
            {
                FailRevisionedSaves -= 1;
                throw new IOException("flow save failed");
            }

            FlowJson = flowJson;
            Metadata = CreateMetadata(projectId, flowJson, persistenceRevision);
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId) => Task.FromResult(FlowJson);

        public Task DeleteFlowJsonAsync(Guid projectId)
        {
            FlowJson = null;
            Metadata = null;
            return Task.CompletedTask;
        }

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId) => Task.FromResult(Metadata);

        private static ProjectFlowStorageMetadata CreateMetadata(Guid projectId, string flowJson, long revision) =>
            new(1, projectId, revision, ComputeSha256(flowJson), DateTimeOffset.UtcNow);

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    private sealed class InMemoryProjectRepository : IProjectRepository
    {
        private readonly Project _project;

        public InMemoryProjectRepository(Project project)
        {
            _project = project;
        }

        public Task<Project?> GetByIdAsync(Guid id) => Task.FromResult(id == _project.Id ? _project : null);

        public Task<IEnumerable<Project>> GetAllAsync() => Task.FromResult<IEnumerable<Project>>([_project]);

        public Task<IEnumerable<Project>> FindAsync(Expression<Func<Project, bool>> predicate) =>
            Task.FromResult<IEnumerable<Project>>([_project]);

        public Task<Project> AddAsync(Project entity) => Task.FromResult(entity);

        public Task UpdateAsync(Project entity) => Task.CompletedTask;

        public Task DeleteAsync(Project entity) => Task.CompletedTask;

        public Task DeleteByIdAsync(Guid id) => Task.CompletedTask;

        public Task<bool> ExistsAsync(Guid id) => Task.FromResult(id == _project.Id);

        public Task<Project?> GetByNameAsync(string name) =>
            Task.FromResult(string.Equals(name, _project.Name, StringComparison.Ordinal) ? _project : null);

        public Task<IEnumerable<Project>> GetRecentlyOpenedAsync(int count = 10) =>
            Task.FromResult<IEnumerable<Project>>([_project]);

        public Task<IEnumerable<Project>> SearchAsync(string keyword) =>
            Task.FromResult<IEnumerable<Project>>([_project]);

        public Task<Project?> GetWithFlowAsync(Guid id) => GetByIdAsync(id);

        public Task UpdateFlowAsync(Project project) => Task.CompletedTask;
    }
}
