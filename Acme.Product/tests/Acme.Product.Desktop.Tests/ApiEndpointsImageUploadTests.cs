using System.Reflection;
using Acme.Product.Desktop.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Acme.Product.Desktop.Tests;

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
}
