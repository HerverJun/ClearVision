using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonFileProjectVariableStateStore : IProjectVariableStateStore
{
    private readonly string _basePath;
    private readonly string? _legacyBasePath;
    private readonly IProjectVariableStateFileSystem _fileSystem;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProjectVariableStateStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "ProjectVariableStates"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectVariableStates"),
            PhysicalProjectVariableStateFileSystem.Instance)
    {
    }

    public JsonFileProjectVariableStateStore(string basePath)
        : this(basePath, null, PhysicalProjectVariableStateFileSystem.Instance)
    {
    }

    public JsonFileProjectVariableStateStore(string basePath, IProjectVariableStateFileSystem fileSystem)
        : this(basePath, null, fileSystem)
    {
    }

    private JsonFileProjectVariableStateStore(
        string basePath,
        string? legacyBasePath,
        IProjectVariableStateFileSystem fileSystem)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));
        }

        _basePath = basePath;
        _legacyBasePath = legacyBasePath;
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _fileSystem.CreateDirectory(_basePath);
    }

    public string BasePath => _basePath;

    public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(schema);

        _gate.Wait();
        try
        {
            var filePath = GetFilePath(scopeId);
            var expectedSchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema);
            RecoverInterruptedSave(scopeId, filePath, expectedSchemaHash);
            if (!_fileSystem.FileExists(filePath))
            {
                var legacyFilePath = GetLegacyFilePath(scopeId);
                if (legacyFilePath != null && _fileSystem.FileExists(legacyFilePath))
                {
                    var legacyJson = _fileSystem.ReadAllText(legacyFilePath, Encoding.UTF8);
                    if (TryDeserialize(legacyJson, out var legacyState))
                    {
                        _fileSystem.CreateDirectory(_basePath);
                        _fileSystem.Copy(legacyFilePath, filePath, overwrite: true);
                        var legacyLastGoodPath = GetLegacyLastGoodPath(scopeId);
                        if (legacyLastGoodPath != null && _fileSystem.FileExists(legacyLastGoodPath))
                        {
                            _fileSystem.Copy(legacyLastGoodPath, GetLastGoodPath(scopeId), overwrite: true);
                        }

                        return ToSnapshots(legacyState);
                    }
                }

                return [];
            }

            var json = _fileSystem.ReadAllText(filePath, Encoding.UTF8);
            if (!TryDeserialize(json, out var state))
            {
                _fileSystem.Copy(filePath, filePath + ".corrupt", overwrite: true);
                var lastGoodPath = GetLastGoodPath(scopeId);
                if (_fileSystem.FileExists(lastGoodPath))
                {
                    var lastGood = _fileSystem.ReadAllText(lastGoodPath, Encoding.UTF8);
                    if (TryDeserialize(lastGood, out state))
                    {
                        return ToSnapshots(state);
                    }
                }

                throw new InvalidDataException($"Project variable state JSON is corrupt and no valid last-good copy exists. ScopeId={scopeId}");
            }

            if (!string.Equals(state.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
            {
                var lastGoodPath = GetLastGoodPath(scopeId);
                if (_fileSystem.FileExists(lastGoodPath))
                {
                    var lastGoodJson = _fileSystem.ReadAllText(lastGoodPath, Encoding.UTF8);
                    if (TryDeserialize(lastGoodJson, out var lastGoodState) &&
                        string.Equals(lastGoodState.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
                    {
                        return ToSnapshots(lastGoodState);
                    }
                }
            }

            return ToSnapshots(state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Save(string scopeId, ProjectGlobalVariableSchema schema, IReadOnlyList<ProjectVariableValueSnapshot> snapshots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(snapshots);

        _gate.Wait();
        try
        {
            _fileSystem.CreateDirectory(_basePath);
            var definitionsById = schema.Variables
                .Where(variable => variable.Id != Guid.Empty)
                .GroupBy(variable => variable.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var state = new ProjectVariableStateFile
            {
                ScopeId = scopeId,
                SchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema),
                SavedAtUtc = DateTimeOffset.UtcNow,
                Variables = snapshots
                    .OrderBy(snapshot => snapshot.VariableId)
                    .Select(snapshot => new ProjectVariableStateFileEntry
                    {
                        VariableId = snapshot.VariableId,
                        Value = SerializeSnapshotValue(snapshot, definitionsById),
                        Version = snapshot.Version,
                        UpdatedAtUtc = snapshot.UpdatedAtUtc,
                        UpdatedBy = snapshot.UpdatedBy,
                        RunId = snapshot.RunId,
                        OperatorId = snapshot.OperatorId
                    })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(state, ProjectVariableJson.Options);
            using var _ = JsonDocument.Parse(json);

            var filePath = GetFilePath(scopeId);
            var tempPath = GetTempPath(scopeId);
            var lastGoodPath = GetLastGoodPath(scopeId);

            _fileSystem.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (_fileSystem.FileExists(filePath))
            {
                _fileSystem.Copy(filePath, lastGoodPath, overwrite: true);
            }

            _fileSystem.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Delete(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        _gate.Wait();
        try
        {
            _fileSystem.DeleteFile(GetFilePath(scopeId));
            _fileSystem.DeleteFile(GetLastGoodPath(scopeId));
            _fileSystem.DeleteFile(GetTempPath(scopeId));
            var legacyFilePath = GetLegacyFilePath(scopeId);
            if (legacyFilePath != null)
            {
                _fileSystem.DeleteFile(legacyFilePath);
                var legacyLastGoodPath = GetLegacyLastGoodPath(scopeId);
                if (legacyLastGoodPath != null)
                {
                    _fileSystem.DeleteFile(legacyLastGoodPath);
                }

                var legacyTempPath = GetLegacyTempPath(scopeId);
                if (legacyTempPath != null)
                {
                    _fileSystem.DeleteFile(legacyTempPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetFilePath(string scopeId) => Path.Combine(_basePath, $"{BuildStableFileName(scopeId)}.json");

    private string GetLastGoodPath(string scopeId) => Path.Combine(_basePath, $"{BuildStableFileName(scopeId)}.last-good.json");

    private string GetTempPath(string scopeId) => GetFilePath(scopeId) + ".tmp";

    private string? GetLegacyFilePath(string scopeId) =>
        _legacyBasePath == null ? null : Path.Combine(_legacyBasePath, $"{BuildStableFileName(scopeId)}.json");

    private string? GetLegacyLastGoodPath(string scopeId) =>
        _legacyBasePath == null ? null : Path.Combine(_legacyBasePath, $"{BuildStableFileName(scopeId)}.last-good.json");

    private string? GetLegacyTempPath(string scopeId)
    {
        var legacyFilePath = GetLegacyFilePath(scopeId);
        return legacyFilePath == null ? null : legacyFilePath + ".tmp";
    }

    private void RecoverInterruptedSave(string scopeId, string filePath, string expectedSchemaHash)
    {
        var tempPath = GetTempPath(scopeId);
        if (!_fileSystem.FileExists(tempPath))
        {
            return;
        }

        if (_fileSystem.FileExists(filePath))
        {
            _fileSystem.DeleteFile(tempPath);
            return;
        }

        var tempJson = _fileSystem.ReadAllText(tempPath, Encoding.UTF8);
        if (!TryDeserialize(tempJson, out var tempState) ||
            !string.Equals(tempState.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
        {
            _fileSystem.DeleteFile(tempPath);
            return;
        }

        _fileSystem.Move(tempPath, filePath, overwrite: true);
    }

    private static string BuildStableFileName(string scopeId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scopeId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryDeserialize(string json, out ProjectVariableStateFile state)
    {
        try
        {
            state = JsonSerializer.Deserialize<ProjectVariableStateFile>(json, ProjectVariableJson.Options)
                ?? new ProjectVariableStateFile();
            return state.SchemaVersion == 1;
        }
        catch (JsonException)
        {
            state = new ProjectVariableStateFile();
            return false;
        }
    }

    private static IReadOnlyList<ProjectVariableValueSnapshot> ToSnapshots(ProjectVariableStateFile state)
    {
        return state.Variables
            .Where(entry => entry.VariableId != Guid.Empty)
            .Select(entry => new ProjectVariableValueSnapshot(
                entry.VariableId,
                entry.Value.Clone(),
                entry.Version,
                entry.UpdatedAtUtc,
                entry.UpdatedBy,
                entry.RunId,
                entry.OperatorId))
            .ToList();
    }

    private static JsonElement SerializeSnapshotValue(
        ProjectVariableValueSnapshot snapshot,
        IReadOnlyDictionary<Guid, ProjectGlobalVariableDefinition> definitionsById)
    {
        if (definitionsById.TryGetValue(snapshot.VariableId, out var definition) &&
            definition.ValueType == ProjectGlobalVariableValueType.Int64)
        {
            return snapshot.Value.ValueKind == JsonValueKind.String
                ? JsonSerializer.SerializeToElement(snapshot.Value.GetString() ?? string.Empty)
                : JsonSerializer.SerializeToElement(snapshot.Value.GetRawText());
        }

        return snapshot.Value.Clone();
    }

    private sealed class ProjectVariableStateFile
    {
        public int SchemaVersion { get; init; } = 1;

        public string ScopeId { get; init; } = string.Empty;

        public string SchemaHash { get; init; } = string.Empty;

        public DateTimeOffset SavedAtUtc { get; init; }

        public List<ProjectVariableStateFileEntry> Variables { get; init; } = [];
    }

    private sealed class ProjectVariableStateFileEntry
    {
        public Guid VariableId { get; init; }

        public JsonElement Value { get; init; }

        public long Version { get; init; }

        public DateTimeOffset UpdatedAtUtc { get; init; }

        public ProjectVariableUpdatedBy UpdatedBy { get; init; }

        public Guid? RunId { get; init; }

        public Guid? OperatorId { get; init; }
    }
}

public interface IProjectVariableStateFileSystem
{
    void CreateDirectory(string path);

    bool FileExists(string path);

    string ReadAllText(string path, Encoding encoding);

    void WriteAllText(string path, string contents, Encoding encoding);

    void Copy(string sourceFileName, string destFileName, bool overwrite);

    void Move(string sourceFileName, string destFileName, bool overwrite);

    void DeleteFile(string path);
}

internal sealed class PhysicalProjectVariableStateFileSystem : IProjectVariableStateFileSystem
{
    public static PhysicalProjectVariableStateFileSystem Instance { get; } = new();

    private PhysicalProjectVariableStateFileSystem()
    {
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path, Encoding encoding) => File.ReadAllText(path, encoding);

    public void WriteAllText(string path, string contents, Encoding encoding) =>
        File.WriteAllText(path, contents, encoding);

    public void Copy(string sourceFileName, string destFileName, bool overwrite) =>
        File.Copy(sourceFileName, destFileName, overwrite);

    public void Move(string sourceFileName, string destFileName, bool overwrite) =>
        File.Move(sourceFileName, destFileName, overwrite);

    public void DeleteFile(string path) => File.Delete(path);
}
