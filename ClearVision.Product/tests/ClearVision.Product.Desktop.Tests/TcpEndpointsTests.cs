using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public class TcpEndpointsTests
{
    [Fact]
    public async Task PutTcpProfiles_ShouldRejectNonAdminUser()
    {
        await using var host = await TcpEndpointTestHost.CreateAsync(new AppConfig(), role: "Operator");

        using var response = await host.Client.PutAsync(
            "/api/tcp/profiles",
            new StringContent("[]", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await host.ConfigurationService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task PutTcpProfiles_ShouldRejectInvalidProfileWithoutPersisting()
    {
        await using var host = await TcpEndpointTestHost.CreateAsync(new AppConfig());
        var payload = new[]
        {
            new
            {
                id = "bad",
                name = "Bad",
                enabled = true,
                mode = "Client",
                remoteHost = "999.1.1.1",
                remotePort = 0,
                localHost = "127.0.0.1",
                localPort = 9001,
                encoding = "UTF8",
                frameMode = "Raw",
                lineEnding = "None",
                timeoutMs = 5000,
                reconnect = true
            }
        };

        using var response = await host.Client.PutAsync(
            "/api/tcp/profiles",
            JsonContent(payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        document.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
        await host.ConfigurationService.DidNotReceive().SaveAsync(Arg.Any<AppConfig>());
    }

    [Fact]
    public async Task PutTcpProfiles_ShouldPersistTcpCommunicationWithoutChangingPlcCommunication()
    {
        var initialConfig = new AppConfig
        {
            Communication = new CommunicationConfig
            {
                ActiveProtocol = CommunicationConfig.ProtocolMc
            }
        };
        await using var host = await TcpEndpointTestHost.CreateAsync(initialConfig);
        var payload = new[]
        {
            new
            {
                id = "robot",
                name = "Robot",
                enabled = true,
                mode = "Client",
                remoteHost = "127.0.0.1",
                remotePort = 9000,
                localHost = "127.0.0.1",
                localPort = 9001,
                encoding = "UTF8",
                frameMode = "Raw",
                lineEnding = "None",
                timeoutMs = 5000,
                reconnect = true
            }
        };

        using var response = await host.Client.PutAsync(
            "/api/tcp/profiles",
            JsonContent(payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

        await host.ConfigurationService.Received(1).SaveAsync(Arg.Is<AppConfig>(config =>
            config.Communication.ActiveProtocol == CommunicationConfig.ProtocolMc
            && config.TcpCommunication.Profiles.Count == 1
            && config.TcpCommunication.Profiles[0].Id == "robot"));
    }

    [Fact]
    public async Task PostTcpSend_ShouldSendClientPayloadAndReturnResponse()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSingleEchoServerAsync(listener, cts.Token);

        var config = new AppConfig
        {
            TcpCommunication = new TcpCommunicationConfig
            {
                Profiles =
                [
                    new TcpCommunicationProfile
                    {
                        Id = "robot",
                        Name = "Robot",
                        Enabled = true,
                        Mode = TcpCommunicationProfile.ModeClient,
                        RemoteHost = "127.0.0.1",
                        RemotePort = port,
                        TimeoutMs = 2500
                    }
                ]
            }
        };
        await using var host = await TcpEndpointTestHost.CreateAsync(config);

        try
        {
            using var response = await host.Client.PostAsync(
                "/api/tcp/profiles/robot/send",
                JsonContent(new { payload = "PING", waitResponse = true, responseTimeoutMs = 2500 }));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            document.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            document.RootElement.GetProperty("response").GetString().Should().Be("PONG");
        }
        finally
        {
            cts.Cancel();
            listener.Stop();
            await IgnoreServerTerminationAsync(serverTask);
        }
    }

    private static StringContent JsonContent(object payload)
    {
        return new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
    }

    private static async Task RunSingleEchoServerAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        var buffer = new byte[4];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Be("PING");
        var response = Encoding.UTF8.GetBytes("PONG");
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task IgnoreServerTerminationAsync(Task serverTask)
    {
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
            // Expected on cleanup.
        }
        catch (SocketException)
        {
            // Listener stop during cleanup can interrupt Accept.
        }
        catch (ObjectDisposedException)
        {
            // Listener/stream may already be disposed during cleanup.
        }
    }

    private sealed class TcpEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TcpEndpointTestHost(WebApplication app, IConfigurationService configurationService)
        {
            _app = app;
            ConfigurationService = configurationService;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public IConfigurationService ConfigurationService { get; }

        public static async Task<TcpEndpointTestHost> CreateAsync(AppConfig initialConfig, string? role = "Admin")
        {
            initialConfig.Normalize();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();

            var currentConfig = initialConfig;
            var configService = Substitute.For<IConfigurationService>();
            configService.LoadAsync().Returns(_ => Task.FromResult(currentConfig));
            configService.GetCurrent().Returns(_ => currentConfig);
            configService
                .When(service => service.SaveAsync(Arg.Any<AppConfig>()))
                .Do(call =>
                {
                    currentConfig = call.Arg<AppConfig>();
                    currentConfig.Normalize();
                });
            configService.SaveAsync(Arg.Any<AppConfig>()).Returns(Task.CompletedTask);
            builder.Services.AddSingleton(configService);
            builder.Services.AddSingleton<ITcpDeviceManager>(sp =>
                new TcpDeviceManager(
                    sp.GetRequiredService<IConfigurationService>(),
                    sp.GetRequiredService<ILogger<TcpDeviceManager>>()));

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                if (role is not null)
                {
                    context.Items["CurrentUser"] = new ClearVision.Product.Application.Services.UserSession
                    {
                        UserId = role.ToLowerInvariant(),
                        Username = role.ToLowerInvariant(),
                        Role = role
                    };
                }

                await next();
            });
            app.MapTcpEndpoints();
            await app.StartAsync();
            return new TcpEndpointTestHost(app, configService);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
