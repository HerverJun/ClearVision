using System.Reflection;
using System.Net;
using System.Net.Http.Headers;
using ClearVision.Product.Application.Services;
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
    public async Task CachedImageEndpoint_ShouldRejectAnonymousRequests()
    {
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = Substitute.For<IAuthService>();
        await using var host = await ImageEndpointTestHost.CreateAsync(cache, authService);

        using var response = await host.Client.GetAsync($"/api/images/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _ = cache.DidNotReceive().GetAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task CachedImageEndpoint_ShouldReturnCachedBytesWithDetectedContentType()
    {
        var imageId = Guid.NewGuid();
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var cache = Substitute.For<IImageCacheRepository>();
        var authService = Substitute.For<IAuthService>();
        authService.GetSessionAsync("image-token").Returns(Task.FromResult<ClearVision.Product.Application.Services.UserSession?>(new ClearVision.Product.Application.Services.UserSession
        {
            UserId = "image-user",
            Username = "image-user",
            Role = "Engineer"
        }));
        cache.GetAsync(imageId).Returns(Task.FromResult<byte[]?>(imageData));
        await using var host = await ImageEndpointTestHost.CreateAsync(cache, authService);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/images/{imageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "image-token");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(imageData);
        _ = cache.Received(1).GetAsync(imageId);
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
            IAuthService authService)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });

            builder.WebHost.UseTestServer();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(cache);
            builder.Services.AddSingleton(authService);

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
