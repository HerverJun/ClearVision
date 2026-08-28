using ClearVision.Product.Desktop.Configuration;

namespace ClearVision.Product.Desktop;

internal enum StudioStartupPageKind
{
    Legacy,
    Welcome
}

internal sealed record StudioStartupPageDecision(
    StudioStartupPageKind Kind,
    string? PagePath,
    string? RequiredFilePath,
    string? DiagnosticMessage)
{
    public bool IsNavigable => Kind is StudioStartupPageKind.Legacy;
}

internal static class StudioStartupPageResolver
{
    public const string LegacyPagePath = "/index.html";

    public static StudioStartupPageDecision Resolve(
        StudioOptions options,
        string? baseDirectory = null,
        string? legacyWebRoot = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ResolveLegacy(baseDirectory, legacyWebRoot);
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
        var root = legacyWebRoot ?? DesktopWebRootResolver.Resolve(baseDirectory);
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
            $"未找到旧前端入口文件：{indexPath}");
    }
}
