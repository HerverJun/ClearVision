namespace ClearVision.Product.Application.Services;

public static class InspectionImagePersistencePaths
{
    public static IReadOnlyList<string> ResolveImageSaveRoots(string? configuredPath)
    {
        var roots = new List<string>();
        var fallbackRoot = NormalizeRoot(GetFallbackImageSaveRoot());

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var configuredRoot = NormalizeRoot(configuredPath);
                if (!string.Equals(configuredRoot, fallbackRoot, StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(configuredRoot);
                }
            }
            catch
            {
            }
        }

        roots.Add(fallbackRoot);
        return roots;
    }

    public static string GetFallbackImageSaveRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "Images");
    }

    private static string NormalizeRoot(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    }
}
