using Acme.Product.Application.Services;

namespace Acme.Product.Tests.TestSupport;

internal sealed class FallbackImageDirectorySnapshot
{
    private readonly string _fallbackRoot;
    private readonly bool _fallbackRootExisted;
    private readonly HashSet<string> _existingDirectories;

    private FallbackImageDirectorySnapshot(
        string fallbackRoot,
        bool fallbackRootExisted,
        HashSet<string> existingDirectories)
    {
        _fallbackRoot = fallbackRoot;
        _fallbackRootExisted = fallbackRootExisted;
        _existingDirectories = existingDirectories;
    }

    public static FallbackImageDirectorySnapshot Capture()
    {
        var fallbackRoot = InspectionImagePersistencePaths.GetFallbackImageSaveRoot();
        var fallbackRootExisted = Directory.Exists(fallbackRoot);
        var existingDirectories = fallbackRootExisted
            ? Directory
                .EnumerateDirectories(fallbackRoot, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new FallbackImageDirectorySnapshot(
            Path.GetFullPath(fallbackRoot),
            fallbackRootExisted,
            existingDirectories);
    }

    public void DeleteSavedFiles(Guid projectId, Guid resultId)
    {
        if (!Directory.Exists(_fallbackRoot))
        {
            return;
        }

        var deletedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory
                     .EnumerateFiles(_fallbackRoot, $"{projectId:N}_{resultId:N}_*", SearchOption.AllDirectories)
                     .ToArray())
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                deletedDirectories.Add(Path.GetFullPath(directory));
            }

            File.Delete(path);
        }

        foreach (var directory in deletedDirectories.OrderByDescending(item => item.Length))
        {
            DeleteNewEmptyDirectoriesUpTo(directory);
        }

        if (!_fallbackRootExisted &&
            Directory.Exists(_fallbackRoot) &&
            !Directory.EnumerateFileSystemEntries(_fallbackRoot).Any())
        {
            Directory.Delete(_fallbackRoot);
        }
    }

    private void DeleteNewEmptyDirectoriesUpTo(string directoryPath)
    {
        var current = Path.GetFullPath(directoryPath);

        while (!string.Equals(current, _fallbackRoot, StringComparison.OrdinalIgnoreCase) &&
               Directory.Exists(current) &&
               !_existingDirectories.Contains(current) &&
               !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current) ?? _fallbackRoot;
        }
    }
}
