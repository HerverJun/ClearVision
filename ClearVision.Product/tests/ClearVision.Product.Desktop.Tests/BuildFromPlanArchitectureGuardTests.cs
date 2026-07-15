using System.Runtime.CompilerServices;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests;

[TestClassification(TestDomain.Desktop, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "desktop")]
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
        var end = text.IndexOf("private AiPersistenceWarning? PersistAgentGenerateFlowResult", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        var adapterBody = text[start..end];

        adapterBody.Should().Contain("BuildCommand.FromGenerationRequest");
        adapterBody.Should().Contain("_agentRunStreamService.CreateRun");
        adapterBody.Should().Contain("_buildRunService.RunAsync");
        adapterBody.Should().Contain("BuildCommandTransports.Internal");
        adapterBody.Should().NotContain("IVisionAgentBuildApplicationService");
        adapterBody.Should().NotContain("IVisionAgentBuildOrchestrator");
        adapterBody.Should().NotContain("_requirementBriefExtractor");
        adapterBody.Should().NotContain("TryPersistAgentGenerateFlowResult");
        adapterBody.Should().NotContain("GetService<");
    }

    [Fact]
    public void OnlyRunService_ShouldCallBuildApplicationService()
    {
        var sourceFiles = Directory.EnumerateFiles(
                Path.Combine(Root, "ClearVision.Product/src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("IVisionAgentBuildApplicationService.cs", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            if (!text.Contains(".BuildAsync(", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (relativePath.EndsWith("VisionAgentBuildRunService.cs", StringComparison.OrdinalIgnoreCase))
            {
                text.Should().Contain("_applicationService.BuildAsync");
                continue;
            }

            text.Should().NotContain("IVisionAgentBuildApplicationService", relativePath);
            text.Should().NotContain("_applicationService.BuildAsync", relativePath);
            text.Should().NotContain("buildApplicationService.BuildAsync", relativePath);
        }
    }

    [Fact]
    public void ApplicationService_ShouldNotPersistSessionProjection()
    {
        var text = File.ReadAllText(Path.Combine(
            Root,
            "ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Agent/VisionAgentBuildApplicationService.cs"));

        text.Should().NotContain("IConversationalFlowService");
        text.Should().NotContain("RecordAssistantResponse");
        text.Should().NotContain("ProjectedTerminals");
        text.Should().NotContain("TryProjectTerminal");
        text.Should().NotContain("Transport != AgentRun");
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
