namespace ClearVision.Product.Desktop;

internal static class DesktopWebRootResolver
{
#if DEBUG
    private const bool DefaultPreferProjectSource = true;
#else
    private const bool DefaultPreferProjectSource = false;
#endif

    public static string Resolve(
        string? baseDirectory = null,
        bool preferProjectSource = DefaultPreferProjectSource)
    {
        var normalizedBaseDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(baseDirectory)
                ? ResolveDefaultBaseDirectory()
                : baseDirectory);

        if (preferProjectSource &&
            TryFindProjectWwwRoot(normalizedBaseDirectory, out var projectWwwRoot))
        {
            return projectWwwRoot;
        }

        return Path.GetFullPath(Path.Combine(normalizedBaseDirectory, "wwwroot"));
    }

    public static string ResolveStudioUi(string? baseDirectory = null)
    {
        var normalizedBaseDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(baseDirectory)
                ? ResolveDefaultBaseDirectory()
                : baseDirectory);

        return Path.GetFullPath(Path.Combine(normalizedBaseDirectory, "wwwroot", "studio"));
    }

    internal static string ResolveDefaultBaseDirectory(
        string? appContextBaseDirectory = null,
        string? processPath = null)
    {
        var appBaseDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(appContextBaseDirectory)
                ? AppContext.BaseDirectory
                : appContextBaseDirectory);
        if (Directory.Exists(Path.Combine(appBaseDirectory, "wwwroot")))
        {
            return appBaseDirectory;
        }

        var resolvedProcessPath = string.IsNullOrWhiteSpace(processPath)
            ? Environment.ProcessPath
            : processPath;
        if (string.IsNullOrWhiteSpace(resolvedProcessPath))
        {
            return appBaseDirectory;
        }

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(resolvedProcessPath));
        return !string.IsNullOrWhiteSpace(executableDirectory) &&
            Directory.Exists(Path.Combine(executableDirectory, "wwwroot"))
            ? Path.GetFullPath(executableDirectory)
            : appBaseDirectory;
    }

    private static bool TryFindProjectWwwRoot(string baseDirectory, out string wwwRoot)
    {
        var current = new DirectoryInfo(baseDirectory);
        while (current != null)
        {
            var projectFile = Path.Combine(current.FullName, "ClearVision.Product.Desktop.csproj");
            var candidateWwwRoot = Path.Combine(current.FullName, "wwwroot");
            var indexFile = Path.Combine(candidateWwwRoot, "index.html");

            if (File.Exists(projectFile) && File.Exists(indexFile))
            {
                wwwRoot = Path.GetFullPath(candidateWwwRoot);
                return true;
            }

            current = current.Parent;
        }

        wwwRoot = string.Empty;
        return false;
    }
}
