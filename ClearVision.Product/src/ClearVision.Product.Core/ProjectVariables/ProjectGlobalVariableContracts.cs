using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClearVision.Product.Core.ProjectVariables;

public enum ProjectGlobalVariableValueType
{
    String = 0,
    Int64 = 1,
    Double = 2,
    Boolean = 3
}

public enum ProjectVariableUpdatedBy
{
    Initial = 0,
    StudioManual = 1,
    StationManual = 2,
    OperatorOutput = 3,
    VariableWrite = 4,
    VariableIncrement = 5,
    Reset = 6
}

public enum ProjectVariableConversionMode
{
    Exact = 0,
    Round = 1,
    Floor = 2,
    Ceiling = 3,
    Truncate = 4
}

[JsonConverter(typeof(ProjectGlobalVariableDefinitionJsonConverter))]
public sealed class ProjectGlobalVariableDefinition
{
    private ProjectVariableNumericBound? _min;
    private ProjectVariableNumericBound? _max;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ProjectGlobalVariableValueType ValueType { get; set; }

    public JsonElement InitialValue { get; set; } = JsonSerializer.SerializeToElement("");

    [JsonIgnore]
    public double? Min
    {
        get => _min.HasValue && _min.Value.TryGetDouble(out var value) ? value : null;
        set => _min = value.HasValue ? ProjectVariableNumericBound.FromDouble(value.Value) : (ProjectVariableNumericBound?)null;
    }

    [JsonPropertyName("min")]
    public ProjectVariableNumericBound? MinBound
    {
        get => _min;
        set => _min = value;
    }

    [JsonIgnore]
    public double? Max
    {
        get => _max.HasValue && _max.Value.TryGetDouble(out var value) ? value : null;
        set => _max = value.HasValue ? ProjectVariableNumericBound.FromDouble(value.Value) : (ProjectVariableNumericBound?)null;
    }

    [JsonPropertyName("max")]
    public ProjectVariableNumericBound? MaxBound
    {
        get => _max;
        set => _max = value;
    }

    public bool ManualWriteAllowed { get; set; } = true;

    public bool IncludeInResultMetadata { get; set; }

    public int Order { get; set; }
}

public sealed class ProjectGlobalVariableSourceBinding
{
    public Guid Id { get; set; }

    public Guid VariableId { get; set; }

    public Guid OperatorId { get; set; }

    public Guid OutputPortId { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string OutputPortName { get; set; } = string.Empty;

    public ProjectVariableConversionMode ConversionMode { get; set; } = ProjectVariableConversionMode.Exact;

    public string? Expression { get; set; }
}

public sealed class ProjectGlobalVariableTargetBinding
{
    public Guid Id { get; set; }

    public Guid VariableId { get; set; }

    public Guid OperatorId { get; set; }

    public Guid ParameterId { get; set; }

    public string OperatorName { get; set; } = string.Empty;

    public string ParameterName { get; set; } = string.Empty;

    public ProjectVariableConversionMode ConversionMode { get; set; } = ProjectVariableConversionMode.Exact;

    public string? Expression { get; set; }
}

public sealed class ProjectGlobalVariableSchema
{
    public string SchemaVersion { get; set; } = "1.0";

    public List<ProjectGlobalVariableDefinition> Variables { get; set; } = [];

    public List<ProjectGlobalVariableSourceBinding> SourceBindings { get; set; } = [];

    public List<ProjectGlobalVariableTargetBinding> TargetBindings { get; set; } = [];

    public static ProjectGlobalVariableSchema Empty { get; } = new();
}

public sealed record ProjectVariableValueSnapshot(
    Guid VariableId,
    JsonElement Value,
    long Version,
    DateTimeOffset UpdatedAtUtc,
    ProjectVariableUpdatedBy UpdatedBy,
    Guid? RunId,
    Guid? OperatorId);

public enum ProjectGlobalVariableDiagnosticSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2
}

public sealed record ProjectGlobalVariableDiagnostic(
    string Code,
    string Message,
    Guid? VariableId = null,
    Guid? OperatorId = null,
    Guid? PortId = null,
    Guid? ParameterId = null,
    ProjectGlobalVariableDiagnosticSeverity Severity = ProjectGlobalVariableDiagnosticSeverity.Error);

[JsonConverter(typeof(ProjectVariableNumericBoundJsonConverter))]
public readonly struct ProjectVariableNumericBound
{
    private readonly string _text;

    public ProjectVariableNumericBound(string text)
    {
        _text = text;
    }

    public string Text => _text ?? string.Empty;

    public static ProjectVariableNumericBound FromInt64(long value) =>
        new(value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public static ProjectVariableNumericBound FromDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException("Numeric bound must be finite.");
        }

        return new(value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture));
    }

    public bool TryGetInt64(out long value) =>
        long.TryParse(Text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);

    public bool TryGetDouble(out double value) =>
        double.TryParse(Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value);

    public override string ToString() => Text;

    public static implicit operator ProjectVariableNumericBound(long value) => FromInt64(value);

    public static implicit operator ProjectVariableNumericBound(int value) => FromInt64(value);

    public static implicit operator ProjectVariableNumericBound(double value) => FromDouble(value);

    public static implicit operator ProjectVariableNumericBound(string value) => new(value);
}

