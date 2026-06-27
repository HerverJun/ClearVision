using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonFileProjectVariableStateStore : IProjectVariableStateStore
{
    private readonly string _basePath;
    private readonly string? _legacyBasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProjectVariableStateStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClearVision",
                "ProjectVariableStates"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectVariableStates"))
    {
    }

    public JsonFileProjectVariableStateStore(string basePath)
        : this(basePath, null)
    {
    }

    private JsonFileProjectVariableStateStore(string basePath, string? legacyBasePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));
        }

        _basePath = basePath;
        _legacyBasePath = legacyBasePath;
        Directory.CreateDirectory(_basePath);
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
            RecoverInterruptedSave(scopeId, filePath);
            if (!File.Exists(filePath))
            {
                var legacyFilePath = GetLegacyFilePath(scopeId);
                if (legacyFilePath != null && File.Exists(legacyFilePath))
                {
                    var legacyJson = File.ReadAllText(legacyFilePath, Encoding.UTF8);
                    if (TryDeserialize(legacyJson, out var legacyState))
                    {
                        Directory.CreateDirectory(_basePath);
                        File.Copy(legacyFilePath, filePath, overwrite: true);
                        var legacyLastGoodPath = GetLegacyLastGoodPath(scopeId);
                        if (legacyLastGoodPath != null && File.Exists(legacyLastGoodPath))
                        {
                            File.Copy(legacyLastGoodPath, GetLastGoodPath(scopeId), overwrite: true);
                        }

                        return ToSnapshots(legacyState);
                    }
                }

                return [];
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            if (!TryDeserialize(json, out var state))
            {
                File.Copy(filePath, filePath + ".corrupt", overwrite: true);
                var lastGoodPath = GetLastGoodPath(scopeId);
                if (File.Exists(lastGoodPath))
                {
                    var lastGood = File.ReadAllText(lastGoodPath, Encoding.UTF8);
                    if (TryDeserialize(lastGood, out state))
                    {
                        return ToSnapshots(state);
                    }
                }

                throw new InvalidDataException($"Project variable state JSON is corrupt and no valid last-good copy exists. ScopeId={scopeId}");
            }

            var expectedSchemaHash = ProjectGlobalVariableSchemaValidator.ComputeSchemaHash(schema);
            if (!string.Equals(state.SchemaHash, expectedSchemaHash, StringComparison.Ordinal))
            {
                var lastGoodPath = GetLastGoodPath(scopeId);
                if (File.Exists(lastGoodPath))
                {
                    var lastGoodJson = File.ReadAllText(lastGoodPath, Encoding.UTF8);
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
            Directory.CreateDirectory(_basePath);
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

            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(filePath))
            {
                File.Copy(filePath, lastGoodPath, overwrite: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
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
            File.Delete(GetFilePath(scopeId));
            File.Delete(GetLastGoodPath(scopeId));
            File.Delete(GetTempPath(scopeId));
            var legacyFilePath = GetLegacyFilePath(scopeId);
            if (legacyFilePath != null)
            {
                File.Delete(legacyFilePath);
                var legacyLastGoodPath = GetLegacyLastGoodPath(scopeId);
                if (legacyLastGoodPath != null)
                {
                    File.Delete(legacyLastGoodPath);
                }

                var legacyTempPath = GetLegacyTempPath(scopeId);
                if (legacyTempPath != null)
                {
                    File.Delete(legacyTempPath);
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

    private void RecoverInterruptedSave(string scopeId, string filePath)
    {
        var tempPath = GetTempPath(scopeId);
        if (!File.Exists(tempPath))
        {
            return;
        }

        if (File.Exists(filePath))
        {
            File.Delete(tempPath);
            return;
        }

        var tempJson = File.ReadAllText(tempPath, Encoding.UTF8);
        if (!TryDeserialize(tempJson, out _))
        {
            File.Delete(tempPath);
            return;
        }

        File.Move(tempPath, filePath, overwrite: true);
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
