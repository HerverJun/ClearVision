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
[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]
public sealed class ProjectGlobalVariableEndpointsTests
{
    [Fact]
    public async Task ProjectGlobalVariableEndpoints_ShouldUpdateReadWriteAndResetWhenIdle()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));

        var updated = CreateSchema(variableId, 5, manualWriteAllowed: true);
        using var updateResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            SchemaPatch(host.Project, updated));
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
    public async Task GlobalVariablesPut_WhenExpectedPersistenceRevisionIsMissing_ShouldReturn422WithoutWrite()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            new UpdateProjectGlobalVariablesRequest
            {
                Schema = CreateSchema(variableId, 2, manualWriteAllowed: true)
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PMU003");
        host.Project.PersistenceRevision.Should().Be(0);
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task GlobalVariablesPut_WhenSchemaIsMissing_ShouldReturn422WithoutWrite()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            new UpdateProjectGlobalVariablesRequest
            {
                ExpectedPersistenceRevision = 0
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PMU005");
        host.Project.PersistenceRevision.Should().Be(0);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task GlobalVariablesPut_WhenExpectedPersistenceRevisionIsStale_ShouldReturn409WithoutWrite()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        host.Project.SetPersistenceRevision(2);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            SchemaPatch(
                host.Project,
                CreateSchema(variableId, 2, manualWriteAllowed: true),
                expectedPersistenceRevision: 1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PSV011");
        host.Project.PersistenceRevision.Should().Be(2);
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(1L);
        await host.Repository.DidNotReceive().UpdateAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task ProjectMutationPut_WhenProjectIsMissing_ShouldReturnOpaque404()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var missingId = Guid.NewGuid();

        using var projectResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{missingId}",
            new UpdateProjectRequest
            {
                ExpectedPersistenceRevision = 0,
                Name = "missing"
            });
        using var schemaResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{missingId}/global-variables",
            new UpdateProjectGlobalVariablesRequest
            {
                ExpectedPersistenceRevision = 0,
                Schema = CreateSchema(variableId, 2, manualWriteAllowed: true)
            });

        projectResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        schemaResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await projectResponse.Content.ReadAsStringAsync()).Should().BeEmpty();
        (await schemaResponse.Content.ReadAsStringAsync()).Should().BeEmpty();
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

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            SchemaPatch(host.Project, invalid));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
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
            SchemaPatch(host.Project, CreateSchema(variableId, 5, manualWriteAllowed: true)));

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
            SchemaPatch(host.Project, CreateSchema(variableId, 2, manualWriteAllowed: true)));
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
            SchemaPatch(host.Project, CreateSchema(variableId, 5, manualWriteAllowed: true)));
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
            new UpdateFlowRequest { ExpectedPersistenceRevision = host.Project.PersistenceRevision });

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
        body.GetProperty("code").GetString().Should().Be("PSV011");
        body.GetProperty("error").GetString().Should().Contain("Refresh and retry");
        body.GetProperty("detail").GetString().Should().Contain("Expected=1");
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
            storedFlowJson: CreateDecisionBoundFlowJson(),
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
    public async Task ProjectPut_WhenGlobalVariablesProvided_ShouldRejectDedicatedEndpointBypass(RuntimeStatus status)
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
                ExpectedPersistenceRevision = host.Project.PersistenceRevision,
                Name = "renamed",
                Description = "changed",
                GlobalVariables = CreateSchema(variableId, 5, manualWriteAllowed: true)
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("PMU008");
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
                ExpectedPersistenceRevision = host.Project.PersistenceRevision,
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
                ExpectedPersistenceRevision = host.Project.PersistenceRevision,
                Name = "renamed",
                Description = "changed"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        host.Project.Name.Should().Be("renamed");
        host.Project.Description.Should().Be("changed");
        await host.Repository.Received(1).UpdateAsync(host.Project);
    }

    [Fact]
    public async Task GlobalVariablesPut_WhenIdle_ShouldUpdateSchemaOnlyAndRefreshSession()
    {
        var variableId = Guid.NewGuid();
        await using var host = await ProjectGlobalVariableEndpointHost.CreateAsync(
            CreateSchema(variableId, 1, manualWriteAllowed: true));
        var oldSession = host.Registry.GetOrCreate(host.Project.Id, host.Project.GlobalVariables);
        oldSession.SetValue(variableId, 9L, ProjectVariableUpdatedBy.StudioManual);

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            SchemaPatch(host.Project, CreateSchema(variableId, 5, manualWriteAllowed: true)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<UpdateProjectGlobalVariablesResponse>();
        body.Should().NotBeNull();
        body!.PersistenceRevision.Should().Be(1);
        host.Project.Name.Should().Be("demo");
        host.Project.Description.Should().BeNull();
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
    public async Task DedicatedSchemaThenProjectFlowPatch_ShouldPreserveEachAuthoritativeParticipant()
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

        using var schemaResponse = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}/global-variables",
            SchemaPatch(host.Project, nextSchema, expectedPersistenceRevision: 0));
        schemaResponse.StatusCode.Should().Be(HttpStatusCode.OK, await schemaResponse.Content.ReadAsStringAsync());

        using var response = await host.Client.PutAsJsonAsync(
            $"/api/projects/{host.Project.Id}",
            new UpdateProjectRequest
            {
                Name = "renamed",
                Description = "changed",
                ExpectedPersistenceRevision = 1,
                Flow = nextFlow
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<ProjectDto>();
        body.Should().NotBeNull();
        body!.PersistenceRevision.Should().Be(2);
        body.Flow.Should().NotBeNull();
        host.Project.Name.Should().Be("renamed");
        host.Project.Description.Should().Be("changed");
        host.Project.GlobalVariables.Variables.Single().InitialValue.GetInt64().Should().Be(5L);
        await host.Repository.Received(2).UpdateAsync(host.Project);
        flowStorage.SaveCount.Should().Be(1);
        flowStorage.LastPersistenceRevision.Should().Be(2);
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

    private static UpdateProjectGlobalVariablesRequest SchemaPatch(
        Project project,
        ProjectGlobalVariableSchema schema,
        long? expectedPersistenceRevision = null) =>
        new()
        {
            ExpectedPersistenceRevision = expectedPersistenceRevision ?? project.PersistenceRevision,
            Schema = schema
        };

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

    private static string CreateDecisionBoundFlowJson() =>
        SerializeFlow(
            new OperatorFlowDto
            {
                Id = Guid.NewGuid(),
                Name = "main",
                Operators =
                [
                    new OperatorDto
                    {
                        Id = Guid.NewGuid(),
                        Name = "ResultOutput",
                        Type = OperatorType.ResultOutput,
                        X = 0,
                        Y = 0
                    }
                ]
            }.WithStringDecisionBinding());

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
            if (assetStorage != null)
            {
                builder.Services.AddSingleton(assetStorage);
            }

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
                    UserId = $"test-{role.ToString().ToLowerInvariant()}",
                    Username = $"test-{role.ToString().ToLowerInvariant()}",
                    Role = role.ToString(),
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
