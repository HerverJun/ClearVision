using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Infrastructure.AI.Runtime;

public sealed class ModelCatalogDocument
{
    [JsonPropertyName("models")]
    public List<ModelCatalogEntry> Models { get; init; } = [];
}

public sealed partial class ModelCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string ArtifactPath { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("model_sha256")]
    public string ModelSha256 { get; init; } = string.Empty;

    [JsonPropertyName("input_size")]
    public int[] InputSize { get; init; } = [];

    [JsonPropertyName("input_shape")]
    public int[] InputShape { get; init; } = [];

    [JsonPropertyName("num_classes")]
    public int NumClasses { get; init; }

    [JsonPropertyName("class_names")]
    public string[] ClassNames { get; init; } = [];

    [JsonPropertyName("classes")]
    public string[] Classes { get; init; } = [];

    [JsonPropertyName("execution_provider")]
    public string ExecutionProvider { get; init; } = "cpu";

    [JsonPropertyName("preprocess")]
    public Dictionary<string, JsonElement> Preprocess { get; init; } = [];

    [JsonPropertyName("postprocess")]
    public Dictionary<string, JsonElement> Postprocess { get; init; } = [];
}

public sealed class ResolvedModelCatalogEntry
{
    public required ModelCatalogEntry Entry { get; init; }

    public required string CatalogPath { get; init; }

    public required string ArtifactPath { get; init; }
}

public sealed class ResolvedModelTarget
{
    public required string ResolvedPath { get; init; }

    public required string Source { get; init; }

    public string ExplicitPath { get; init; } = string.Empty;

    public string ModelId { get; init; } = string.Empty;

    public string CatalogPath { get; init; } = string.Empty;

    public ModelCatalogEntry? Entry { get; init; }

    public Dictionary<string, object> ToProvenancePayload()
    {
        var payload = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["ResolutionSource"] = Source,
            ["ResolvedPath"] = ResolvedPath,
            ["ExplicitPath"] = ExplicitPath,
            ["ModelId"] = ModelId,
            ["CatalogPath"] = CatalogPath
        };

        if (Entry == null)
        {
            return payload;
        }

        payload["ModelName"] = Entry.Name;
        payload["ModelType"] = Entry.Type;
        payload["ModelVersion"] = Entry.Version;
        payload["CatalogArtifactPath"] = Entry.ArtifactPath;
        payload["CatalogSource"] = Entry.Source ?? string.Empty;
        payload["CatalogLicense"] = Entry.License ?? string.Empty;
        payload["ModelSha256"] = Entry.ModelSha256;
        payload["ExecutionProvider"] = Entry.ExecutionProvider;
        payload["InputSize"] = Entry.InputSize;
        payload["InputShape"] = Entry.InputShape;
        payload["NumClasses"] = Entry.NumClasses;
        payload["ClassNames"] = Entry.ResolvedClassNames;
        payload["Preprocess"] = Entry.Preprocess;
        payload["Postprocess"] = Entry.Postprocess;
        payload["CatalogEntry"] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = Entry.Id,
            ["Name"] = Entry.Name,
            ["Type"] = Entry.Type,
            ["Version"] = Entry.Version,
            ["ArtifactPath"] = Entry.ArtifactPath,
            ["Source"] = Entry.Source ?? string.Empty,
            ["License"] = Entry.License ?? string.Empty,
            ["ModelSha256"] = Entry.ModelSha256,
            ["ExecutionProvider"] = Entry.ExecutionProvider,
            ["InputSize"] = Entry.InputSize,
            ["InputShape"] = Entry.InputShape,
            ["NumClasses"] = Entry.NumClasses,
            ["ClassNames"] = Entry.ResolvedClassNames,
            ["Preprocess"] = Entry.Preprocess,
            ["Postprocess"] = Entry.Postprocess
        };

        return payload;
    }
}

public sealed partial class ModelCatalogEntry
{
    public string[] ResolvedClassNames => ClassNames.Length > 0 ? ClassNames : Classes;
}

