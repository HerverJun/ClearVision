// AiModelCapabilities.cs
// AI 模型能力定义
// 描述模型支持的功能范围与限制项
// 作者：蘅芜君
namespace ClearVision.Product.Infrastructure.AI;

/// <summary>
/// Declares runtime capabilities for a model profile.
/// Stage C uses this object as the decision source for routing and fallback.
/// </summary>
public class AiModelCapabilities
{
    public bool SupportsStreaming { get; set; } = true;
    public bool SupportsStreamOptions { get; set; } = true;
    public bool SupportsVisionInput { get; set; } = true;
    public bool SupportsReasoningStream { get; set; }
    public bool SupportsJsonMode { get; set; } = true;
    public bool SupportsToolCall { get; set; }
    public bool SupportsSystemPrompt { get; set; } = true;
    public int MaxImageCount { get; set; } = 4;
    public int MaxImageBytes { get; set; } = 20 * 1024 * 1024;

    public AiModelCapabilities Clone()
    {
        return new AiModelCapabilities
        {
            SupportsStreaming = SupportsStreaming,
            SupportsStreamOptions = SupportsStreamOptions,
            SupportsVisionInput = SupportsVisionInput,
            SupportsReasoningStream = SupportsReasoningStream,
            SupportsJsonMode = SupportsJsonMode,
            SupportsToolCall = SupportsToolCall,
            SupportsSystemPrompt = SupportsSystemPrompt,
            MaxImageCount = MaxImageCount,
            MaxImageBytes = MaxImageBytes
        };
    }

    public AiModelCapabilities Normalize()
    {
        if (MaxImageCount < 0)
            MaxImageCount = 0;

        if (MaxImageBytes <= 0)
            MaxImageBytes = 20 * 1024 * 1024;

        if (!SupportsVisionInput)
            MaxImageCount = 0;

        return this;
    }

    public AiModelCapabilities ApplyToolCallingMode(
        string mode,
        string? protocol,
        string? provider,
        string? model)
    {
        var normalizedMode = AiToolCallingModes.Normalize(mode);
        if (normalizedMode == AiToolCallingModes.Disabled ||
            normalizedMode == AiToolCallingModes.JsonFallback)
        {
            SupportsToolCall = false;
            return this;
        }

        if (normalizedMode == AiToolCallingModes.Native)
        {
            SupportsToolCall = true;
            return this;
        }

        var normalizedProtocol = AiModelConfig.NormalizeProtocol(protocol, provider);
        if (normalizedProtocol is AiModelConfig.ProtocolAnthropic or AiModelConfig.ProtocolAzureOpenAi)
        {
            SupportsToolCall = true;
            return this;
        }

        if (normalizedProtocol == AiModelConfig.ProtocolOpenAiCompatible)
        {
            var providerKey = (provider ?? string.Empty).ToLowerInvariant();
            var isNativeOpenAi = providerKey == "openai" ||
                providerKey.Contains("openai api") ||
                (providerKey.Contains("openai") && !providerKey.Contains("compatible"));
            SupportsToolCall = SupportsToolCall || isNativeOpenAi;
        }

        return this;
    }

    public static AiModelCapabilities Infer(string? provider, string? model)
    {
        var caps = new AiModelCapabilities();

        var providerKey = (provider ?? string.Empty).ToLowerInvariant();
        var modelKey = (model ?? string.Empty).ToLowerInvariant();

        if (providerKey.Contains("anthropic"))
        {
            // Anthropic response streaming is mature and commonly includes thinking deltas.
            caps.SupportsReasoningStream = true;
            caps.SupportsToolCall = true;
        }

        if (providerKey.Contains("azure") ||
            providerKey == "openai" ||
            providerKey.Contains("openai api") ||
            (providerKey.Contains("openai") && !providerKey.Contains("compatible")))
        {
            caps.SupportsToolCall = true;
        }

        if (modelKey.Contains("reasoner", StringComparison.OrdinalIgnoreCase))
        {
            // Current known pattern: reasoner class models are text-only for this pipeline.
            caps.SupportsVisionInput = false;
            caps.SupportsReasoningStream = true;
            caps.MaxImageCount = 0;
        }

        return caps.Normalize();
    }
}

public static class AiToolCallingModes
{
    public const string Auto = "auto";
    public const string Native = "native";
    public const string JsonFallback = "json_fallback";
    public const string Disabled = "disabled";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");
        return normalized switch
        {
            Native => Native,
            JsonFallback => JsonFallback,
            Disabled => Disabled,
            _ => Auto
        };
    }

    public static string ToDisplayLabel(string? value, bool nativeFallbackUsed = false)
    {
        if (nativeFallbackUsed)
        {
            return "JSON fallback";
        }

        return Normalize(value) switch
        {
            Native => "Native",
            Disabled => "Disabled",
            JsonFallback => "JSON fallback",
            _ => "Auto"
        };
    }
}
