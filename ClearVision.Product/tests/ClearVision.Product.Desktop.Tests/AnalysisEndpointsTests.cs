using System.Net;
using System.Text.Json;
using ClearVision.Product.Application.DTOs;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]
public sealed class AnalysisEndpointsTests
{
    [Fact]
    public async Task StatisticsEndpoint_WhenBudgetIsRejected_ShouldReturnStableBadRequestContract()
    {
        var service = Substitute.For<IResultAnalysisService>();
        service.GetStatisticsAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime?>(),
                Arg.Any<DateTime?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(_ => Task.FromException<InspectionStatisticsDto>(
                new ResultAnalysisBudgetException(
                    "ANALYSIS_TIME_RANGE_LIMIT",
                    "Analysis requests may span at most 31 days.")));
        await using var host = await AnalysisEndpointTestHost.CreateAsync(service);

        using var response = await host.Client.GetAsync(
            $"/api/analysis/statistics/{Guid.NewGuid():D}?startTime=2026-07-01T00:00:00Z&endTime=2026-08-02T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement;
        payload.GetProperty("error").GetString().Should().Be("ANALYSIS_TIME_RANGE_LIMIT");
        payload.GetProperty("message").GetString().Should().Contain("31 days");
        payload.GetProperty("maximumWindowDays").GetInt32().Should().Be(ResultAnalysisQueryBudget.MaximumWindowDays);
        payload.GetProperty("maximumTrendPoints").GetInt32().Should().Be(ResultAnalysisQueryBudget.MaximumTrendPoints);
        payload.GetProperty("maximumTrendRows").GetInt32().Should().Be(ResultAnalysisQueryBudget.MaximumTrendRows);
    }

    [Fact]
    public async Task TrendEndpoint_WhenRowBudgetIsRejected_ShouldReturnTheSameBadRequestContract()
    {
        var service = Substitute.For<IResultAnalysisService>();
        service.GetTrendAnalysisAsync(
                Arg.Any<Guid>(),
                Arg.Any<TrendInterval>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string?>(),
                Arg.Any<string?>())
            .Returns(_ => Task.FromException<TrendAnalysisDto>(
                new ResultAnalysisBudgetException(
                    "ANALYSIS_QUERY_ROW_LIMIT",
                    "Analysis requests may scan at most 25000 result rows.")));
        await using var host = await AnalysisEndpointTestHost.CreateAsync(service);

        using var response = await host.Client.GetAsync(
            $"/api/analysis/trend/{Guid.NewGuid():D}?interval=Hour&startTime=2026-08-01T00:00:00Z&endTime=2026-08-01T01:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var payload = document.RootElement;
        payload.GetProperty("error").GetString().Should().Be("ANALYSIS_QUERY_ROW_LIMIT");
        payload.GetProperty("message").GetString().Should().Contain("25000");
        payload.GetProperty("maximumTrendRows").GetInt32().Should().Be(ResultAnalysisQueryBudget.MaximumTrendRows);
    }

    private sealed class AnalysisEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private AnalysisEndpointTestHost(WebApplication application, HttpClient client)
        {
            _application = application;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<AnalysisEndpointTestHost> CreateAsync(IResultAnalysisService service)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(service);
            var application = builder.Build();
            application.MapAnalysisEndpoints();
            await application.StartAsync();
            return new AnalysisEndpointTestHost(application, application.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }
}
