namespace ClearVision.Product.Desktop;

internal enum StudioStartupPageKind
{
    Legacy,
    StudioUi,
    Diagnostic,
    Welcome
}

internal sealed record StudioStartupPageDecision(
    StudioStartupPageKind Kind,
    string? PagePath,
    string? RequiredFilePath,
    string? DiagnosticMessage,
    IReadOnlyList<string>? MissingPaths = null)
{
    public bool IsNavigable => Kind is StudioStartupPageKind.Legacy or StudioStartupPageKind.StudioUi;

    public IReadOnlyList<string> MissingAssetPaths { get; } =
        MissingPaths ?? Array.Empty<string>();
}

internal static class StudioStartupPageResolver
{
    public const string LegacyPagePath = "/index.html";
    public const string StudioUiPagePath = "/studio/index.html";

    public static StudioStartupPageDecision Resolve(
        string? baseDirectory = null,
        string? legacyWebRoot = null)
    {
        return Resolve(
            studioUiEnabled: false,
            baseDirectory: baseDirectory,
            legacyWebRoot: legacyWebRoot,
            studioUiWebRoot: null);
    }

    public static StudioStartupPageDecision Resolve(
        bool studioUiEnabled,
        string? baseDirectory = null,
        string? legacyWebRoot = null,
        string? studioUiWebRoot = null)
    {
        return studioUiEnabled
            ? ResolveStudioUi(baseDirectory, studioUiWebRoot)
            : ResolveLegacy(baseDirectory, legacyWebRoot);
    }

    public static Uri CreateInitialPageUri(int webPort, StudioStartupPageDecision decision)
    {
        if (webPort < 1 || webPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(webPort), "Web port must be between 1 and 65535.");
        }

        ArgumentNullException.ThrowIfNull(decision);
        if (!decision.IsNavigable || string.IsNullOrWhiteSpace(decision.PagePath))
        {
            throw new InvalidOperationException(decision.DiagnosticMessage ?? "Startup page is not navigable.");
        }

        return new Uri($"http://localhost:{webPort}{decision.PagePath}");
    }

    private static StudioStartupPageDecision ResolveLegacy(
        string? baseDirectory,
        string? legacyWebRoot)
    {
        var root = NormalizeRoot(
            legacyWebRoot,
            () => DesktopWebRootResolver.Resolve(baseDirectory));
        var indexPath = Path.Combine(root, "index.html");
        if (File.Exists(indexPath))
        {
            return new StudioStartupPageDecision(
                StudioStartupPageKind.Legacy,
                LegacyPagePath,
                indexPath,
                DiagnosticMessage: null);
        }

        return new StudioStartupPageDecision(
            StudioStartupPageKind.Welcome,
            PagePath: null,
            indexPath,
            $"未找到旧前端入口文件：{indexPath}",
            new[] { indexPath });
    }

    private static StudioStartupPageDecision ResolveStudioUi(
        string? baseDirectory,
        string? studioUiWebRoot)
    {
        var root = NormalizeRoot(
            studioUiWebRoot,
            () => DesktopWebRootResolver.ResolveStudioUi(baseDirectory));
        var indexPath = Path.Combine(root, "index.html");
        var assetsPath = Path.Combine(root, "assets");
        var manifestPath = Path.Combine(root, ".vite", "manifest.json");
        var missingPaths = new List<string>();

        if (!File.Exists(indexPath))
        {
            missingPaths.Add(indexPath);
        }

        if (!Directory.Exists(assetsPath) || !ContainsAnyFile(assetsPath))
        {
            missingPaths.Add(assetsPath);
        }

        if (!File.Exists(manifestPath))
        {
            missingPaths.Add(manifestPath);
        }

        if (missingPaths.Count == 0)
        {
            return new StudioStartupPageDecision(
                StudioStartupPageKind.StudioUi,
                StudioUiPagePath,
                indexPath,
                DiagnosticMessage: null);
        }

        var missingList = string.Join(
            Environment.NewLine,
            missingPaths.Select(path => $"- {path}"));
        var diagnosticMessage =
            "StudioUI 资产不完整，已进入诊断页且不会回退 Legacy。缺失或无效路径：" +
            Environment.NewLine +
            missingList;

        return new StudioStartupPageDecision(
            StudioStartupPageKind.Diagnostic,
            PagePath: null,
            missingPaths[0],
            diagnosticMessage,
            missingPaths);
    }

    private static string NormalizeRoot(string? configuredRoot, Func<string> fallback)
    {
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? fallback()
            : configuredRoot;
        return Path.GetFullPath(root);
    }

    private static bool ContainsAnyFile(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(
                    directoryPath,
                    "*",
                    SearchOption.AllDirectories)
                .Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
