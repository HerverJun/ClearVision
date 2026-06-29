using System.Net;
using ClearVision.Product.Desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClearVision.Product.Desktop.Tests;

public class ProgramCorsTests
{
    [Theory]
    [InlineData("http://app.local", 5000, true)]
    [InlineData("http://localhost:5000", 5000, true)]
    [InlineData("http://127.0.0.1:5010", 5000, true)]
    [InlineData("http://[::1]:5000", 5000, true)]
    [InlineData("http://localhost:5173", 5000, false)]
    [InlineData("https://app.local", 5000, false)]
    [InlineData("http://example.com", 5000, false)]
    [InlineData("file:///", 5000, false)]
    public void IsAllowedApiOrigin_ShouldOnlyAllowKnownLocalOrigins(
        string origin,
        int webPort,
        bool expected)
    {
        Program.IsAllowedApiOrigin(origin, webPort).Should().Be(expected);
    }

    [Fact]
    public void BuildSingleInstanceMutexName_ShouldBeStableForSameUserData()
    {
        var first = Program.BuildSingleInstanceMutexName(
            "DOMAIN\\operator",
            @"C:\Data\ClearVision\settings.json",
            @"C:\Program Files\ClearVision");
        var second = Program.BuildSingleInstanceMutexName(
            "DOMAIN\\operator",
            @"C:\Data\ClearVision\settings.json",
            @"C:\Program Files\ClearVision");

        second.Should().Be(first);
        first.Should().StartWith("Global\\ClearVision.Desktop.StoreLease.");
        first.Should().NotContain("DOMAIN");
        first.Should().NotContain("Data");
        first.Should().NotContain("Program Files");
    }

    [Fact]
    public void BuildSingleInstanceMutexName_ShouldBeIsolatedByUserAndDataOnly()
    {
        var baseline = Program.BuildSingleInstanceMutexName(
            "DOMAIN\\operator-a",
            @"C:\Data\A\settings.json",
            @"C:\Install\A");

        Program.BuildSingleInstanceMutexName("DOMAIN\\operator-b", @"C:\Data\A\settings.json", @"C:\Install\A")
            .Should().NotBe(baseline);
        Program.BuildSingleInstanceMutexName("DOMAIN\\operator-a", @"C:\Data\B\settings.json", @"C:\Install\A")
            .Should().NotBe(baseline);
        Program.BuildSingleInstanceMutexName("DOMAIN\\operator-a", @"C:\Data\A\settings.json", @"C:\Install\B")
            .Should().Be(baseline);
    }

    [Fact]
    public void BuildStoreLeaseMutexName_ShouldConflictWhenAnyStorePathOverlaps()
    {
        var conversation = Program.BuildStoreLeaseMutexName(
            "conversation",
            @"C:\Data\ClearVision\store.json",
            "DOMAIN\\operator");
        var agentRun = Program.BuildStoreLeaseMutexName(
            "agent-run",
            @"C:\Data\ClearVision\store.json",
            "DOMAIN\\operator");
        var independent = Program.BuildStoreLeaseMutexName(
            "agent-run",
            @"C:\Data\ClearVision\agent-runs",
            "DOMAIN\\operator");

        agentRun.Should().Be(conversation);
        independent.Should().NotBe(conversation);
    }

    [Fact]
    public async Task CorsPreflight_ShouldAllowAuthAndSseHeaders_ForKnownLocalOrigins()
    {
        await using var host = await CorsTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/protected");
        request.Headers.Add("Origin", "http://localhost:5000");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization, Last-Event-ID, X-Auth-Token");

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("http://localhost:5000");
        var allowedHeaders = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        allowedHeaders.Should().Contain("authorization");
        allowedHeaders.Should().Contain("last-event-id");
        allowedHeaders.Should().Contain("x-auth-token");
    }

    [Fact]
    public async Task CorsPreflight_ShouldRejectUnknownOrigins()
    {
        await using var host = await CorsTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/protected");
        request.Headers.Add("Origin", "http://example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Authorization");

        using var response = await host.Client.SendAsync(request);

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private sealed class CorsTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private CorsTestHost(WebApplication app)
        {
            _app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<CorsTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy
                        .SetIsOriginAllowed(origin => Program.IsAllowedApiOrigin(origin, 5000))
                        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
                        .WithHeaders("Authorization", "Content-Type", "Last-Event-ID", "X-Auth-Token", "X-Requested-With");
                });
            });

            var app = builder.Build();
            app.UseCors();
            app.MapGet("/api/protected", () => Results.Ok());
            await app.StartAsync();
            return new CorsTestHost(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
