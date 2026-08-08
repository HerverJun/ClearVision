using System.Text.Json;
using ClearVision.Product.Contracts.Messages;
using ClearVision.Product.Infrastructure.AI;
using FluentAssertions;

namespace ClearVision.Product.Tests.AI;

[TestClassification(TestDomain.Ai, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "vision-agent")]
public sealed class PromptTraceDesensitizationTests
{
    [Fact]
    public void Desensitize_MasksWindowsPaths()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "Load model from C:\\Users\\John\\models\\yolo.onnx and C:\\Data\\images\\test.bmp"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("<local-path>");
        result.UsedReferenceFlowSummary.Should().NotContain("C:\\Users\\John");
        result.UsedReferenceFlowSummary.Should().NotContain("C:\\Data\\images");
    }

    [Fact]
    public void Desensitize_MasksUnixPaths()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "Use /home/user/models/best.pt and /Users/admin/data/label.txt"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("<local-path>");
        result.UsedReferenceFlowSummary.Should().NotContain("/home/user/models");
        result.UsedReferenceFlowSummary.Should().NotContain("/Users/admin/data");
    }

    [Fact]
    public void Desensitize_MasksApiKeys()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "Key is " + "sk-" + "abc123def456ghi789jkl012mno345pqr678stu" +
                                       " and xai-abc123def456ghi789jkl012mno345pqr678stu"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("***API_KEY***");
        result.UsedReferenceFlowSummary.Should().NotContain("sk-abc123");
        result.UsedReferenceFlowSummary.Should().NotContain("xai-abc123");
    }

    [Fact]
    public void Desensitize_MasksGitHubTokens()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "Token: " + "ghp_" + "abcdefghijklmnopqrstuvwxyz123456"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("***API_KEY***");
        result.UsedReferenceFlowSummary.Should().NotContain("ghp_");
    }

    [Fact]
    public void Desensitize_MasksPrivateIPs()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "Connect to 192.168.1.100:8080 or 10.0.0.1 or 172.16.0.5"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("<internal-ip>");
        result.UsedReferenceFlowSummary.Should().NotContain("192.168.1.100");
        result.UsedReferenceFlowSummary.Should().NotContain("10.0.0.1");
        result.UsedReferenceFlowSummary.Should().NotContain("172.16.0.5");
    }

    [Fact]
    public void Desensitize_MasksLocalDomains()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "API at myserver.local:3000 and db.local"
        };

        var result = trace.Desensitize();

        result.UsedReferenceFlowSummary.Should().Contain("<internal-host>");
        result.UsedReferenceFlowSummary.Should().NotContain("myserver.local");
    }

    [Fact]
    public void Desensitize_MasksBaseUrlApiKey()
    {
        var trace = new AiPromptTrace
        {
            BaseUrl = "https://api.example.com/v1?key=" + "sk-" + "secret123456789abcdef"
        };

        var result = trace.Desensitize();

        result.BaseUrl.Should().Be("[hidden]");
        result.BaseUrl.Should().NotContain("sk-secret123");
    }

    [Fact]
    public void Desensitize_HidesAttachmentReportPathsAndNames()
    {
        var trace = new AiPromptTrace
        {
            AttachmentReport = new GenerateFlowAttachmentReport
            {
                Sent =
                [
                    new GenerateFlowAttachmentSentItem
                    {
                        Path = @"C:\factory\parts\scratch.png",
                        Name = "scratch.png"
                    }
                ],
                Skipped =
                [
                    new GenerateFlowAttachmentSkippedItem
                    {
                        Path = "/home/operator/parts/live.png",
                        Name = "live.png",
                        Reason = "too_large"
                    }
                ]
            }
        };

        var result = trace.Desensitize();
        var json = JsonSerializer.Serialize(result.AttachmentReport);

        json.Should().Contain("sentCount");
        json.Should().Contain("skippedCount");
        json.Should().Contain("too_large");
        json.Should().NotContain(@"C:\factory");
        json.Should().NotContain("/home/operator");
        json.Should().NotContain("scratch.png");
        json.Should().NotContain("live.png");
    }

    [Fact]
    public void Desensitize_PreservesNonSensitiveMetadataAndHidesPromptBodies()
    {
        var trace = new AiPromptTrace
        {
            Mode = "new",
            Provider = "OpenAI Compatible",
            Model = "gpt-4o",
            SystemPrompt = "You are a helpful assistant for industrial vision.",
            UserPrompt = "Generate a flow for defect detection.",
            UsedReferenceFlowSummary = "Reference flow summary"
        };

        var result = trace.Desensitize();

        result.Mode.Should().Be("new");
        result.Provider.Should().Be("OpenAI Compatible");
        result.Model.Should().Be("gpt-4o");
        result.SystemPrompt.Should().Be("[hidden]");
        result.UserPrompt.Should().Be("[hidden]");
        result.UsedReferenceFlowSummary.Should().Be("Reference flow summary");
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
            UserPrompt = null!,
            UsedReferenceFlowSummary = string.Empty
        };

        var result = trace.Desensitize();

        result.BaseUrl.Should().BeNull();
        result.SystemPrompt.Should().BeEmpty();
        result.UserPrompt.Should().BeEmpty();
        result.UsedReferenceFlowSummary.Should().BeEmpty();
    }

    [Fact]
    public void Desensitize_DoesNotMaskPublicIPs()
    {
        var trace = new AiPromptTrace
        {
            UsedReferenceFlowSummary = "API at 8.8.8.8 and 1.1.1.1"
        };

        var result = trace.Desensitize();

        // Public IPs should NOT be masked
        result.UsedReferenceFlowSummary.Should().Contain("8.8.8.8");
        result.UsedReferenceFlowSummary.Should().Contain("1.1.1.1");
    }
}
