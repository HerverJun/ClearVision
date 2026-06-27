using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.Services;
using ClearVision.Product.Runtime;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class ProjectGlobalVariableEndpointsTests
{
    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldUpdateReadWriteAndResetWhenIdle()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));

        var updated = CreateSchema(variableId, 5, manualWriteAllowed: true);
        using var updateResponse = await host.Client.PutAsJsonAsync($"/api/projects/{host.Project.Id}/global-variables", updated);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await updateResponse.Content.ReadAsStringAsync());

        using var valuesResponse = await host.Client.GetAsync($"/api/projects/{host.Project.Id}/global-variable-values");
        valuesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var values = await valuesResponse.Content.ReadFromJsonAsync<JsonElement>();
        values.EnumerateArray().Single().GetProperty("value").GetString().Should().Be("5");

        using var writeResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L });
        writeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetValue(variableId, out var written).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(written).Should().Be(8L);

        using var resetOneResponse = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            null);
        resetOneResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetValue(variableId, out var resetOne).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(resetOne).Should().Be(5L);

        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables)
            .SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var resetResponse = await host.Client.PostAsync($"/api/projects/{host.Project.Id}/global-variable-values/reset", null);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetValue(variableId, out var reset).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(reset).Should().Be(5L);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldRejectInvalidSchemaAndKeepOldSession()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            storedFlowJson: JsonSerializer.Serialize(new OperatorFlowDto
            {
                Name = "empty",
                Operators = [],
                Connections = []
            }));
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);
        var invalid = CreateSchema(variableId, 5, manualWriteAllowed: true);
        invalid.Variables.Add(new ProjectGlobalVariableDefinition
        {
            Id = Guid.NewGuid(),
            Name = "stats.count",
            DisplayName = "Duplicate",
            ValueType = ProjectGlobalVariableValueType.Int64,
            InitialValue = JsonSerializer.SerializeToElement(6L),
            ManualWriteAllowed = true
        });

        using var response = await host.Client.PutAsJsonAsync($"/api/projects/{host.Project.Id}/global-variables", invalid);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_WhenStatePersistFails_ShouldReturnGv030AndRollback()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            stateStore: new FailingProjectVariableStateStore());
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            CreateSchema(variableId, 5, manualWriteAllowed: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("GV030");
        body.GetProperty("error").GetString().Should().Contain("state failed");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
        await host.Repository.Received(2).UpdateAsync(host.Project);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldRejectForbiddenWrite()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: false));

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldRejectForbiddenReset()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: false));

        using var resetOne = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            null);
        using var resetAll = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            null);

        resetOne.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resetAll.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var resetOneBody = await resetOne.Content.ReadFromJsonAsync<JsonElement>();
        var resetAllBody = await resetAll.Content.ReadFromJsonAsync<JsonElement>();
        resetOneBody.GetProperty("code").GetString().Should().Be("GV030");
        resetAllBody.GetProperty("code").GetString().Should().Be("GV030");
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldRejectMutationsWhileProjectIsRunning()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: RuntimeStatus.Running);

        using var schemaResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            CreateSchema(variableId, 5, manualWriteAllowed: true));
        using var writeResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L });
        using var resetResponse = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            null);
        using var resetOneResponse = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            null);

        schemaResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        writeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resetOneResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(1L);
    }

    [Theory]
    [InlineData(RuntimeStatus.Starting)]
    [InlineData(RuntimeStatus.Running)]
    [InlineData(RuntimeStatus.Stopping)]
    public async Task FlowPut_WhenRuntimeBusy_ShouldRejectWithoutPartialUpdate(RuntimeStatus status)
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: status);
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/flow",
            new UpdateFlowRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("GV031");
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task ProjectDelete_ShouldRejectWhileProjectIsRunning()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: RuntimeStatus.Running);

        using var response = await host.Client.DeleteAsync($"/api/projects/{host.Project.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task RuntimePackageExport_ShouldRejectWhileProjectIsRunning()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: RuntimeStatus.Running);

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/runtime-package/export",
            new ApiEndpoints.ExportRuntimePackageRequest
            {
                RegisterForStationDeployment = false
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("GV031");
    }

    [Theory]
    [InlineData(RuntimeStatus.Starting)]
    [InlineData(RuntimeStatus.Running)]
    [InlineData(RuntimeStatus.Stopping)]
    public async Task ProjectPut_WhenRuntimeBusyAndGlobalVariablesProvided_ShouldRejectWithoutPartialUpdate(RuntimeStatus status)
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: status);
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed",
                GlobalVariables = CreateSchema(variableId, 5, manualWriteAllowed: true)
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        host.Project.Name.Should().Be("demo");
        host.Project.Description.Should().BeNull();
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Theory]
    [InlineData(RuntimeStatus.Starting)]
    [InlineData(RuntimeStatus.Running)]
    [InlineData(RuntimeStatus.Stopping)]
    public async Task ProjectPut_WhenRuntimeBusyAndFlowProvided_ShouldRejectWithoutPartialUpdate(RuntimeStatus status)
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: status);
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed",
                Flow = new OperatorFlowDto
                {
                    Name = "ChangedFlow",
                    Operators = [],
                    Connections = []
                }
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("GV031");
        host.Project.Name.Should().Be("demo");
        host.Project.Description.Should().BeNull();
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task ProjectPut_WhenRuntimeRunningAndGlobalVariablesOmitted_ShouldAllowMetadataUpdate()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            status: RuntimeStatus.Running);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Project.Name.Should().Be("renamed");
        host.Project.Description.Should().Be("changed");
        await host.Repository.Received(1).UpdateAsync(host.Project);
    }

    [Fact]
    public async Task ProjectPut_WhenIdleAndGlobalVariablesProvided_ShouldUpdateAndRefreshSession()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed",
                GlobalVariables = CreateSchema(variableId, 5, manualWriteAllowed: true)
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Project.Name.Should().Be("renamed");
        host.Project.Description.Should().Be("changed");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(5L);
        var newSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        newSession.Should().NotBeSameAs(oldSession);
        newSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
    }

    private static ProjectGlobalVariableSchema CreateSchema(Guid variableId, long initialValue, bool manualWriteAllowed)
    {
        return new ProjectGlobalVariableSchema
        {
            Variables =
            [
                new ProjectGlobalVariableDefinition
                {
                    Id = variableId,
                    Name = "stats.count",
                    DisplayName = "Count",
                    ValueType = ProjectGlobalVariableValueType.Int64,
                    InitialValue = JsonSerializer.SerializeToElement(initialValue),
                    ManualWriteAllowed = manualWriteAllowed
                }
            ]
        };
    }

    private sealed class ProjectGlobalVariableEndpointHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ProjectGlobalVariableEndpointHost(
            WebApplication app,
            Project project,
            ProjectVariableSessionRegistry registry,
            IProjectRepository repository)
        {
            _app = app;
            Project = project;
            Registry = registry;
            Repository = repository;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public Project Project { get; }

        public ProjectVariableSessionRegistry Registry { get; }

        public IProjectRepository Repository { get; }

        public static async Task<ProjectGlobalVariableEndpointHost> CreateAsync(
            ProjectGlobalVariableSchema schema,
            string? storedFlowJson = null,
            RuntimeStatus? status = null,
            IProjectVariableStateStore? stateStore = null)
        {
            var project = new Project("demo");
            project.UpdateGlobalVariables(schema);
            var repository = Substitute.For<IProjectRepository>();
            repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);

            var storage = Substitute.For<IProjectFlowStorage>();
            storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(storedFlowJson));

            var coordinator = Substitute.For<IInspectionRuntimeCoordinator>();
            if (status.HasValue)
            {
                coordinator.GetState(project.Id).Returns(new RuntimeState
                {
                    ProjectId = project.Id,
                    SessionId = Guid.NewGuid(),
                    Status = status.Value,
                    StartedAt = DateTime.UtcNow
                });
            }

            coordinator
                .TryAcquireMutationLeaseAsync(project.Id, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<ProjectMutationLease?>(
                    status is RuntimeStatus.Starting or RuntimeStatus.Running or RuntimeStatus.Stopping
                        ? null
                        : new ProjectMutationLease(project.Id, "test", () => ValueTask.CompletedTask)));

            var registry = new ProjectVariableSessionRegistry(stateStore);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(repository);
            builder.Services.AddSingleton(storage);
            builder.Services.AddSingleton<IOperatorFactory>(new OperatorFactory());
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton(coordinator);
            builder.Services.AddSingleton<ILogger<ProjectService>>(NullLogger<ProjectService>.Instance);
            builder.Services.AddScoped(sp => new RuntimePackageExporter(
                sp.GetRequiredService<IOperatorFactory>(),
                NullLogger<RuntimePackageExporter>.Instance));
            builder.Services.AddSingleton(sp => new StationPackageStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationPackageStore>.Instance));
            builder.Services.AddScoped<ProjectService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = "test-admin",
                    Username = "test-admin",
                    Role = UserRole.Admin.ToString(),
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
                await next();
            });
            MapProjectEndpoints(app);
            await app.StartAsync();
            return new ProjectGlobalVariableEndpointHost(app, project, registry, repository);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static void MapProjectEndpoints(IEndpointRouteBuilder app)
    {
        var method = typeof(ApiEndpoints).GetMethod(
            "MapProjectEndpoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [app]);
    }

    private sealed class FailingProjectVariableStateStore : IProjectVariableStateStore
    {
        public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema) => [];

        public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
        {
            throw new IOException("state failed");
        }

        public void Delete(string scopeId)
        {
        }
    }
}
