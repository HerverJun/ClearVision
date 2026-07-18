using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Decisions;
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
    public async Task ProjectWriteEndpoints_ShouldRejectOperatorButAllowProjectReads()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            role: UserRole.Operator);

        using var projectRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}");
        using var schemaRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}/global-variables");
        using var valuesRead = await host.Client.GetAsync($"/api/projects/{host.Project.Id}/global-variable-values");
        using var create = await host.Client.PostAsJsonAsync("/api/projects", new CreateProjectRequest { Name = "blocked" });
        using var update = await host.Client.PutAsJsonAsync($"/api/projects/{host.Project.Id}", new UpdateProjectRequest { Name = "blocked" });
        using var delete = await host.Client.DeleteAsync($"/api/projects/{host.Project.Id}");
        using var flow = await host.Client.PutAsJsonAsync($"/api/projects/{host.Project.Id}/flow", new UpdateFlowRequest());
        using var schemaWrite = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            CreateSchema(variableId, 2, manualWriteAllowed: true));
        using var valueWrite = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}",
            new { value = 3L });
        using var resetOne = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/{variableId}/reset",
            null);
        using var resetAll = await host.Client.PostAsync(
            $"/api/projects/{host.Project.Id}/global-variable-values/reset",
            null);
        using var export = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/runtime-package/export",
            new ApiEndpoints.ExportRuntimePackageRequest { RegisterForStationDeployment = false });

        projectRead.StatusCode.Should().Be(HttpStatusCode.OK);
        schemaRead.StatusCode.Should().Be(HttpStatusCode.OK);
        valuesRead.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        flow.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        schemaWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        valueWrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resetOne.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resetAll.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RuntimePackageExport_ShouldRejectEngineer()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            role: UserRole.Engineer);

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/runtime-package/export",
            new ApiEndpoints.ExportRuntimePackageRequest { RegisterForStationDeployment = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
    public async Task FlowPut_WhenExpectedRevisionMatches_ShouldPersistAndReturnNextRevision()
    {
        var variableId = Guid.NewGuid();
        var flowStorage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            flowStorage: flowStorage);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/flow",
            new UpdateFlowRequest
            {
                Name = "EndpointFlow",
                ExpectedPersistenceRevision = 0,
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput
                    }
                ],
                Connections = []
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("persistenceRevision").GetInt64().Should().Be(1);
        host.Project.PersistenceRevision.Should().Be(1);
        flowStorage.LastPersistenceRevision.Should().Be(1);
        DeserializeFlow(flowStorage.LastSavedFlowJson!).Name.Should().Be("EndpointFlow");
    }

    [Fact]
    public async Task FlowPut_WhenExpectedRevisionIsStale_ShouldReturnConflictWithoutWriting()
    {
        var variableId = Guid.NewGuid();
        var flowStorage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            flowStorage: flowStorage);
        host.Project.SetPersistenceRevision(2);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/flow",
            new UpdateFlowRequest
            {
                Name = "StaleEndpointFlow",
                ExpectedPersistenceRevision = 1,
                Operators = [],
                Connections = []
            });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PROJECT_REVISION_CONFLICT");
        body.GetProperty("compatibilityCode").GetString().Should().Be("PSV011");
        body.GetProperty("error").GetString().Should().Contain("Refresh and retry");
        body.GetProperty("expectedRevision").GetInt64().Should().Be(1);
        body.GetProperty("actualRevision").GetInt64().Should().Be(2);
        host.Project.PersistenceRevision.Should().Be(2);
        flowStorage.SaveCount.Should().Be(0);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task FlowPut_WhenNameIsOmitted_ShouldPreserveExistingFlowName()
    {
        var variableId = Guid.NewGuid();
        var flowStorage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            storedFlowJson: SerializeFlow(new OperatorFlowDto { Name = "ExistingEndpointFlow" }),
            flowStorage: flowStorage,
            databaseFlowName: "DbEndpointFlow");

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/flow",
            new UpdateFlowRequest
            {
                ExpectedPersistenceRevision = 0,
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput
                    }
                ],
                Connections = []
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        DeserializeFlow(flowStorage.LastSavedFlowJson!).Name.Should().Be("ExistingEndpointFlow");
    }

    [Fact]
    public async Task FlowPut_WhenNameIsOmittedAndStoredFlowMissing_ShouldPreserveDatabaseFlowName()
    {
        var variableId = Guid.NewGuid();
        var flowStorage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            flowStorage: flowStorage,
            databaseFlowName: "DbOnlyFlow");

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/flow",
            new UpdateFlowRequest
            {
                ExpectedPersistenceRevision = 0,
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput
                    }
                ],
                Connections = []
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        DeserializeFlow(flowStorage.LastSavedFlowJson!).Name.Should().Be("DbOnlyFlow");
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

    [Fact]
    public async Task RuntimePackageExport_WhenProjectAssetMetadataRevisionMismatches_ShouldReturnBadRequest()
    {
        var variableId = Guid.NewGuid();
        var assetStorage = new MutableProjectAssetStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true),
            storedFlowJson: CreateResultOnlyFlowJson(),
            assetStorage: assetStorage);
        assetStorage.Assets = CreateProjectAssets("endpoint-calibration", host.Project.PersistenceRevision);
        assetStorage.Metadata = new ProjectAssetStorageMetadata(
            SchemaVersion: 1,
            ProjectId: host.Project.Id,
            PersistenceRevision: host.Project.PersistenceRevision + 1,
            AssetsHash: ProjectAssetJson.ComputeAssetsHash(assetStorage.Assets),
            SaveId: Guid.NewGuid(),
            SavedAtUtc: DateTimeOffset.UtcNow);

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/runtime-package/export",
            new ApiEndpoints.ExportRuntimePackageRequest
            {
                RegisterForStationDeployment = false
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Contain("RPA003");
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
        body.GetProperty("code").GetString().Should().Be("PROJECT_MUTATION_CONFLICT");
        body.GetProperty("compatibilityCode").GetString().Should().Be("GV031");
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
    public async Task ProjectPut_WhenExpectedPersistenceRevisionIsStale_ShouldReturnStructuredConflictWithoutWrite()
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
        body.GetProperty("code").GetString().Should().Be("PROJECT_REVISION_CONFLICT");
        body.GetProperty("compatibilityCode").GetString().Should().Be("PSV011");
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

    [Fact]
    public async Task ProjectPut_GetPutGetGolden_ShouldPreserveFalsyNullableRoiStructureAndOpaqueFields()
    {
        var variableId = Guid.NewGuid();
        var factory = new OperatorFactory();
        var image = CreateGoldenOperator(factory, OperatorType.ImageAcquisition, "Image", 20, 30);
        var roi = CreateGoldenOperator(factory, OperatorType.RoiManager, "ROI", 260, 30);
        var removed = CreateGoldenOperator(factory, OperatorType.Thresholding, "Removed", 500, 30);
        var caliper = CreateGoldenOperator(factory, OperatorType.CaliperTool, "Caliper", 500, 220);
        image.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["futureOperatorField"] = JsonSerializer.SerializeToElement(new { keep = true })
        };
        image.OutputPorts[0].ExtensionData = new Dictionary<string, JsonElement>
        {
            ["futurePortField"] = JsonSerializer.SerializeToElement("port-opaque")
        };
        image.Parameters[0].ExtensionData = new Dictionary<string, JsonElement>
        {
            ["futureParameterField"] = JsonSerializer.SerializeToElement("parameter-opaque")
        };
        var imageOutput = image.OutputPorts.First();
        var roiImageInput = roi.InputPorts.First(port => port.DataType == PortDataType.Image);
        var originalConnection = new OperatorConnectionDto
        {
            Id = Guid.NewGuid(),
            SourceOperatorId = image.Id,
            SourcePortId = imageOutput.Id,
            TargetOperatorId = roi.Id,
            TargetPortId = roiImageInput.Id,
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureConnectionField"] = JsonSerializer.SerializeToElement("old-connection")
            }
        };
        var initialFlow = new OperatorFlowDto
        {
            Id = Guid.NewGuid(),
            Name = "G5 Golden",
            Operators = [image, roi, removed, caliper],
            Connections = [originalConnection],
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureFlowField"] = JsonSerializer.SerializeToElement(new { mode = "preserve" })
            }
        };
        var storage = new RecordingProjectFlowStorage();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 7, manualWriteAllowed: true),
            storedFlowJson: JsonSerializer.Serialize(initialFlow),
            flowStorage: storage);

        using var initialGet = await host.Client.GetAsync($"/api/projects/{host.Project.Id}");
        initialGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = JsonNode.Parse(await initialGet.Content.ReadAsStringAsync())!.AsObject();
        var nextFlow = initial["flow"]!.DeepClone().AsObject();
        var operators = nextFlow["operators"]!.AsArray();
        var imageNode = operators.Select(node => node!.AsObject()).Single(node =>
            node["id"]!.GetValue<Guid>() == image.Id);
        var roiNode = operators.Select(node => node!.AsObject()).Single(node =>
            node["id"]!.GetValue<Guid>() == roi.Id);
        operators.Remove(operators.Single(node => node!["id"]!.GetValue<Guid>() == removed.Id));
        roiNode["x"] = 312.5;
        roiNode["y"] = 96.25;
        var roiParameters = roiNode["parameters"]!.AsArray().Select(node => node!.AsObject()).ToList();
        roiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "X")["value"] = 12;
        roiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Y")["value"] = 14;
        roiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Width")["value"] = 52;
        roiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Height")["value"] = 38;
        var imageParameters = imageNode["parameters"]!.AsArray();
        imageParameters.Add(CreateGoldenParameterNode("ExplicitNull", null));
        imageParameters.Add(CreateGoldenParameterNode("Zero", 0));
        imageParameters.Add(CreateGoldenParameterNode("Disabled", false));
        imageParameters.Add(CreateGoldenParameterNode("Empty", string.Empty));

        var rectangle = CreateGoldenOperator(factory, OperatorType.RectangleRegion, "Caliper RectangleRegion", 300, 220);
        rectangle.ExtensionData = new Dictionary<string, JsonElement>
        {
            ["futureRectangleField"] = JsonSerializer.SerializeToElement(new { source = "g5" })
        };
        rectangle.Parameters.Single(parameter => parameter.Name == "X").Value = 20;
        rectangle.Parameters.Single(parameter => parameter.Name == "Y").Value = 30;
        rectangle.Parameters.Single(parameter => parameter.Name == "Width").Value = 100;
        rectangle.Parameters.Single(parameter => parameter.Name == "Height").Value = 24;
        operators.Add(JsonNode.Parse(JsonSerializer.Serialize(rectangle)));
        foreach (var operatorNode in operators.Select(node => node!.AsObject()))
        {
            operatorNode.Remove("executionStatus");
            operatorNode.Remove("executionTimeMs");
            operatorNode.Remove("errorMessage");
        }

        var newConnections = new JsonArray();
        var replacementConnectionId = Guid.NewGuid();
        newConnections.Add(JsonNode.Parse(JsonSerializer.Serialize(new OperatorConnectionDto
        {
            Id = replacementConnectionId,
            SourceOperatorId = image.Id,
            SourcePortId = imageOutput.Id,
            TargetOperatorId = roi.Id,
            TargetPortId = roiImageInput.Id,
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureConnectionField"] = JsonSerializer.SerializeToElement("replacement")
            }
        })));
        var rectangleOutput = rectangle.OutputPorts.Single(port => port.DataType == PortDataType.Rectangle);
        var caliperRegionInput = caliper.InputPorts.Single(port => port.Name == "SearchRegion");
        newConnections.Add(JsonNode.Parse(JsonSerializer.Serialize(new OperatorConnectionDto
        {
            Id = Guid.NewGuid(),
            SourceOperatorId = rectangle.Id,
            SourcePortId = rectangleOutput.Id,
            TargetOperatorId = caliper.Id,
            TargetPortId = caliperRegionInput.Id
        })));
        nextFlow["connections"] = newConnections;
        var putBody = new JsonObject
        {
            ["name"] = initial["name"]!.DeepClone(),
            ["description"] = initial["description"]?.DeepClone(),
            ["expectedPersistenceRevision"] = initial["persistenceRevision"]!.DeepClone(),
            ["flow"] = nextFlow,
            ["globalVariables"] = null
        };

        using var put = await host.Client.PutAsync(
            $"/api/projects/{host.Project.Id}",
            new StringContent(putBody.ToJsonString(), Encoding.UTF8, "application/json"));
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        using var finalGet = await host.Client.GetAsync($"/api/projects/{host.Project.Id}");
        finalGet.StatusCode.Should().Be(HttpStatusCode.OK);
        var final = JsonNode.Parse(await finalGet.Content.ReadAsStringAsync())!.AsObject();
        final["persistenceRevision"]!.GetValue<long>().Should().Be(1);
        var finalFlow = final["flow"]!.AsObject();
        finalFlow["futureFlowField"]!["mode"]!.GetValue<string>().Should().Be("preserve");
        var finalOperators = finalFlow["operators"]!.AsArray().Select(node => node!.AsObject()).ToList();
        finalOperators.Should().NotContain(node => node["id"]!.GetValue<Guid>() == removed.Id);
        var finalImage = finalOperators.Single(node => node["id"]!.GetValue<Guid>() == image.Id);
        finalImage["futureOperatorField"]!["keep"]!.GetValue<bool>().Should().BeTrue();
        finalImage["outputPorts"]![0]!["futurePortField"]!.GetValue<string>().Should().Be("port-opaque");
        var finalImageParameters = finalImage["parameters"]!.AsArray().Select(node => node!.AsObject()).ToList();
        finalImageParameters.First()["futureParameterField"]!.GetValue<string>().Should().Be("parameter-opaque");
        finalImageParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "ExplicitNull")
            .ContainsKey("value").Should().BeTrue();
        finalImageParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "ExplicitNull")["value"]
            .Should().BeNull();
        finalImageParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Zero")["value"]!
            .GetValue<int>().Should().Be(0);
        finalImageParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Disabled")["value"]!
            .GetValue<bool>().Should().BeFalse();
        finalImageParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Empty")["value"]!
            .GetValue<string>().Should().BeEmpty();
        var finalRoi = finalOperators.Single(node => node["id"]!.GetValue<Guid>() == roi.Id);
        finalRoi["x"]!.GetValue<double>().Should().Be(312.5);
        finalRoi["y"]!.GetValue<double>().Should().Be(96.25);
        var finalRoiParameters = finalRoi["parameters"]!.AsArray().Select(node => node!.AsObject()).ToList();
        finalRoiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "X")["value"]!.GetValue<int>().Should().Be(12);
        finalRoiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Y")["value"]!.GetValue<int>().Should().Be(14);
        finalRoiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Width")["value"]!.GetValue<int>().Should().Be(52);
        finalRoiParameters.Single(parameter => parameter["name"]!.GetValue<string>() == "Height")["value"]!.GetValue<int>().Should().Be(38);
        var finalRectangle = finalOperators.Single(node => node["id"]!.GetValue<Guid>() == rectangle.Id);
        finalRectangle["futureRectangleField"]!["source"]!.GetValue<string>().Should().Be("g5");
        finalFlow["connections"]!.AsArray().Should().Contain(node =>
            node!["id"]!.GetValue<Guid>() == replacementConnectionId &&
            node["futureConnectionField"]!.GetValue<string>() == "replacement");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(7);
        storage.LastSavedFlowJson.Should().NotContain("executionStatus");
        storage.LastSavedFlowJson.Should().NotContain("executionTimeMs");
        storage.LastSavedFlowJson.Should().NotContain("errorMessage");
    }

    [Fact]
    public async Task ProjectLifecycleEndpoints_ShouldCreateReplayReconcileAndRejectPayloadMismatch()
    {
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            new ProjectGlobalVariableSchema());
        var operationId = Guid.NewGuid();
        var request = new
        {
            clientOperationId = operationId,
            name = "  lifecycle project  ",
            description = "  authoritative  "
        };

        using var first = await host.Client.PostAsJsonAsync("/api/projects", request);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var projectId = firstBody.GetProperty("projectId").GetGuid();
        firstBody.GetProperty("operationReplayed").GetBoolean().Should().BeFalse();
        firstBody.GetProperty("project").GetProperty("name").GetString().Should().Be("lifecycle project");
        firstBody.GetProperty("project").GetProperty("flow").GetProperty("operators").GetArrayLength().Should().Be(0);

        using var replay = await host.Client.PostAsJsonAsync("/api/projects", request);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        replayBody.GetProperty("projectId").GetGuid().Should().Be(projectId);
        replayBody.GetProperty("operationReplayed").GetBoolean().Should().BeTrue();

        using var reconcile = await host.Client.GetAsync($"/api/project-operations/{operationId}?kind=create");
        reconcile.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconcileBody = await reconcile.Content.ReadFromJsonAsync<JsonElement>();
        reconcileBody.GetProperty("status").GetString().Should().Be("completed");
        reconcileBody.GetProperty("projectId").GetGuid().Should().Be(projectId);

        using var mismatch = await host.Client.PostAsJsonAsync("/api/projects", new
        {
            clientOperationId = operationId,
            name = "different"
        });
        mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var mismatchBody = await mismatch.Content.ReadFromJsonAsync<JsonElement>();
        mismatchBody.GetProperty("code").GetString().Should().Be("OPERATION_PAYLOAD_MISMATCH");

        using var nonBlank = await host.Client.PostAsJsonAsync("/api/projects", new
        {
            clientOperationId = Guid.NewGuid(),
            name = "invalid",
            template = "unsupported"
        });
        nonBlank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var nonBlankBody = await nonBlank.Content.ReadFromJsonAsync<JsonElement>();
        nonBlankBody.GetProperty("code").GetString().Should().Be("PROJECT_VALIDATION_BLANK_CREATE_ONLY");
    }

    [Fact]
    public async Task ProjectLifecycleEndpoints_ShouldOpenThenTombstoneAndReturnStructuredNotFound()
    {
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            new ProjectGlobalVariableSchema());
        var revision = host.Project.PersistenceRevision;

        using var opened = await host.Client.PostAsync($"/api/projects/{host.Project.Id}/open", null);
        opened.StatusCode.Should().Be(HttpStatusCode.OK);
        var openedBody = await opened.Content.ReadFromJsonAsync<JsonElement>();
        openedBody.GetProperty("projectId").GetGuid().Should().Be(host.Project.Id);
        openedBody.GetProperty("lastOpenedAtUtc").GetDateTime().Kind.Should().Be(DateTimeKind.Utc);
        host.Project.PersistenceRevision.Should().Be(revision);

        var operationId = Guid.NewGuid();
        using var deleted = await host.Client.PostAsJsonAsync(
            $"/api/projects/{host.Project.Id}/delete",
            new
            {
                clientOperationId = operationId,
                expectedPersistenceRevision = revision
            });
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());
        var deletedBody = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        deletedBody.GetProperty("operation").GetProperty("result").GetProperty("cleanupStatus")
            .GetString().Should().Be("cleanup-pending");

        using var list = await host.Client.GetAsync("/api/projects");
        using var detail = await host.Client.GetAsync($"/api/projects/{host.Project.Id}");
        using var reopen = await host.Client.PostAsync($"/api/projects/{host.Project.Id}/open", null);
        using var update = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "must-not-return",
                ExpectedPersistenceRevision = revision
            });
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().Should().Be(0);
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reopen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("PROJECT_NOT_FOUND");
        (await reopen.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("PROJECT_NOT_FOUND");
        (await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()
            .Should().Be("PROJECT_NOT_FOUND");

        using var reconcile = await host.Client.GetAsync($"/api/project-operations/{operationId}?kind=delete");
        reconcile.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconcileBody = await reconcile.Content.ReadFromJsonAsync<JsonElement>();
        reconcileBody.GetProperty("status").GetString().Should().Be("completed");
        reconcileBody.GetProperty("result").GetProperty("deleted").GetBoolean().Should().BeTrue();
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

    private static ProjectAssetsDto CreateProjectAssets(string assetId, long projectRevision)
    {
        var payload = CreateCalibrationPayload(assetId);
        return new ProjectAssetsDto
        {
            CalibrationAssets =
            [
                new ProjectCalibrationAssetDto
                {
                    AssetId = assetId,
                    Kind = "CalibrationBundleV2",
                    Version = "2.0",
                    Producer = "test",
                    ContentHash = ProjectAssetJson.ComputePayloadHash(payload),
                    ProjectRevision = projectRevision,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                    Status = "authority",
                    Payload = payload
                }
            ]
        };
    }

    private static JsonElement CreateCalibrationPayload(string bundleId) =>
        JsonSerializer.SerializeToElement(
            new
            {
                schemaVersion = 2,
                bundleId,
                calibrationVersion = "2.0",
                quality = new
                {
                    accepted = true
                }
            },
            ProjectAssetJson.Options);

    private static string CreateResultOnlyFlowJson()
    {
        var decisionOperatorId = Guid.NewGuid();
        var decisionPortId = Guid.NewGuid();
        return SerializeFlow(
            new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "main",
                DecisionConfiguration = new DecisionConfiguration
                {
                    FinalDecisionBinding = new FinalDecisionBinding
                    {
                        SourceOperatorId = decisionOperatorId,
                        SourceOutputPortId = decisionPortId,
                        SourceOutputName = "Result",
                        DataType = DecisionValueType.Boolean,
                        Rule = DecisionInterpretationRule.Boolean,
                        TrueMeansOk = true
                    }
                },
                Operators =
                [
                    new OperatorDto
                    {
                        Id = decisionOperatorId,
                        Name = "DecisionComparator",
                        Type = OperatorType.Comparator,
                        X = 0,
                        Y = 0,
                        OutputPorts =
                        [
                            new PortDto
                            {
                                Id = decisionPortId,
                                Name = "Result",
                                Direction = PortDirection.Output,
                                DataType = PortDataType.Boolean
                            }
                        ]
                    }
                ]
            });
    }

    private static OperatorDto CreateGoldenOperator(
        IOperatorFactory factory,
        OperatorType type,
        string name,
        double x,
        double y)
    {
        var metadata = factory.GetMetadata(type) ?? throw new InvalidOperationException($"Missing metadata for {type}.");
        return new OperatorDto
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            X = x,
            Y = y,
            InputPorts = metadata.InputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Input,
                DataType = port.DataType,
                IsRequired = port.IsRequired
            }).ToList(),
            OutputPorts = metadata.OutputPorts.Select(port => new PortDto
            {
                Id = Guid.NewGuid(),
                Name = port.Name,
                Direction = PortDirection.Output,
                DataType = port.DataType,
                IsRequired = false
            }).ToList(),
            Parameters = metadata.Parameters.Select(parameter => new ParameterDto
            {
                Id = Guid.NewGuid(),
                Name = parameter.Name,
                DisplayName = parameter.DisplayName,
                Description = parameter.Description,
                DataType = parameter.DataType,
                Value = parameter.DefaultValue,
                DefaultValue = parameter.DefaultValue,
                MinValue = parameter.MinValue,
                MaxValue = parameter.MaxValue,
                IsRequired = parameter.IsRequired,
                Options = parameter.Options
            }).ToList(),
            IsEnabled = true
        };
    }

    private static JsonObject CreateGoldenParameterNode(string name, object? value) => new()
    {
        ["id"] = Guid.NewGuid(),
        ["name"] = name,
        ["displayName"] = name,
        ["description"] = null,
        ["dataType"] = value switch
        {
            bool => "bool",
            int => "int",
            string => "string",
            _ => "nullable"
        },
        ["value"] = value == null ? null : JsonValue.Create(value),
        ["defaultValue"] = null,
        ["minValue"] = null,
        ["maxValue"] = null,
        ["isRequired"] = false,
        ["options"] = null,
        ["futureParameterField"] = "new-parameter-opaque"
    };

    private static string SerializeFlow(OperatorFlowDto flow) =>
        JsonSerializer.Serialize(flow, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        });

    private static OperatorFlowDto DeserializeFlow(string flowJson) =>
        JsonSerializer.Deserialize<OperatorFlowDto>(flowJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        }) ?? throw new InvalidOperationException("Unable to deserialize test flow JSON.");

    private sealed class MutableProjectAssetStorage : IProjectAssetStorage
    {
        public ProjectAssetsDto Assets { get; set; } = new();

        public ProjectAssetStorageMetadata? Metadata { get; set; }

        public Task<ProjectAssetsDto> LoadAssetsAsync(Guid projectId) =>
            Task.FromResult(ProjectAssetJson.Clone(Assets));

        public Task<ProjectAssetStorageMetadata?> LoadMetadataAsync(Guid projectId) =>
            Task.FromResult(Metadata);

        public Task SaveAssetsAsync(
            Guid projectId,
            ProjectAssetsDto assets,
            long persistenceRevision,
            Guid saveId,
            string assetsHash)
        {
            Assets = ProjectAssetJson.Clone(assets);
            Metadata = new ProjectAssetStorageMetadata(
                1,
                projectId,
                persistenceRevision,
                assetsHash,
                saveId,
                DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        }

        public Task DeleteAssetsAsync(Guid projectId)
        {
            Assets = new ProjectAssetsDto();
            Metadata = null;
            return Task.CompletedTask;
        }
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
            IProjectFlowStorage? flowStorage = null,
            IProjectAssetStorage? assetStorage = null,
            UserRole role = UserRole.Admin,
            string? databaseFlowName = null)
        {
            ProjectSaveCoordinator.ResetStaticStateForTests();
            var project = new Project("demo");
            if (!string.IsNullOrWhiteSpace(databaseFlowName))
            {
                project.UpdateFlow(new OperatorFlow(databaseFlowName));
            }

            project.UpdateGlobalVariables(schema);
            var projects = new Dictionary<Guid, Project>
            {
                [project.Id] = project
            };
            var repository = Substitute.For<IProjectRepository>();
            repository.GetByIdAsync(Arg.Any<Guid>()).Returns(call =>
            {
                var candidate = projects.GetValueOrDefault(call.Arg<Guid>());
                return Task.FromResult(candidate is { IsDeleted: false } ? candidate : null);
            });
            repository.GetByIdFreshAsync(Arg.Any<Guid>()).Returns(call =>
            {
                var candidate = projects.GetValueOrDefault(call.Arg<Guid>());
                return Task.FromResult(candidate is { IsDeleted: false } ? candidate : null);
            });
            repository.GetByIdForUpdateAsync(Arg.Any<Guid>()).Returns(call =>
            {
                var candidate = projects.GetValueOrDefault(call.Arg<Guid>());
                return Task.FromResult(candidate is { IsDeleted: false } ? candidate : null);
            });
            repository.GetByIdIncludingDeletedAsync(Arg.Any<Guid>()).Returns(call =>
                Task.FromResult(projects.GetValueOrDefault(call.Arg<Guid>())));
            repository.GetAllAsync().Returns(_ => Task.FromResult<IEnumerable<Project>>(
                projects.Values.Where(candidate => !candidate.IsDeleted).ToList()));
            repository.GetRecentlyOpenedAsync(Arg.Any<int>()).Returns(call => Task.FromResult<IEnumerable<Project>>(
                projects.Values
                    .Where(candidate => !candidate.IsDeleted && candidate.LastOpenedAt != null)
                    .OrderByDescending(candidate => candidate.LastOpenedAt)
                    .Take(call.Arg<int>())
                    .ToList()));
            repository.SearchAsync(Arg.Any<string>()).Returns(call => Task.FromResult<IEnumerable<Project>>(
                projects.Values
                    .Where(candidate => !candidate.IsDeleted && candidate.Name.Contains(call.Arg<string>(), StringComparison.OrdinalIgnoreCase))
                    .ToList()));
            repository.AddAsync(Arg.Any<Project>()).Returns(call =>
            {
                var candidate = call.Arg<Project>();
                projects[candidate.Id] = candidate;
                return Task.FromResult(candidate);
            });
            repository.UpdateAsync(Arg.Any<Project>()).Returns(call =>
            {
                var candidate = call.Arg<Project>();
                projects[candidate.Id] = candidate;
                return Task.CompletedTask;
            });
            repository.AddWithLifecycleOperationAsync(Arg.Any<Project>(), Arg.Any<ProjectLifecycleOperation>())
                .Returns(call =>
                {
                    var candidate = call.ArgAt<Project>(0);
                    projects[candidate.Id] = candidate;
                    return Task.CompletedTask;
                });
            repository.TombstoneWithLifecycleOperationAsync(Arg.Any<Project>(), Arg.Any<ProjectLifecycleOperation>())
                .Returns(call =>
                {
                    var candidate = call.ArgAt<Project>(0);
                    projects[candidate.Id] = candidate;
                    return Task.CompletedTask;
                });
            repository.RecordOpenAsync(Arg.Any<Guid>(), Arg.Any<DateTime>()).Returns(call =>
            {
                var candidate = projects.GetValueOrDefault(call.ArgAt<Guid>(0));
                if (candidate is not { IsDeleted: false })
                {
                    return Task.FromResult<DateTime?>(null);
                }

                candidate.RecordOpen(call.ArgAt<DateTime>(1));
                return Task.FromResult(candidate.LastOpenedAt);
            });

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
            builder.Services.AddSingleton<IProjectLifecycleOperationRepository>(new InMemoryProjectLifecycleOperationRepository());
            builder.Services.AddSingleton(storage);
            if (assetStorage != null)
            {
                builder.Services.AddSingleton(assetStorage);
            }

            builder.Services.AddSingleton<IOperatorFactory>(new OperatorFactory());
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton(coordinator);
            builder.Services.AddSingleton<ILogger<ProjectService>>(NullLogger<ProjectService>.Instance);
            builder.Services.AddSingleton<ILogger<ProjectLifecycleCoordinator>>(NullLogger<ProjectLifecycleCoordinator>.Instance);
            builder.Services.AddScoped<ProjectSaveCoordinator>();
            builder.Services.AddScoped(sp => new RuntimePackageExporter(
                sp.GetRequiredService<IOperatorFactory>(),
                NullLogger<RuntimePackageExporter>.Instance));
            builder.Services.AddSingleton(sp => new StationPackageStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<StationPackageStore>.Instance));
            builder.Services.AddScoped<ProjectService>();
            builder.Services.AddScoped<ProjectLifecycleCoordinator>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = $"test-{role.ToString().ToLowerInvariant()}",
                    Username = $"test-{role.ToString().ToLowerInvariant()}",
                    Role = role.ToString(),
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, $"test-{role.ToString().ToLowerInvariant()}"),
                    new Claim(ClaimTypes.Name, $"test-{role.ToString().ToLowerInvariant()}"),
                    new Claim(ClaimTypes.Role, role.ToString())
                ], "Test"));
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

    private sealed class InMemoryProjectLifecycleOperationRepository : IProjectLifecycleOperationRepository
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, ProjectLifecycleOperation> _operations = new();

        public Task<ProjectLifecycleOperation?> GetAsync(
            string userId,
            ProjectLifecycleOperationKind kind,
            Guid clientOperationId,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_operations.Values.FirstOrDefault(operation =>
                    operation.UserId == userId &&
                    operation.Kind == kind &&
                    operation.ClientOperationId == clientOperationId));
            }
        }

        public Task<ProjectLifecycleOperation?> GetByIdAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_operations.GetValueOrDefault(operationId));
            }
        }

        public Task<ProjectLifecycleOperation?> GetDeleteAuthorityAsync(
            Guid projectId,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_operations.Values
                    .Where(operation =>
                        operation.ProjectId == projectId &&
                        operation.Kind == ProjectLifecycleOperationKind.Delete &&
                        operation.Status == ProjectLifecycleOperationStatus.Completed)
                    .OrderByDescending(operation => operation.UpdatedAtUtc)
                    .FirstOrDefault());
            }
        }

        public Task AddAsync(
            ProjectLifecycleOperation operation,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_operations.Values.Any(existing =>
                        existing.UserId == operation.UserId &&
                        existing.Kind == operation.Kind &&
                        existing.ClientOperationId == operation.ClientOperationId))
                {
                    throw new InvalidOperationException("duplicate operation identity");
                }

                _operations.Add(operation.Id, operation);
                return Task.CompletedTask;
            }
        }

        public Task UpdateAsync(
            ProjectLifecycleOperation operation,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _operations[operation.Id] = operation;
                return Task.CompletedTask;
            }
        }

        public Task<IReadOnlyList<ProjectLifecycleOperation>> GetRecoverableAsync(
            DateTimeOffset nowUtc,
            int take,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                IReadOnlyList<ProjectLifecycleOperation> result = _operations.Values
                    .Where(operation => operation.Status == ProjectLifecycleOperationStatus.Pending ||
                        operation.Status == ProjectLifecycleOperationStatus.FailedRetryable ||
                        (operation.Kind == ProjectLifecycleOperationKind.Delete &&
                         operation.Status == ProjectLifecycleOperationStatus.Completed &&
                         operation.CleanupAuthorityOperationId == null &&
                         operation.CleanupStatus != ProjectLifecycleCleanupStatus.CleanupCompleted))
                    .Where(operation => operation.CleanupNextAttemptAtUtc == null || operation.CleanupNextAttemptAtUtc <= nowUtc)
                    .OrderBy(operation => operation.UpdatedAtUtc)
                    .Take(take)
                    .ToList();
                return Task.FromResult(result);
            }
        }

        public Task<int> DeleteExpiredAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var expired = _operations.Values
                    .Where(operation => operation.ExpiresAtUtc <= nowUtc)
                    .Select(operation => operation.Id)
                    .ToList();
                foreach (var operationId in expired)
                {
                    _operations.Remove(operationId);
                }

                return Task.FromResult(expired.Count);
            }
        }
    }
}
