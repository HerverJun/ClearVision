using Acme.Product.Core.Interfaces;

namespace Acme.Product.Infrastructure.Services;

public class JsonFileProjectFlowStorage : IProjectFlowStorage
{
    private readonly string _basePath;

    public JsonFileProjectFlowStorage()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectFlows"))
    {
    }

    public JsonFileProjectFlowStorage(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));

        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task SaveFlowJsonAsync(Guid projectId, string flowJson)
    {
        ArgumentNullException.ThrowIfNull(flowJson);

        Directory.CreateDirectory(_basePath);
        var filePath = GetFilePath(projectId);
        await File.WriteAllTextAsync(filePath, flowJson);
    }

    public async Task<string?> LoadFlowJsonAsync(Guid projectId)
    {
        var filePath = GetFilePath(projectId);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(filePath);
    }

    private string GetFilePath(Guid projectId)
    {
        return Path.Combine(_basePath, $"{projectId}.json");
    }
}
