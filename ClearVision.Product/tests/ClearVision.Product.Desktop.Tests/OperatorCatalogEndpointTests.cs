using System.Net;
using System.Reflection;
using System.Text.Json;
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
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

public sealed class OperatorCatalogEndpointTests
{
    [Fact]
    public void EndpointProjection_ShouldFilterCompatibilityAndUseStableCategoryOrdering()
    {
        var all = new OperatorFactory().GetAllMetadata().ToList();

        var defaultCatalog = ApiEndpoints.GetOperatorEndpointMetadata(all, includeCompatibility: false);
        var compatibilityCatalog = ApiEndpoints.GetOperatorEndpointMetadata(all, includeCompatibility: true);

        defaultCatalog.Should().HaveCount(157);
        compatibilityCatalog.Should().HaveCount(158);
        defaultCatalog.Should().NotContain(item => item.Type == OperatorType.Morphology);
        compatibilityCatalog.Should().ContainSingle(item => item.Type == OperatorType.Morphology);
        defaultCatalog.Should().OnlyContain(item => !item.DefaultHidden);
        compatibilityCatalog
            .Select(item => $"{OperatorCategoryCatalog.GetOrder(item.CategoryId):D2}|{item.DisplayName}")
            .Should()
            .BeInAscendingOrder();
    }

    [Fact]
    public async Task LibraryTypesAndDetailsEndpoints_ShouldExposeAlignedCurrentBranchMetadata()
    {
        await using var host = await OperatorEndpointTestHost.CreateAsync();

        using var defaultLibrary = await host.Client.GetAsync("/api/operators/library");
        using var compatibilityLibrary = await host.Client.GetAsync("/api/operators/library?includeCompatibility=true");
        using var defaultTypes = await host.Client.GetAsync("/api/operators/types");
        using var compatibilityTypes = await host.Client.GetAsync("/api/operators/types?includeCompatibility=true");
        using var detail = await host.Client.GetAsync("/api/operators/Morphology/metadata");
        using var missing = await host.Client.GetAsync("/api/operators/999999/metadata");

        var defaultLibraryBody = await defaultLibrary.Content.ReadAsStringAsync();
        var compatibilityLibraryBody = await compatibilityLibrary.Content.ReadAsStringAsync();
        var defaultTypesBody = await defaultTypes.Content.ReadAsStringAsync();
        var compatibilityTypesBody = await compatibilityTypes.Content.ReadAsStringAsync();
        var detailBody = await detail.Content.ReadAsStringAsync();

        defaultLibrary.StatusCode.Should().Be(HttpStatusCode.OK, defaultLibraryBody);
        compatibilityLibrary.StatusCode.Should().Be(HttpStatusCode.OK, compatibilityLibraryBody);
        defaultTypes.StatusCode.Should().Be(HttpStatusCode.OK, defaultTypesBody);
        compatibilityTypes.StatusCode.Should().Be(HttpStatusCode.OK, compatibilityTypesBody);
        detail.StatusCode.Should().Be(HttpStatusCode.OK, detailBody);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound, await missing.Content.ReadAsStringAsync());

        using var defaultLibraryJson = JsonDocument.Parse(defaultLibraryBody);
        using var compatibilityLibraryJson = JsonDocument.Parse(compatibilityLibraryBody);
        using var defaultTypesJson = JsonDocument.Parse(defaultTypesBody);
        using var compatibilityTypesJson = JsonDocument.Parse(compatibilityTypesBody);
        using var detailJson = JsonDocument.Parse(detailBody);

        var defaultLibraryItems = defaultLibraryJson.RootElement.EnumerateArray().ToList();
        var compatibilityLibraryItems = compatibilityLibraryJson.RootElement.EnumerateArray().ToList();
        var defaultTypeValues = defaultTypesJson.RootElement.EnumerateArray().Select(item => item.GetInt32()).ToList();
        var compatibilityTypeValues = compatibilityTypesJson.RootElement.EnumerateArray().Select(item => item.GetInt32()).ToList();

        defaultLibraryItems.Should().HaveCount(157);
        compatibilityLibraryItems.Should().HaveCount(158);
        defaultTypeValues.Should().Equal(defaultLibraryItems.Select(item => item.GetProperty("type").GetInt32()));
        compatibilityTypeValues.Should().Equal(compatibilityLibraryItems.Select(item => item.GetProperty("type").GetInt32()));
        defaultTypeValues.Should().NotContain((int)OperatorType.Morphology);
        compatibilityTypeValues.Should().Contain((int)OperatorType.Morphology);

        detailJson.RootElement.GetProperty("type").GetInt32().Should().Be((int)OperatorType.Morphology);
        detailJson.RootElement.GetProperty("lifecycle").GetInt32().Should().Be((int)OperatorLifecycle.Legacy);
        detailJson.RootElement.GetProperty("defaultHidden").GetBoolean().Should().BeTrue();
        detailJson.RootElement.GetProperty("inputPorts").ValueKind.Should().Be(JsonValueKind.Array);
        detailJson.RootElement.GetProperty("outputPorts").ValueKind.Should().Be(JsonValueKind.Array);
        detailJson.RootElement.GetProperty("parameters").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private sealed class OperatorEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication app;

        private OperatorEndpointTestHost(WebApplication app)
        {
            this.app = app;
            Client = app.GetTestClient();
        }

        public HttpClient Client { get; }

        public static async Task<OperatorEndpointTestHost> CreateAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IOperatorFactory, OperatorFactory>();
            builder.Services.AddSingleton(Substitute.For<IFlowTemplateService>());
            builder.Services.AddSingleton(new ParameterRecommender());
            builder.Services.AddSingleton(Substitute.For<IFlowExecutionService>());
            builder.Services.AddSingleton(Substitute.For<IExecutionAdmissionService>());
            builder.Services.AddSingleton<OperatorPreviewService>();

            var app = builder.Build();
            app.UseDeveloperExceptionPage();
            MapOperatorEndpoints(app);
            await app.StartAsync();
            return new OperatorEndpointTestHost(app);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
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
