using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

public sealed class AiSecretSanitizerTests
{
    [Fact]
    public void Redact_ShouldRemoveApiKeysAndBearerTokens()
    {
        var bearerToken = "sk" + "-secret-token";
        var apiKey = "cpa" + "-key";
        var authHeader = "Authorization";
        var bearerPrefix = "Bearer";
        var input = $"{authHeader}: {bearerPrefix} {bearerToken} x-api-key: {apiKey} apiKey=\"hidden\" token=abc123456789";

        var output = AiSecretSanitizer.Redact(input);

        output.Should().NotContain(bearerToken);
        output.Should().NotContain(apiKey);
        output.Should().NotContain("hidden");
        output.Should().NotContain("abc123456789");
        output.Should().Contain("<redacted>");
    }

    [Fact]
    public void Redact_ShouldRemoveUrlQueryAndTokenPathFragments()
    {
        var tokenPath = "sk" + "-abc123456789";
        var input = $"https://user:pass@10.1.2.3/v1/{tokenPath}/chat?api_key=secret&token=secret2";

        var output = AiSecretSanitizer.Redact(input);

        output.Should().NotContain("secret");
        output.Should().NotContain(tokenPath);
        output.Should().Contain("<redacted>");
    }

    [Fact]
    public void RedactBaseUrlForReport_ShouldHideHostPathQueryAndUserInfo()
    {
        var output = AiSecretSanitizer.RedactBaseUrlForReport(
            "https://user:pass@10.20.30.40/cpa/v1/chat/completions?api_key=secret");

        output.Should().Be("https://<redacted-host>/<redacted-path>");
    }
}
