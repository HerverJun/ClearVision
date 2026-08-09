using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClearVision.Product.Desktop.Configuration;
using ClearVision.Product.Desktop.Handlers;
using FluentAssertions;

namespace ClearVision.Product.Desktop.Tests.Architecture;

public sealed class StudioUiProfileIsolationGuardTests
{
    private static readonly string Root = FindRepositoryRoot();
    private const string StudioUiRoot =
        "ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI";

    [Fact]
    public void StartupProfiles_ShouldResolveOneProfileSpecificWebMessageOwner()
    {
        MainForm.ResolveWebMessageOwnerType(new StudioOptions
        {
            StartupProfile = StudioStartupProfileCatalog.NextDefault
        }).Should().Be(typeof(StudioHostCapabilityMessageHandler));
        MainForm.ResolveWebMessageOwnerType(new StudioOptions
        {
            StartupProfile = StudioStartupProfileCatalog.LegacyFallback
        }).Should().Be(typeof(WebMessageHandler));

        var mainForm = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/MainForm.cs"));
        Regex.Matches(mainForm, @"_messageOwner\?\.Initialize\(_webView\)").Count.Should().Be(1);
        Regex.Matches(mainForm, @"_messageOwner\?\.Dispose\(\)").Count.Should().Be(1);
        mainForm.Should().Contain("[StudioWebMessageOwner]");
        mainForm.Should().Contain("activeSubscriptionCount = owner.ActiveSubscriptionCount");
        mainForm.Should().NotContain("_messageHandler");

        var host = File.ReadAllText(RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/WebView2Host.cs"));
        host.Should().NotContain("WebMessageHandler");
        host.Should().NotContain("RegisterMessageHandlers");
        host.Should().NotContain("WebMessageReceived");
    }

    [Fact]
    public void StudioHostCapabilitySurface_ShouldAllowOnlyTheFilePickerCommand()
    {
        StudioHostCapabilityMessageHandler.IsAllowedMessageType("PickFileCommand")
            .Should().BeTrue();

        foreach (var messageType in new string?[]
                 {
                     null,
                     string.Empty,
                     "pickfilecommand",
                     "ExecuteOperatorCommand",
                     "UpdateFlowCommand",
                     "StartInspectionCommand",
                     "GenerateFlowCommand"
                 })
        {
            StudioHostCapabilityMessageHandler.IsAllowedMessageType(messageType)
                .Should().BeFalse();
        }
    }

    [Fact]
    public void StudioUiProductionSource_ShouldPostHostMessagesOnlyFromTheFilePickerPort()
    {
        var postingFiles = GetStudioUiProductionFiles()
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\bhost\.postMessage\s*\(",
                RegexOptions.CultureInvariant))
            .Select(StudioUiRelativePath)
            .ToList();

        postingFiles.Should().Equal("src/platform/host/filePickerPort.ts");
        File.ReadAllText(Path.Combine(
                RepoPath(StudioUiRoot),
                "src",
                "platform",
                "host",
                "filePickerPort.ts"))
            .Should().Contain("messageType: 'PickFileCommand'");
    }

    [Fact]
    public void StudioUiCanonicalDependencyInventory_ShouldExcludeTheLegacyCompositionRoot()
    {
        var projectPath = RepoPath(
            "ClearVision.Product/src/ClearVision.Product.Desktop/ClearVision.Product.Desktop.csproj");
        var project = XDocument.Load(projectPath);
        var canonicalInputs = project
            .Descendants()
            .Where(element => element.Name.LocalName == "StudioUiBuildInput")
            .Select(element => element.Attribute("Include")?.Value.Replace('\\', '/'))
            .Where(path => path?.Contains("wwwroot/", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToList();

        canonicalInputs.Should().Equal(
            "$(MSBuildThisFileDirectory)wwwroot/src/core/canvas/flowCanvasAdapter.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/core/canvas/flowCanvas.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/core/canvas/portTypeCompatibility.mjs",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/flowEditorInteraction.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/shared/components/uiComponents.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/shared/operatorVisuals.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/core/logging/debugLogger.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/previewCoordinator.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/previewOutputFormatter.mjs",
            "$(MSBuildThisFileDirectory)wwwroot/src/shared/parameterDependencyRules.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/core/canvas/imageCanvas.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/roiGeometry.mjs",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/roiEditorSupport.mjs",
            "$(MSBuildThisFileDirectory)wwwroot/src/shared/featureRegistry.js",
            "$(MSBuildThisFileDirectory)wwwroot/src/features/flow-editor/imagePixelProbe.mjs");
        canonicalInputs.Should().NotContain(path =>
            path.EndsWith("wwwroot/index.html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("wwwroot/src/app.js", StringComparison.OrdinalIgnoreCase));

        var vite = File.ReadAllText(Path.Combine(RepoPath(StudioUiRoot), "vite.config.ts"));
        Regex.Matches(vite, @"'@clearvision/canonical-[^']+'\s*:").Count.Should().Be(7);
        vite.Should().NotContain("'app.js'");
    }

    private static IReadOnlyList<string> GetStudioUiProductionFiles()
    {
        var sourceRoot = Path.Combine(RepoPath(StudioUiRoot), "src");
        return Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vue", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string StudioUiRelativePath(string path) =>
        Path.GetRelativePath(RepoPath(StudioUiRoot), path).Replace('\\', '/');

    private static string RepoPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

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
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "ClearVision.Product",
                        "ClearVision.Product.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
