using FluentAssertions;
using System.Runtime.CompilerServices;

namespace ClearVision.Product.Desktop.Tests;

public sealed class BuildFromPlanArchitectureGuardTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void DesktopAdapters_ShouldNotReferenceBuildOrchestratorOrBuildOptions()
    {
        var guardedFiles = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/AgentRunEndpoints.cs",
            "ClearVision.Product/src/ClearVision.Product.Desktop/Handlers/WebMessageHandler.cs",
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/GenerateFlowMessageHandler.cs"
        };

        foreach (var relativePath in guardedFiles)
        {
            var text = File.ReadAllText(Path.Combine(Root, relativePath));
            text.Should().NotContain("IVisionAgentBuildOrchestrator", relativePath);
            text.Should().NotContain("VisionAgentBuildOrchestrator", relativePath);
            text.Should().NotContain("AgentGenerateFlowOptions", relativePath);
        }
    }

    [Fact]
    public void AgentRunEndpoint_ShouldDelegateBuildTerminalLifecycleToRunService()
    {
        var text = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/AgentRunEndpoints.cs"));
        var start = text.IndexOf("private static async Task RunGenerateFlowAsync", StringComparison.Ordinal);
        var end = text.IndexOf("private static object BuildCreatePayload", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var methodBody = text[start..end];

        methodBody.Should().Contain("IVisionAgentBuildRunService");
        methodBody.Should().NotContain("IVisionAgentBuildApplicationService");
        methodBody.Should().NotContain("IVisionAgentBuildTerminalProjector");
        methodBody.Should().NotContain("streamService.Complete");
        methodBody.Should().NotContain("streamService.Fail");
        methodBody.Should().NotContain("streamService.Cancel");
        text.Should().NotContain("ProjectBuildTerminal");
        text.Should().NotContain("BuildReplayPayload");
        text.Should().NotContain("ResolveResultPlanId");
    }

    [Fact]
    public void WebMessageBuildFromPlanAdapter_ShouldCreateAgentRunBeforeBuild()
    {
        var text = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/GenerateFlowMessageHandler.cs"));
        var start = text.IndexOf("private async Task<AiFlowGenerationResult> RunBuildFromPlanViaAgentRunAsync", StringComparison.Ordinal);
        var end = text.IndexOf("private static AiFlowGenerationResult BuildAgentRunAdapterMissingResult", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var methodBody = text[start..end];

        methodBody.Should().Contain("_agentRunStreamService.CreateRun");
        methodBody.Should().Contain("_buildRunService.RunAsync");
        methodBody.Should().Contain("BuildCommandTransports.WebMessage");
        methodBody.Should().NotContain("_generationService.GenerateFlowAsync");
        methodBody.Should().NotContain("IVisionAgentBuildApplicationService");
    }

    [Fact]
    public void BuildFromPlanAdapter_ShouldDelegateToApplicationServiceWithoutLegacyExtractor()
    {
        var text = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/AiFlowGenerationService.cs"));
        var start = text.IndexOf("private async Task<AiFlowGenerationResult> GenerateFlowFromPlanAsync", StringComparison.Ordinal);
        var end = text.IndexOf("private void PersistAgentGenerateFlowResult", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var adapterBody = text[start..end];

        adapterBody.Should().Contain("BuildCommand.FromGenerationRequest");
        adapterBody.Should().Contain("IVisionAgentBuildApplicationService");
        adapterBody.Should().NotContain("IVisionAgentBuildOrchestrator");
        adapterBody.Should().NotContain("_requirementBriefExtractor");
        adapterBody.Should().NotContain("GetService<");
    }

    [Fact]
    public void BuildExecutionLayer_ShouldNotContainLegacyReadinessGate()
    {
        var guardedFiles = new[]
        {
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/VisionAgentOrchestrator.cs",
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/VisionAgentBuildOrchestrator.cs"
        };
        var forbiddenMarkers = new[]
        {
            "EnforceBuildMaturityGate",
            "EnforceMaturityGate",
            "BuildMaturityGateFailure",
            "BuildMaturityBlockedResult",
            "maturity_gate_blocked"
        };

        foreach (var relativePath in guardedFiles)
        {
            var text = File.ReadAllText(Path.Combine(Root, relativePath));
            foreach (var marker in forbiddenMarkers)
            {
                text.Should().NotContain(marker, relativePath);
            }
        }
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var startPath in new[]
                 {
                     Path.GetDirectoryName(sourceFile),
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                continue;
            }

            var directory = new DirectoryInfo(startPath);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "ClearVision.Product", "ClearVision.Product.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
