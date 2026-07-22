using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.Communication.Gr;

public sealed class GrRegisterMapCatalog
{
    private const string ResourceSuffix = "Communication.Gr.Templates.gr-v3.0-register-map.json";
    private readonly Lazy<GrRegisterMapTemplate> _template = new(LoadTemplate);

    public GrRegisterMapTemplate GetTemplate() => _template.Value;

    private static GrRegisterMapTemplate LoadTemplate()
    {
        var assembly = typeof(GrRegisterMapCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded GR template '{resourceName}' was not found.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var template = JsonSerializer.Deserialize<GrRegisterMapTemplate>(bytes, JsonOptions)
            ?? throw new InvalidOperationException("Embedded GR template is empty or invalid.");
        return template with { Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record GrRegisterMapTemplate
{
    public string TemplateId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public int DefaultPort { get; init; }
    public int DefaultUnitId { get; init; }
    public GrRegisterRange StatusRange { get; init; } = new();
    public GrWritePolicy WritePolicy { get; init; } = new();
    public IReadOnlyList<GrRegisterDefinition> Registers { get; init; } = [];
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record GrRegisterRange
{
    public int StartAddress { get; init; }
    public int Count { get; init; }
    public string FunctionCode { get; init; } = "ReadHolding";
}

public sealed record GrWritePolicy
{
    public bool EnabledByDefault { get; init; }
    public IReadOnlyList<int> AllowedAddresses { get; init; } = [];
    public IReadOnlyList<int> DisabledAddresses { get; init; } = [];
}

public sealed record GrRegisterDefinition
{
    public int Address { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ValueType { get; init; } = "raw";
    public IReadOnlyDictionary<string, string> EnumValues { get; init; } = new Dictionary<string, string>();
}