public static class ModelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string ResolveCatalogPath(string? catalogPath = null)
    {
        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            var resolved = Path.GetFullPath(catalogPath);
            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException($"Model catalog not found: {resolved}", resolved);
            }

            return resolved;
        }

        foreach (var start in EnumerateCandidateRoots())
        {
            var resolved = SearchUpwards(start);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        throw new FileNotFoundException("Model catalog not found. Expected models/model_catalog.json.");
    }

    public static ModelCatalogDocument Load(string? catalogPath = null)
    {
        var resolvedCatalogPath = ResolveCatalogPath(catalogPath);
        var json = File.ReadAllText(resolvedCatalogPath);
        var document = JsonSerializer.Deserialize<ModelCatalogDocument>(json, JsonOptions) ?? new ModelCatalogDocument();
        return new ModelCatalogDocument
        {
            Models = document.Models
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToList()
        };
    }

    public static bool TryResolve(
        string? modelId,
        string? catalogPath,
        IReadOnlyCollection<string>? expectedTypes,
        out ResolvedModelCatalogEntry? resolved,
        out string? error)
    {
        resolved = null;
        error = null;

        if (string.IsNullOrWhiteSpace(modelId))
        {
            error = "ModelId is required.";
            return false;
        }

        ModelCatalogDocument document;
        string resolvedCatalogPath;
        try
        {
            resolvedCatalogPath = ResolveCatalogPath(catalogPath);
            document = Load(resolvedCatalogPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var entry = document.Models.FirstOrDefault(x => string.Equals(x.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            error = $"ModelId not found in catalog: {modelId}";
            return false;
        }

        if (expectedTypes != null && expectedTypes.Count > 0)
        {
            var expectedTypeSet = new HashSet<string>(expectedTypes.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            if (expectedTypeSet.Count > 0 && !expectedTypeSet.Contains(entry.Type))
            {
                error = $"Model '{modelId}' type '{entry.Type}' is not supported here. Expected: {string.Join(", ", expectedTypeSet)}";
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(entry.ArtifactPath))
        {
            error = $"Model '{modelId}' does not define a path in the catalog.";
            return false;
        }

        var artifactPath = ResolveArtifactPath(resolvedCatalogPath, entry.ArtifactPath);
        resolved = new ResolvedModelCatalogEntry
        {
            Entry = entry,
            CatalogPath = resolvedCatalogPath,
            ArtifactPath = artifactPath
        };
        return true;
    }

    public static string ResolveExplicitOrCatalogPath(
        string? explicitPath,
        string? modelId,
        string? catalogPath,
        IReadOnlyCollection<string>? expectedTypes,
        out ModelCatalogEntry? entry)
    {
        var resolved = ResolveExplicitOrCatalog(explicitPath, modelId, catalogPath, expectedTypes);
        entry = resolved.Entry;
        return resolved.ResolvedPath;
    }

    public static ResolvedModelTarget ResolveExplicitOrCatalog(
        string? explicitPath,
        string? modelId,
        string? catalogPath,
        IReadOnlyCollection<string>? expectedTypes)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return new ResolvedModelTarget
            {
                ResolvedPath = Path.GetFullPath(explicitPath),
                Source = "ExplicitPath",
                ExplicitPath = explicitPath,
                ModelId = string.Empty,
                CatalogPath = string.Empty,
                Entry = null
            };
        }

        if (!TryResolve(modelId, catalogPath, expectedTypes, out var resolved, out var error) || resolved == null)
        {
            throw new InvalidOperationException(error ?? "Unable to resolve model path.");
        }

        return new ResolvedModelTarget
        {
            ResolvedPath = resolved.ArtifactPath,
            Source = "ModelCatalog",
            ExplicitPath = string.Empty,
            ModelId = resolved.Entry.Id,
            CatalogPath = resolved.CatalogPath,
            Entry = resolved.Entry
        };
    }

    private static IEnumerable<string> EnumerateCandidateRoots()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static string? SearchUpwards(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "models", "model_catalog.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string ResolveArtifactPath(string catalogPath, string artifactPath)
    {
        if (Path.IsPathRooted(artifactPath))
        {
            return artifactPath;
        }

        var catalogDirectory = Path.GetDirectoryName(catalogPath) ?? Directory.GetCurrentDirectory();
        var repoRoot = Directory.GetParent(catalogDirectory)?.FullName ?? catalogDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(catalogDirectory, artifactPath)),
            Path.GetFullPath(Path.Combine(repoRoot, artifactPath))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[1];
    }
}
