using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.ProjectVariables;

namespace ClearVision.Product.Infrastructure.Services;

public sealed class JsonFileProjectVariableStateStore : IProjectVariableStateStore
{
    private readonly string _basePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonFileProjectVariableStateStore()
        : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "App_Data", "ProjectVariableStates"))
    {
    }

    public JsonFileProjectVariableStateStore(string basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be empty.", nameof(basePath));
        }

        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public IReadOnlyList<ProjectVariableValueSnapshot> Load(string scopeId, ProjectGlobalVariableSchema schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(schema);

        _gate.Wait();
        try
        {
            var filePath = GetFilePath(scopeId);
            if (!File.Exists(filePath))
            {
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
                .ToDictionary(variable => variable.Id);
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
            var tempPath = filePath + ".tmp";
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
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetFilePath(string scopeId) => Path.Combine(_basePath, $"{BuildStableFileName(scopeId)}.json");

    private string GetLastGoodPath(string scopeId) => Path.Combine(_basePath, $"{BuildStableFileName(scopeId)}.last-good.json");

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
