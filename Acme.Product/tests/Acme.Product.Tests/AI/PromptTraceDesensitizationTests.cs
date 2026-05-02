using Acme.Product.Infrastructure.AI;
using FluentAssertions;

namespace Acme.Product.Tests.AI;

public sealed class PromptTraceDesensitizationTests
{
    [Fact]
    public void Desensitize_MasksWindowsPaths()
    {
        var trace = new AiPromptTrace
        {
            SystemPrompt = "Load model from C:\\Users\\John\\models\\yolo.onnx and C:\\Data\\images\\test.bmp"
        };

        var result = trace.Desensitize();

        result.SystemPrompt.Should().Contain("<local-path>");
        result.SystemPrompt.Should().NotContain("C:\\Users\\John");
        result.SystemPrompt.Should().NotContain("C:\\Data\\images");
    }

    [Fact]
    public void Desensitize_MasksUnixPaths()
    {
        var trace = new AiPromptTrace
        {
            UserPrompt = "Use /home/user/models/best.pt and /Users/admin/data/label.txt"
        };

        var result = trace.Desensitize();

        result.UserPrompt.Should().Contain("<local-path>");
        result.UserPrompt.Should().NotContain("/home/user/models");
        result.UserPrompt.Should().NotContain("/Users/admin/data");
    }

    [Fact]
    public void Desensitize_MasksApiKeys()
    {
        var trace = new AiPromptTrace
        {
            SystemPrompt = "Key is sk-abc123def456ghi789jkl012mno345pqr678stu and xai-abc123def456ghi789jkl012mno345pqr678stu"
        };

        var result = trace.Desensitize();

        result.SystemPrompt.Should().Contain("***API_KEY***");
        result.SystemPrompt.Should().NotContain("sk-abc123");
        result.SystemPrompt.Should().NotContain("xai-abc123");
    }

    [Fact]
    public void Desensitize_MasksGitHubTokens()
    {
        var trace = new AiPromptTrace
        {
            UserPrompt = "Token: ghp_abcdefghijklmnopqrstuvwxyz123456"
        };

        var result = trace.Desensitize();

        result.UserPrompt.Should().Contain("***API_KEY***");
        result.UserPrompt.Should().NotContain("ghp_");
    }

    [Fact]
    public void Desensitize_MasksPrivateIPs()
    {
        var trace = new AiPromptTrace
        {
            SystemPrompt = "Connect to 192.168.1.100:8080 or 10.0.0.1 or 172.16.0.5"
        };

        var result = trace.Desensitize();

        result.SystemPrompt.Should().Contain("<internal-ip>");
        result.SystemPrompt.Should().NotContain("192.168.1.100");
        result.SystemPrompt.Should().NotContain("10.0.0.1");
        result.SystemPrompt.Should().NotContain("172.16.0.5");
    }

    [Fact]
    public void Desensitize_MasksLocalDomains()
    {
        var trace = new AiPromptTrace
        {
            UserPrompt = "API at myserver.local:3000 and db.local"
        };

        var result = trace.Desensitize();

        result.UserPrompt.Should().Contain("<internal-host>");
        result.UserPrompt.Should().NotContain("myserver.local");
    }

    [Fact]
    public void Desensitize_MasksBaseUrlApiKey()
    {
        var trace = new AiPromptTrace
        {
            BaseUrl = "https://api.example.com/v1?key=sk-secret123456789abcdef"
        };

        var result = trace.Desensitize();

        result.BaseUrl.Should().Contain("key=***");
        result.BaseUrl.Should().NotContain("sk-secret123");
    }

    [Fact]
    public void Desensitize_PreservesNonSensitiveContent()
    {
        var trace = new AiPromptTrace
        {
            Mode = "new",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o",
            SystemPrompt = "You are a helpful assistant for industrial vision.",
            UserPrompt = "Generate a flow for defect detection."
        };

        var result = trace.Desensitize();

        result.Mode.Should().Be("new");
        result.Provider.Should().Be("OpenAI Compatible");
        result.Model.Should().Be("gpt-4o");
        result.SystemPrompt.Should().Contain("industrial vision");
        result.UserPrompt.Should().Contain("defect detection");
    }

    [Fact]
    public void Desensitize_PreservesVersionAndTokenInfo()
    {
        var trace = new AiPromptTrace
        {
            PromptVersionId = "v1.0",
            PromptVersionName = "Baseline",
            EstimatedInputTokens = 1200,
            EstimatedOutputTokens = 800,
            SelectionReason = "active"
        };

        var result = trace.Desensitize();

        result.PromptVersionId.Should().Be("v1.0");
        result.PromptVersionName.Should().Be("Baseline");
        result.EstimatedInputTokens.Should().Be(1200);
        result.EstimatedOutputTokens.Should().Be(800);
        result.SelectionReason.Should().Be("active");
    }

    [Fact]
    public void Desensitize_HandlesNullAndEmpty()
    {
        var trace = new AiPromptTrace
        {
            BaseUrl = null,
            SystemPrompt = string.Empty,
            UserPrompt = null,
            UsedReferenceFlowSummary = string.Empty
        };

        var result = trace.Desensitize();

        result.BaseUrl.Should().BeNull();
        result.SystemPrompt.Should().BeEmpty();
        result.UserPrompt.Should().BeNull();
        result.UsedReferenceFlowSummary.Should().BeEmpty();
    }

    [Fact]
    public void Desensitize_DoesNotMaskPublicIPs()
    {
        var trace = new AiPromptTrace
        {
            SystemPrompt = "API at 8.8.8.8 and 1.1.1.1"
        };

        var result = trace.Desensitize();

        // Public IPs should NOT be masked
        result.SystemPrompt.Should().Contain("8.8.8.8");
        result.SystemPrompt.Should().Contain("1.1.1.1");
    }
}