public sealed class ProjectVariableNumericBoundJsonConverter : JsonConverter<ProjectVariableNumericBound>
{
    public override ProjectVariableNumericBound Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new ProjectVariableNumericBound(reader.GetString() ?? string.Empty),
            JsonTokenType.Number => new ProjectVariableNumericBound(reader.HasValueSequence
                ? System.Text.Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                : System.Text.Encoding.UTF8.GetString(reader.ValueSpan)),
            _ => throw new JsonException("Numeric bound must be a JSON number or decimal string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ProjectVariableNumericBound value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Text);
    }
}

public sealed class ProjectGlobalVariableDefinitionJsonConverter : JsonConverter<ProjectGlobalVariableDefinition>
{
    public override ProjectGlobalVariableDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var valueType = ReadValueType(root);
        var definition = new ProjectGlobalVariableDefinition
        {
            Id = ReadGuid(root, "id", "Id"),
            Name = ReadString(root, "name", "Name") ?? string.Empty,
            DisplayName = ReadString(root, "displayName", "DisplayName") ?? string.Empty,
            Description = ReadString(root, "description", "Description"),
            ValueType = valueType,
            InitialValue = ReadInitialValue(root, valueType),
            MinBound = ReadBound(root, "min", "Min", "MinBound"),
            MaxBound = ReadBound(root, "max", "Max", "MaxBound"),
            ManualWriteAllowed = ReadBoolean(root, true, "manualWriteAllowed", "ManualWriteAllowed"),
            IncludeInResultMetadata = ReadBoolean(root, false, "includeInResultMetadata", "IncludeInResultMetadata"),
            Order = ReadInt32(root, 0, "order", "Order")
        };

        return definition;
    }

    public override void Write(Utf8JsonWriter writer, ProjectGlobalVariableDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("displayName", value.DisplayName);
        if (value.Description == null)
        {
            writer.WriteNull("description");
        }
        else
        {
            writer.WriteString("description", value.Description);
        }

        writer.WriteString("valueType", value.ValueType.ToString());
        writer.WritePropertyName("initialValue");
        WriteInitialValue(writer, value);
        WriteBound(writer, "min", value.MinBound, options);
        WriteBound(writer, "max", value.MaxBound, options);
        writer.WriteBoolean("manualWriteAllowed", value.ManualWriteAllowed);
        writer.WriteBoolean("includeInResultMetadata", value.IncludeInResultMetadata);
        writer.WriteNumber("order", value.Order);
        writer.WriteEndObject();
    }

    private static JsonElement ReadInitialValue(JsonElement root, ProjectGlobalVariableValueType valueType)
    {
        if (!TryGetProperty(root, out var element, "initialValue", "InitialValue"))
        {
            return JsonSerializer.SerializeToElement("");
        }

        if (valueType == ProjectGlobalVariableValueType.Int64 &&
            ProjectVariableValueConverter.TryConvertToVariableValue(element, valueType, out var converted, out _))
        {
            return converted;
        }

        return element.Clone();
    }

    private static void WriteInitialValue(Utf8JsonWriter writer, ProjectGlobalVariableDefinition value)
    {
        if (value.ValueType == ProjectGlobalVariableValueType.Int64 &&
            ProjectVariableValueConverter.TryConvertToVariableValue(value.InitialValue, value.ValueType, out var converted, out _))
        {
            writer.WriteStringValue(converted.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        value.InitialValue.WriteTo(writer);
    }

    private static ProjectVariableNumericBound? ReadBound(JsonElement root, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => new ProjectVariableNumericBound(element.GetString() ?? string.Empty),
            JsonValueKind.Number => new ProjectVariableNumericBound(element.GetRawText()),
            _ => (ProjectVariableNumericBound?)null
        };
    }

    private static void WriteBound(
        Utf8JsonWriter writer,
        string propertyName,
        ProjectVariableNumericBound? value,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(propertyName);
        if (value.HasValue)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }

    private static ProjectGlobalVariableValueType ReadValueType(JsonElement root)
    {
        if (!TryGetProperty(root, out var element, "valueType", "ValueType"))
        {
            return ProjectGlobalVariableValueType.String;
        }

        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse<ProjectGlobalVariableValueType>(element.GetString(), ignoreCase: true, out var textValue))
        {
            return textValue;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numberValue))
        {
            return Enum.IsDefined(typeof(ProjectGlobalVariableValueType), numberValue)
                ? (ProjectGlobalVariableValueType)numberValue
                : ProjectGlobalVariableValueType.String;
        }

        return ProjectGlobalVariableValueType.String;
    }

    private static Guid ReadGuid(JsonElement root, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names))
        {
            return Guid.Empty;
        }

        if (element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out var value))
        {
            return value;
        }

        return Guid.Empty;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
    }

    private static bool ReadBoolean(JsonElement root, bool defaultValue, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static int ReadInt32(JsonElement root, int defaultValue, params string[] names)
    {
        if (!TryGetProperty(root, out var element, names))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static bool TryGetProperty(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }
}
