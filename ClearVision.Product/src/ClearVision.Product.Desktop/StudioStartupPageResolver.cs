using ClearVision.Product.Desktop.Configuration;

namespace ClearVision.Product.Desktop;

internal enum StudioStartupPageKind
{
    Legacy,
    FrontendV2,
    Diagnostic,
    Welcome
}

internal sealed record StudioStartupPageDecision(
    StudioStartupPageKind Kind,
    bool WorkspaceV2Enabled,
    string? PagePath,
    string? RequiredFilePath,
    string? DiagnosticMessage)
{
    public bool IsNavigable => Kind is StudioStartupPageKind.Legacy or StudioStartupPageKind.FrontendV2;
}

internal static class StudioStartupPageResolver
{
    public const string LegacyPagePath = "/index.html";
    public const string FrontendV2BasePath = "/v2";
    public const string FrontendV2PagePath = "/v2/index.html";

    public static StudioStartupPageDecision Resolve(
        StudioOptions options,
        string? baseDirectory = null,
        string? legacyWebRoot = null,
        string? frontendV2WebRoot = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.WorkspaceV2Enabled)
        {
            return ResolveFrontendV2(baseDirectory, frontendV2WebRoot);
        }

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
                WorkspaceV2Enabled: false,
                LegacyPagePath,
                indexPath,
                DiagnosticMessage: null);
        }

        return new StudioStartupPageDecision(
            StudioStartupPageKind.Welcome,
            WorkspaceV2Enabled: false,
            PagePath: null,
            indexPath,
            $"未找到旧前端入口文件：{indexPath}");
    }

    private static StudioStartupPageDecision ResolveFrontendV2(
        string? baseDirectory,
        string? frontendV2WebRoot)
    {
        var root = frontendV2WebRoot ?? DesktopWebRootResolver.ResolveFrontendV2(baseDirectory);
        var indexPath = Path.Combine(root, "index.html");
        if (!File.Exists(indexPath))
        {
            return CreateFrontendV2Diagnostic(indexPath, "缺少 /v2/index.html");
        }

        var assetsDirectory = Path.Combine(root, "assets");
        if (!Directory.Exists(assetsDirectory) ||
            !Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.TopDirectoryOnly).Any())
        {
            return CreateFrontendV2Diagnostic(assetsDirectory, "缺少 /v2/assets 关键静态资产");
        }

        return new StudioStartupPageDecision(
            StudioStartupPageKind.FrontendV2,
            WorkspaceV2Enabled: true,
            FrontendV2PagePath,
            indexPath,
            DiagnosticMessage: null);
    }

    private static StudioStartupPageDecision CreateFrontendV2Diagnostic(
        string requiredPath,
        string reason)
    {
        return new StudioStartupPageDecision(
            StudioStartupPageKind.Diagnostic,
            WorkspaceV2Enabled: true,
            PagePath: null,
            requiredPath,
            $"Studio 2.0 V2 已启用，但前端资产不可用：{reason}。请先执行 Desktop build/publish 生成 wwwroot/v2；系统已禁止回退旧页面，避免混合运行。缺失路径：{requiredPath}");
    }
}
