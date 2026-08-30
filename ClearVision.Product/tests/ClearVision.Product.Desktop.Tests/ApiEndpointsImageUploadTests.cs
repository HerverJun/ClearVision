using System.Reflection;
using System.Net;
using System.Net.Http.Headers;
using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Desktop.Endpoints;
using ClearVision.Product.Desktop.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop", Suites = "DesktopEndpoints")]

public sealed class ApiEndpointsImageUploadTests
{
    [Fact]
    public void TryDecodeImageUpload_ShouldAcceptBase64DataUrl()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var result = TryDecodeImageUpload(" data:image/png;base64,\r\n" + Convert.ToBase64String(expected) + "\n ");

        result.Ok.Should().BeTrue();
        result.ImageData.Should().Equal(expected);
        result.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public void TryDecodeImageUpload_ShouldRejectInvalidBase64()
    {
        var result = TryDecodeImageUpload("not valid base64");

        result.Ok.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.ErrorMessage.Should().Contain("invalid");
    }

    [Fact]
    public void TryDecodeImageUpload_ShouldRejectEmptyDataUrl()
    {
        var result = TryDecodeImageUpload("data:image/png;base64,");

        result.Ok.Should().BeFalse();
        result.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.ErrorMessage.Should().Contain("invalid");
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg")]
    [InlineData(new byte[] { 0x42, 0x4D }, "image/bmp")]
    public void GetImageResponseContentType_ShouldReflectCachedImageBytes(byte[] imageData, string expectedContentType)
    {
        var method = typeof(ApiEndpoints).GetMethod(
            "GetImageResponseContentType",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var contentType = method!.Invoke(null, [imageData]);

        contentType.Should().Be(expectedContentType);
    }

    [Fact]
    public async Task ResultImageEndpoint_ShouldRejectAnonymousRequests()
    {
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = Substitute.For<IAuthService>();
        await using var host = await ImageEndpointTestHost.CreateAsync(cache, authService);

        using var response = await host.Client.GetAsync(
            $"/api/projects/{Guid.NewGuid():D}/inspection-results/{Guid.NewGuid():D}/image");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _ = cache.DidNotReceive().GetEntryAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ResultImageEndpoint_ShouldRejectRoleWithoutResultReadCapability()
    {
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = CreateSessionAuthService("Viewer");
        var projectRepository = Substitute.For<IProjectRepository>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        await using var host = await ImageEndpointTestHost.CreateAsync(
            cache,
            authService,
            projectRepository,
            resultRepository);

        using var response = await SendAuthorizedAsync(
            host.Client,
            $"/api/projects/{Guid.NewGuid():D}/inspection-results/{Guid.NewGuid():D}/image");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _ = await projectRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>());
        _ = cache.DidNotReceive().GetEntryAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task ResultImageEndpoint_ShouldReturnBoundCachedBytesWithDetectedContentType()
    {
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = CreateSessionAuthService("Engineer");
        var projectRepository = Substitute.For<IProjectRepository>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        projectRepository.GetByIdAsync(projectId).Returns(new Project("result-image-project"));
        resultRepository.GetHistoryDetailAsync(projectId, resultId).Returns(new InspectionHistoryDetail
        {
            Id = resultId,
            ProjectId = projectId,
            ImageId = imageId,
            HasImage = true
        });
        cache.GetEntryAsync(imageId).Returns(new CachedImage(
            imageData,
            "jpeg",
            new ResultImageCacheAuthority(projectId, resultId)));
        await using var host = await ImageEndpointTestHost.CreateAsync(
            cache,
            authService,
            projectRepository,
            resultRepository);

        using var response = await SendAuthorizedAsync(
            host.Client,
            $"/api/projects/{projectId:D}/inspection-results/{resultId:D}/image");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(imageData);
        _ = cache.Received(1).GetEntryAsync(imageId);
    }

    [Theory]
    [InlineData("missing-project")]
    [InlineData("deleted-project")]
    [InlineData("missing-result")]
    [InlineData("wrong-result-project")]
    [InlineData("missing-image")]
    [InlineData("cache-miss")]
    [InlineData("unbound-cache")]
    [InlineData("wrong-cache-project")]
    [InlineData("wrong-cache-result")]
    public async Task ResultImageEndpoint_ShouldReturnOpaqueNotFoundForAuthorityMismatch(string scenario)
    {
        var projectId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var project = new Project("authority-project");
        if (scenario == "deleted-project")
        {
            project.MarkAsDeleted();
        }

        var projectRepository = Substitute.For<IProjectRepository>();
        var resultRepository = Substitute.For<IInspectionResultRepository>();
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = CreateSessionAuthService("Operator");

        if (scenario != "missing-project")
        {
            projectRepository.GetByIdAsync(projectId).Returns(project);
        }

        if (scenario is not "missing-result")
        {
            resultRepository.GetHistoryDetailAsync(projectId, resultId).Returns(new InspectionHistoryDetail
            {
                Id = resultId,
                ProjectId = scenario == "wrong-result-project" ? Guid.NewGuid() : projectId,
                ImageId = scenario == "missing-image" ? null : imageId,
                HasImage = scenario != "missing-image"
            });
        }

        if (scenario != "cache-miss")
        {
            ResultImageCacheAuthority? authority = scenario switch
            {
                "unbound-cache" => null,
                "wrong-cache-project" => new ResultImageCacheAuthority(Guid.NewGuid(), resultId),
                "wrong-cache-result" => new ResultImageCacheAuthority(projectId, Guid.NewGuid()),
                _ => new ResultImageCacheAuthority(projectId, resultId)
            };
            cache.GetEntryAsync(imageId).Returns(new CachedImage([1, 2, 3], "png", authority));
        }

        await using var host = await ImageEndpointTestHost.CreateAsync(
            cache,
            authService,
            projectRepository,
            resultRepository);

        using var response = await SendAuthorizedAsync(
            host.Client,
            $"/api/projects/{projectId:D}/inspection-results/{resultId:D}/image");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        if (scenario is "missing-project" or "deleted-project" or "missing-result" or "wrong-result-project" or "missing-image")
        {
            _ = cache.DidNotReceive().GetEntryAsync(Arg.Any<Guid>());
        }
    }

    [Fact]
    public async Task LegacyImageGuidEndpoint_ShouldBeStableOpaqueNotFoundWithoutCacheRead()
    {
        var cache = Substitute.For<IImageCacheRepository>();
        await using var host = await ImageEndpointTestHost.CreateAsync(
            cache,
            CreateSessionAuthService("Admin"));

        using var response = await SendAuthorizedAsync(host.Client, $"/api/images/{Guid.NewGuid():D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        _ = cache.DidNotReceive().GetAsync(Arg.Any<Guid>());
        _ = cache.DidNotReceive().GetEntryAsync(Arg.Any<Guid>());
    }

    private static IAuthService CreateSessionAuthService(string role)
    {
        var authService = Substitute.For<IAuthService>();
        authService.GetSessionAsync("image-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "image-user",
            Username = "image-user",
            Role = role
        }));
        return authService;
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "image-token");
        return await client.SendAsync(request);
    }

    private static DecodeResult TryDecodeImageUpload(string? dataBase64)
    {
        var method = typeof(ApiEndpoints).GetMethod(
            "TryDecodeImageUpload",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        object?[] args = [dataBase64, null, string.Empty, 0];
        var ok = (bool)method!.Invoke(null, args)!;

        return new DecodeResult(
            ok,
            (byte[])args[1]!,
            (string)args[2]!,
            (int)args[3]!);
    }

    private sealed record DecodeResult(
        bool Ok,
        byte[] ImageData,
        string ErrorMessage,
        int StatusCode);

    private sealed class ImageEndpointTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private ImageEndpointTestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ImageEndpointTestHost> CreateAsync(
            IImageCacheRepository cache,
            IAuthService authService,
            IProjectRepository? projectRepository = null,
            IInspectionResultRepository? resultRepository = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(cache);
            builder.Services.AddSingleton(authService);
            builder.Services.AddSingleton(projectRepository ?? Substitute.For<IProjectRepository>());
            builder.Services.AddSingleton(resultRepository ?? Substitute.For<IInspectionResultRepository>());

            var app = builder.Build();
            app.UseMiddleware<AuthMiddleware>();
            var mapper = typeof(ApiEndpoints).GetMethod(
                "MapImageEndpoints",
                BindingFlags.NonPublic | BindingFlags.Static);
            mapper.Should().NotBeNull();
            mapper!.Invoke(null, [app]);
            await app.StartAsync();

            return new ImageEndpointTestHost(app, app.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
