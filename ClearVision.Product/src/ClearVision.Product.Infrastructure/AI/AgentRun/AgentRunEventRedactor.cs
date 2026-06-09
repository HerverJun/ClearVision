using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClearVision.Product.Infrastructure.AI.AgentRun;

public sealed class AgentRunEventRedactor
{
    private static readonly Regex SensitiveKeyRegex = new(
        "(api[-_ ]?key|x-api-key|authorization|bearer|password|secret|token|credential|authheader)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrivatePlanningKeyRegex = new(
        "^(rawprompt|systemprompt|chainofthought|chain_of_thought|reasoningcontent|reasoning_content|hiddenreasoning)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PathKeyRegex = new(
        "(path|directory|folder|root|cvpkg|modelpath|templatepath|filepath|packagepath|packageroot|packagedirectory)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StationAddressKeyRegex = new(
        "(stationaddress|plcaddress|ipaddress|endpoint|host|port|url)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AuthorizationRegex = new(
        @"(?i)\b(authorization|x-api-key|api[-_ ]?key|token|secret|bearer)\b\s*[:=]\s*[""']?[^""'\s,;}]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BearerRegex = new(
        @"(?i)\bbearer\s+[a-z0-9._~+/=-]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SecretTokenRegex = new(
        @"(?i)\bsk-[a-z0-9_\-]{8,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrivatePlanningMarkerRegex = new(
        @"(?i)\b(rawPrompt|systemPrompt|chainOfThought|chain_of_thought|reasoningContent|reasoning_content|hiddenReasoning)\b\s*[:=]\s*[^,;}\r\n]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex IPv4Regex = new(
        @"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlcAddressRegex = new(
        @"(?i)\b(DB\d+\.DB[XBWD]\d+(?:\.\d+)?|M\d+(?:\.\d+)?|D\d+|plc://[^\s,;""'}]+)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WindowsPathRegex = new(
        @"(?i)(?:[a-z]:\\|\\\\)[^\s""'<>|]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UnixSensitivePathRegex = new(
        @"(?i)(?:/users/|/home/|/var/|/tmp/|/mnt/|/data/|/models/|/artifacts/)[^\s""'<>|]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ArtifactPathRegex = new(
        @"(?i)[a-z0-9_\-./\\:]+?\.(?:cvpkg|onnx|pt|pth|engine|weights|blob|zip|7z|tar|gz|png|jpg|jpeg|bmp|tif|tiff)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DataImageRegex = new(
        @"(?i)data:image/[a-z0-9.+-]+;base64,[a-z0-9+/=\r\n]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LongBase64Regex = new(
        @"(?<![a-z0-9+/=])(?:[a-z0-9+/]{96,}={0,2})(?![a-z0-9+/=])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ForbiddenFragments =
    [
        "authorization",
        "x-api-key",
        "api_key",
        "apikey",
        "bearer ",
        "data:image/",
        ".cvpkg",
        "plc://",
        "station://",
        "rawprompt",
        "systemprompt",
        "chainofthought",
        "chain_of_thought",
        "reasoningcontent",
        "reasoning_content",
        "hiddenreasoning"
    ];

    public object? RedactObject(object? value)
    {
        if (value == null)
        {
            return null;
        }

        var node = ToJsonNode(value);
        var redacted = RedactNode(node, propertyName: null);
        return redacted?.Deserialize<object>(AgentRunEventJson.Options);
    }

    public string RedactText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return RedactString(value, propertyName: null);
    }

    public bool IsRedactionSafe(object? value)
    {
        var json = JsonSerializer.Serialize(value, AgentRunEventJson.Options);
        return IsRedactionSafeText(json);
    }

    public bool IsRedactionSafeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (AuthorizationRegex.IsMatch(text) ||
            BearerRegex.IsMatch(text) ||
            SecretTokenRegex.IsMatch(text) ||
            IPv4Regex.IsMatch(text) ||
            PlcAddressRegex.IsMatch(text) ||
            WindowsPathRegex.IsMatch(text) ||
            UnixSensitivePathRegex.IsMatch(text) ||
            DataImageRegex.IsMatch(text) ||
            LongBase64Regex.IsMatch(text) ||
            ArtifactPathRegex.IsMatch(text))
        {
            return false;
        }

        return !ForbiddenFragments.Any(fragment =>
            text.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonNode? ToJsonNode(object value)
    {
        if (value is JsonElement element)
        {
            return JsonNode.Parse(element.GetRawText());
        }

        if (value is JsonNode node)
        {
            return node.DeepClone();
        }

        return JsonSerializer.SerializeToNode(value, AgentRunEventJson.Options);
    }

    private static JsonNode? RedactNode(JsonNode? node, string? propertyName)
    {
        if (node == null)
        {
            return null;
        }

        if (node is JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var property in obj)
            {
                var name = property.Key;
                if (SensitiveKeyRegex.IsMatch(name) || PrivatePlanningKeyRegex.IsMatch(name))
                {
                    copy[NextRedactedKey(copy, "redactedSecret")] = "[redacted:secret]";
                    continue;
                }

                if (PathKeyRegex.IsMatch(name))
                {
                    copy[name] = "[redacted:path]";
                    continue;
                }

                copy[name] = RedactNode(property.Value, name);
            }

            return copy;
        }

        if (node is JsonArray array)
        {
            var copy = new JsonArray();
            foreach (var item in array)
            {
                copy.Add(RedactNode(item, propertyName));
            }

            return copy;
        }

        var value = node.GetValue<object?>();
        if (value is string text)
        {
            return JsonValue.Create(RedactString(text, propertyName));
        }

        return node.DeepClone();
    }

    private static string NextRedactedKey(JsonObject obj, string baseName)
    {
        if (!obj.ContainsKey(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName}{index.ToString(CultureInfo.InvariantCulture)}";
            if (!obj.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private static string RedactString(string text, string? propertyName)
    {
        if (SensitiveKeyRegex.IsMatch(propertyName ?? string.Empty) ||
            PrivatePlanningKeyRegex.IsMatch(propertyName ?? string.Empty))
        {
            return "[redacted:secret]";
        }

        if (PathKeyRegex.IsMatch(propertyName ?? string.Empty))
        {
            return "[redacted:path]";
        }

        if (StationAddressKeyRegex.IsMatch(propertyName ?? string.Empty) &&
            (IPv4Regex.IsMatch(text) || text.Contains("://", StringComparison.Ordinal)))
        {
            return "[redacted:address]";
        }

        var result = text;
        result = PrivatePlanningMarkerRegex.Replace(result, "[redacted:private-planning]");
        result = AuthorizationRegex.Replace(result, "[redacted:secret]");
        result = BearerRegex.Replace(result, "Bearer [redacted:secret]");
        result = SecretTokenRegex.Replace(result, "[redacted:secret]");
        result = DataImageRegex.Replace(result, "[redacted:image-bytes]");
        result = LongBase64Regex.Replace(result, "[redacted:base64]");
        result = WindowsPathRegex.Replace(result, "[redacted:path]");
        result = UnixSensitivePathRegex.Replace(result, "[redacted:path]");
        result = ArtifactPathRegex.Replace(result, "[redacted:artifact-path]");
        result = IPv4Regex.Replace(result, RedactIPv4);
        result = PlcAddressRegex.Replace(result, "[redacted:plc-address]");
        return result;
    }

    private static string RedactIPv4(Match match)
    {
        var parts = match.Value.Split('.');
        if (parts.Length != 4)
        {
            return "[redacted:ip]";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{parts[0]}.{parts[1]}.x.x");
    }
}
