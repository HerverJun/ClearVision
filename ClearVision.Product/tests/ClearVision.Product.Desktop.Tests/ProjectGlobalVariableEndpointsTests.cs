using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

[Collection(ProjectSaveCoordinatorTestCollections.ProjectSaveCoordinatorState)]
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
    public async Task ProjectGlobalVariableEndpoints_WhenManualWriteExpectedVersionIsStale_ShouldReturnGv025AndKeepValue()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var session = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        session.TryGetSnapshot(variableId, out var initial).Should().BeTrue();
        initial.Version.Should().Be(0);

        using var firstWrite = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L, expectedVersion = initial.Version });
        firstWrite.StatusCode.Should().Be(HttpStatusCode.OK);

        using var staleWrite = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 11L, expectedVersion = initial.Version });

        staleWrite.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await staleWrite.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("GV025");
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetSnapshot(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current.Value).Should().Be(8L);
        current.Version.Should().Be(1);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_WhenResetExpectedVersionIsStale_ShouldReturnGv025AndKeepValue()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var session = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        session.TryGetSnapshot(variableId, out var initial).Should().BeTrue();
        initial.Version.Should().Be(0);

        using var write = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L, expectedVersion = initial.Version });
        write.StatusCode.Should().Be(HttpStatusCode.OK);

        using var resetOne = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            new { expectedVersion = initial.Version });
        using var resetAll = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            new { expectedVersions = new Dictionary<Guid, long> { [variableId] = initial.Version } });

        resetOne.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resetAll.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var resetOneBody = await resetOne.Content.ReadFromJsonAsync<JsonElement>();
        var resetAllBody = await resetAll.Content.ReadFromJsonAsync<JsonElement>();
        resetOneBody.GetProperty("code").GetString().Should().Be("GV025");
        resetAllBody.GetProperty("code").GetString().Should().Be("GV025");
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetSnapshot(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current.Value).Should().Be(8L);
        current.Version.Should().Be(1);
    }

    [Fact]
    public async Task ProjectGlobalVariableEndpoints_WhenResetAllSucceeds_ShouldAdvanceVersion()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var session = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        session.TryGetSnapshot(variableId, out var initial).Should().BeTrue();
        initial.Version.Should().Be(0);

        using var write = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 8L, expectedVersion = initial.Version });
        write.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetSnapshot(variableId, out var written).Should().BeTrue();
        written.Version.Should().Be(1);

        using var resetAll = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            new { expectedVersions = new Dictionary<Guid, long> { [variableId] = written.Version } });

        resetAll.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).TryGetSnapshot(variableId, out var reset).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(reset.Value).Should().Be(1L);
        reset.Version.Should().Be(2);
        reset.UpdatedBy.Should().Be(ProjectVariableUpdatedBy.Reset);
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
    public async Task ProjectGlobalVariableEndpoints_WhenStatePersistFailsAfterCommitIntent_ShouldReturnPsv012AndFence()
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
        body.GetProperty("code").GetString().Should().Be("PSV012");
        body.GetProperty("error").GetString().Should().Contain("state failed");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(5L);
        host.Project.PersistenceRevision.Should().Be(1);
        host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables).Should().BeSameAs(oldSession);
        oldSession.TryGetValue(variableId, out var current).Should().BeTrue();
        ProjectVariableValueConverter.ToObject(current).Should().Be(9L);
        await host.Repository.Received(1).UpdateAsync(host.Project);

        using var projectRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}");
        using var schemaRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}/global-variables");
        using var valuesRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}/global-variable-values");
        using var valueWrite = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 10L });
        using var resetOne = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            null);
        using var resetAll = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            null);
        using var export = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/runtime-package/export",
            new ApiEndpoints.ExportRuntimePackageRequest
            {
                RegisterForStationDeployment = false
            });
        projectRead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        schemaRead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        valuesRead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        valueWrite.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resetOne.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resetAll.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        export.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var projectBody = await projectRead.Content.ReadFromJsonAsync<JsonElement>();
        var readBody = await schemaRead.Content.ReadFromJsonAsync<JsonElement>();
        var valuesBody = await valuesRead.Content.ReadFromJsonAsync<JsonElement>();
        var writeBody = await valueWrite.Content.ReadFromJsonAsync<JsonElement>();
        var resetOneBody = await resetOne.Content.ReadFromJsonAsync<JsonElement>();
        var resetAllBody = await resetAll.Content.ReadFromJsonAsync<JsonElement>();
        var exportBody = await export.Content.ReadFromJsonAsync<JsonElement>();
        projectBody.GetProperty("code").GetString().Should().Be("PSV001");
        readBody.GetProperty("code").GetString().Should().Be("PSV001");
        valuesBody.GetProperty("code").GetString().Should().Be("PSV001");
        writeBody.GetProperty("code").GetString().Should().Be("PSV001");
        resetOneBody.GetProperty("code").GetString().Should().Be("PSV001");
        resetAllBody.GetProperty("code").GetString().Should().Be("PSV001");
        exportBody.GetProperty("code").GetString().Should().Be("PSV001");
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

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
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

    [Fact]
    public async Task ProjectPut_WhenExpectedPersistenceRevisionIsStale_ShouldReturnPsv011ConflictWithoutWrite()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        host.Project.SetPersistenceRevision(2);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "stale",
                ExpectedPersistenceRevision = 1
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PSV011");
        host.Project.Name.Should().Be("demo");
        host.Project.Description.Should().BeNull();
        host.Project.PersistenceRevision.Should().Be(2);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
        await host.FlowStorage.DidNotReceive().SaveFlowJsonAsync(host.Project.Id, Arg.Any<string>(), Arg.Any<long>());
    }

    [Fact]
    public async Task ProjectPut_WhenFlowAndGlobalVariablesProvided_ShouldPersistThroughSingleProjectSave()
    {
        var variableId = Guid.NewGuid();
        var flowStorage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            flowStorage: flowStorage);
        var nextSchema = CreateSchema(variableId, 5, manualWriteAllowed: true);
        var nextFlow = new OperatorFlowDto
        {
            Name = "MainFlow",
            Operators =
            [
                new OperatorDto
                {
                    Id = Guid.NewGuid(),
                    Name = "Threshold",
                    Type = OperatorType.Thresholding,
                    Parameters =
                    [
                        new ParameterDto
                        {
                            Id = Guid.NewGuid(),
                            Name = "Threshold",
                            DisplayName = "Threshold",
                            DataType = "int",
                            Value = 21
                        }
                    ]
                }
            ],
            Connections = []
        };

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed",
                ExpectedPersistenceRevision = 0,
                Flow = nextFlow,
                GlobalVariables = nextSchema
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ProjectDto>();
        body.Should().NotBeNull();
        body!.PersistenceRevision.Should().Be(1);
        body.Flow.Should().NotBeNull();
        host.Project.Name.Should().Be("renamed");
        host.Project.Description.Should().Be("changed");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(5L);
        await host.Repository.Received(1).UpdateAsync(host.Project);
        flowStorage.SaveCount.Should().Be(1);
        flowStorage.LastPersistenceRevision.Should().Be(1);
        flowStorage.LastSavedFlowJson.Should().Contain("Threshold");
    }

    [Fact]
    public async Task ProjectPut_WhenNothingChanges_ShouldNotIncrementPersistenceRevision()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "demo",
                Description = null,
                ExpectedPersistenceRevision = 0
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ProjectDto>();
        body.Should().NotBeNull();
        body!.PersistenceRevision.Should().Be(0);
        host.Project.PersistenceRevision.Should().Be(0);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
        await host.FlowStorage.DidNotReceive().SaveFlowJsonAsync(host.Project.Id, Arg.Any<string>(), Arg.Any<long>());
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
        private readonly string? _ownedStateStoreRoot;

        private ProjectGlobalVariableEndpointHost(
            WebApplication app,
            Project project,
            ProjectVariableSessionRegistry registry,
            IProjectRepository repository,
            IProjectFlowStorage flowStorage,
            string? ownedStateStoreRoot)
        {
            _app = app;
            _ownedStateStoreRoot = ownedStateStoreRoot;
            Project = project;
            Registry = registry;
            Repository = repository;
            FlowStorage = flowStorage;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public Project Project { get; }

        public ProjectVariableSessionRegistry Registry { get; }

        public IProjectRepository Repository { get; }

        public IProjectFlowStorage FlowStorage { get; }

        public static async Task<ProjectGlobalVariableEndpointHost> CreateAsync(
            ProjectGlobalVariableSchema schema,
            string? storedFlowJson = null,
            RuntimeStatus? status = null,
            IProjectVariableStateStore? stateStore = null,
            IProjectFlowStorage? flowStorage = null)
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            var project = new Project("demo");
            project.UpdateGlobalVariables(schema);
            var repository = Substitute.For<IProjectRepository>();
            repository.GetByIdAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.GetByIdForUpdateAsync(project.Id).Returns(Task.FromResult<Project?>(project));
            repository.UpdateAsync(Arg.Any<Project>()).Returns(Task.CompletedTask);

            var storage = flowStorage ?? Substitute.For<IProjectFlowStorage>();
            if (flowStorage == null)
            {
                storage.LoadFlowJsonAsync(project.Id).Returns(Task.FromResult<string?>(storedFlowJson));
            }
            else if (!string.IsNullOrWhiteSpace(storedFlowJson))
            {
                await storage.SaveFlowJsonAsync(project.Id, storedFlowJson, project.PersistenceRevision);
            }

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

            var ownedStateStoreRoot = stateStore == null
                ? Path.Combine(Path.GetTempPath(), "ClearVision.ProjectGlobalVariableEndpointTests", Guid.NewGuid().ToString("N"))
                : null;
            stateStore ??= new JsonFileProjectVariableStateStore(ownedStateStoreRoot!);
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
            builder.Services.AddScoped<ProjectSaveCoordinator>();
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
            return new ProjectGlobalVariableEndpointHost(app, project, registry, repository, storage, ownedStateStoreRoot);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            ProjectSaveCoordinator.ResetStaticStateForTests();
            if (_ownedStateStoreRoot != null && Directory.Exists(_ownedStateStoreRoot))
            {
                Directory.Delete(_ownedStateStoreRoot, recursive: true);
            }
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

    private sealed class RecordingProjectFlowStorage : IProjectFlowStorage
    {
        private string? _flowJson;
        private ProjectFlowStorageMetadata? _metadata;

        public string? LastSavedFlowJson { get; private set; }

        public long LastPersistenceRevision { get; private set; }

        public int SaveCount { get; private set; }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson)
        {
            Save(projectId, flowJson, 0);
            return Task.CompletedTask;
        }

        public Task SaveFlowJsonAsync(Guid projectId, string flowJson, long persistenceRevision)
        {
            SaveCount += 1;
            Save(projectId, flowJson, persistenceRevision);
            return Task.CompletedTask;
        }

        public Task<string?> LoadFlowJsonAsync(Guid projectId) => Task.FromResult(_flowJson);

        public Task DeleteFlowJsonAsync(Guid projectId)
        {
            _flowJson = null;
            _metadata = null;
            LastSavedFlowJson = null;
            LastPersistenceRevision = 0;
            return Task.CompletedTask;
        }

        public Task<ProjectFlowStorageMetadata?> LoadMetadataAsync(Guid projectId) => Task.FromResult(_metadata);

        private void Save(Guid projectId, string flowJson, long persistenceRevision)
        {
            _flowJson = flowJson;
            LastSavedFlowJson = flowJson;
            LastPersistenceRevision = persistenceRevision;
            _metadata = new ProjectFlowStorageMetadata(
                1,
                projectId,
                persistenceRevision,
                ComputeSha256(flowJson),
                DateTimeOffset.UtcNow);
        }

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
