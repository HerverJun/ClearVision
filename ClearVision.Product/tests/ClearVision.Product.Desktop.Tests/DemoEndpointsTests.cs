using System.Net;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using UserSession = ClearVision.Product.Application.Services.UserSession;

namespace ClearVision.Product.Desktop.Tests;

public sealed class DemoEndpointsTests
{
    [Fact]
    public async Task DemoProjectCreateEndpoints_ShouldRejectOperator()
    {
        await using var host = await DemoEndpointTestHost.CreateAsync(UserRole.Operator);

        using var fullResponse = await host.Client.PostAsync("/api/demo/create", null);
        using var simpleResponse = await host.Client.PostAsync("/api/demo/create-simple", null);

        fullResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await fullResponse.Content.ReadAsStringAsync());
        simpleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden, await simpleResponse.Content.ReadAsStringAsync());
        await host.ProjectRepository.DidNotReceive().AddAsync(Arg.Any<Project>());
    }

    [Theory]
    [InlineData(UserRole.Engineer)]
    [InlineData(UserRole.Admin)]
    public async Task DemoProjectCreateEndpoints_ShouldAllowProjectEditors(UserRole role)
    {
        await using var host = await DemoEndpointTestHost.CreateAsync(role);

        using var fullResponse = await host.Client.PostAsync("/api/demo/create", null);
        using var simpleResponse = await host.Client.PostAsync("/api/demo/create-simple", null);

        fullResponse.StatusCode.Should().Be(HttpStatusCode.OK, await fullResponse.Content.ReadAsStringAsync());
        simpleResponse.StatusCode.Should().Be(HttpStatusCode.OK, await simpleResponse.Content.ReadAsStringAsync());
        await host.ProjectRepository.Received(2).AddAsync(Arg.Any<Project>());
    }

    [Fact]
    public async Task DemoGuide_ShouldRemainReadableByOperator()
    {
        await using var host = await DemoEndpointTestHost.CreateAsync(UserRole.Operator);

        using var response = await host.Client.GetAsync("/api/demo/guide");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private sealed class DemoEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private DemoEndpointTestHost(WebApplication app, IProjectRepository projectRepository)
        {
            this.app = app;
            ProjectRepository = projectRepository;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IProjectRepository ProjectRepository { get; }

        public static async Task<DemoEndpointTestHost> CreateAsync(UserRole role)
        {
            var projectRepository = Substitute.For<IProjectRepository>();
            projectRepository
                .AddAsync(Arg.Any<Project>())
                .Returns(call => Task.FromResult(call.Arg<Project>()));

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(projectRepository);
            builder.Services.AddSingleton<DemoProjectService>();

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
            app.MapDemoEndpoints();
            await app.StartAsync();
            return new DemoEndpointTestHost(app, projectRepository);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
