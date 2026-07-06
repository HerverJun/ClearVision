using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.AI;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UserSession = ClearVision.Product.Application.Services.UserSession;

namespace ClearVision.Product.Desktop.Tests;

public sealed class TemplateEndpointTests
{
    [Fact]
    public async Task TemplateReadEndpoints_ShouldAllowOperator()
    {
        var template = CreateTemplate(Guid.NewGuid());
        await using var host = await TemplateEndpointTestHost.CreateAsync(UserRole.Operator);
        host.TemplateService
            .GetTemplatesAsync(null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<FlowTemplate>>([template]));
        host.TemplateService
            .GetTemplateAsync(template.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FlowTemplate?>(template));

        using var listResponse = await host.Client.GetAsync("/api/templates");
        using var detailResponse = await host.Client.GetAsync($"/api/templates/{template.Id:D}");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, await listResponse.Content.ReadAsStringAsync());
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK, await detailResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TemplateWriteEndpoints_ShouldRejectOperator()
    {
        var templateId = Guid.NewGuid();
        await using var host = await TemplateEndpointTestHost.CreateAsync(UserRole.Operator);

        using var createResponse = await host.Client.PostAsJsonAsync("/api/templates", CreatePayload());
        using var updateResponse = await host.Client.PutAsJsonAsync($"/api/templates/{templateId:D}", CreatePayload());

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await createResponse.Content.ReadAsStringAsync());
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await updateResponse.Content.ReadAsStringAsync());
        await host.TemplateService.DidNotReceive().CreateTemplateAsync(Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>());
        await host.TemplateService.DidNotReceive().UpdateTemplateAsync(Arg.Any<Guid>(), Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TemplateWriteEndpoints_ShouldAllowEngineer()
    {
        var templateId = Guid.NewGuid();
        await using var host = await TemplateEndpointTestHost.CreateAsync(UserRole.Engineer);
        host.TemplateService
            .CreateTemplateAsync(Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var template = call.ArgAt<FlowTemplate>(0);
                template.Id = templateId;
                return Task.FromResult(template);
            });
        host.TemplateService
            .UpdateTemplateAsync(templateId, Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<FlowTemplate?>(call.ArgAt<FlowTemplate>(1)));

        using var createResponse = await host.Client.PostAsJsonAsync("/api/templates", CreatePayload());
        using var updateResponse = await host.Client.PutAsJsonAsync($"/api/templates/{templateId:D}", CreatePayload());

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, await updateResponse.Content.ReadAsStringAsync());
        await host.TemplateService.Received(1).CreateTemplateAsync(Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>());
        await host.TemplateService.Received(1).UpdateTemplateAsync(templateId, Arg.Any<FlowTemplate>(), Arg.Any<CancellationToken>());
    }

    private static ApiEndpoints.TemplateUpsertRequest CreatePayload() => new()
    {
        Name = "Template",
        Description = "Template endpoint test",
        Industry = "General",
        Tags = ["test"],
        FlowJson = """{"operators":[],"connections":[]}"""
    };

    private static FlowTemplate CreateTemplate(Guid id) => new()
    {
        Id = id,
        Name = "Template",
        Description = "Template endpoint test",
        Industry = "General",
        FlowJson = """{"operators":[],"connections":[]}"""
    };

    private sealed class TemplateEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TemplateEndpointTestHost(WebApplication app, IFlowTemplateService templateService)
        {
            _app = app;
            TemplateService = templateService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IFlowTemplateService TemplateService { get; }

        public static async Task<TemplateEndpointTestHost> CreateAsync(UserRole role)
        {
            var templateService = Substitute.For<IFlowTemplateService>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(templateService);
            builder.Services.AddSingleton(Substitute.For<IOperatorFactory>());
            builder.Services.AddSingleton(new ParameterRecommender());
            builder.Services.AddSingleton(Substitute.For<IFlowExecutionService>());
            var admissionService = Substitute.For<IExecutionAdmissionService>();
            admissionService
                .ValidateOperator(Arg.Any<Operator>(), Arg.Any<ExecutionAdmissionSurface>())
                .Returns(ExecutionAdmissionResult.Allow());
            builder.Services.AddSingleton(admissionService);
            builder.Services.AddSingleton(NullLogger<OperatorPreviewService>.Instance);
            builder.Services.AddSingleton<OperatorPreviewService>();

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items["CurrentUser"] = new UserSession
                {
                    UserId = role.ToString().ToLowerInvariant(),
                    Username = role.ToString().ToLowerInvariant(),
                    Role = role.ToString(),
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                };
                await next();
            });
            MapOperatorEndpoints(app);
            await app.StartAsync();
            return new TemplateEndpointTestHost(app, templateService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static void MapOperatorEndpoints(IEndpointRouteBuilder app)
    {
        var method = typeof(ApiEndpoints).GetMethod(
            "MapOperatorEndpoints",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [app]);
    }
}
