using System.Security.Cryptography;
using System.Text;

namespace ClearVision.Product.Infrastructure.AI;

internal interface IAiApiKeySecretStore
{
    void Save(string modelId, string apiKey);

    bool TryRead(string modelId, out string apiKey);

    void Delete(string modelId);

    void PruneExcept(IEnumerable<string> modelIds);
}

internal sealed class DpapiFileAiApiKeySecretStore : IAiApiKeySecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ClearVision.AiModel.ApiKey.v1");
    private readonly string _directory;

    public DpapiFileAiApiKeySecretStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Secret directory must not be empty.", nameof(directory));

        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public void Save(string modelId, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Delete(modelId);
            return;
        }

        Directory.CreateDirectory(_directory);
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = Protect(bytes);
        var path = GetSecretPath(modelId);
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, Convert.ToBase64String(protectedBytes));
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public bool TryRead(string modelId, out string apiKey)
    {
        apiKey = string.Empty;
        if (string.IsNullOrWhiteSpace(modelId))
            return false;

        var secretPath = GetSecretPath(modelId);
        if (!File.Exists(secretPath))
            return false;

        try
        {
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(secretPath));
            var bytes = Unprotect(protectedBytes);
            apiKey = Encoding.UTF8.GetString(bytes);
            return !string.IsNullOrEmpty(apiKey);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public void Delete(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return;

        var secretPath = GetSecretPath(modelId);
        if (File.Exists(secretPath))
            File.Delete(secretPath);
    }

    public void PruneExcept(IEnumerable<string> modelIds)
    {
        if (!Directory.Exists(_directory))
            return;

        var allowedPaths = modelIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(GetSecretPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(_directory, "*.dpapi"))
        {
            if (!allowedPaths.Contains(path))
                File.Delete(path);
        }
    }

    private string GetSecretPath(string modelId)
    {
        var idBytes = Encoding.UTF8.GetBytes(modelId.Trim());
        var fileName = Convert.ToHexString(SHA256.HashData(idBytes)).ToLowerInvariant() + ".dpapi";
        return Path.Combine(_directory, fileName);
    }

    private static byte[] Protect(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AI model API keys are protected with Windows DPAPI.");

        return ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] bytes)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AI model API keys are protected with Windows DPAPI.");

        return ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
    }
}
