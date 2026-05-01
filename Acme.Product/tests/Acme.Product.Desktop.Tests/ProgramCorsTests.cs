using System.Net;
using Acme.Product.Desktop;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.Product.Desktop.Tests;

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
