using System.Text.RegularExpressions;

namespace Acme.Product.Infrastructure.AI;

public sealed class AiPromptTrace
{
    public string Mode { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public AiModelCapabilities? Capabilities { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public object? AttachmentReport { get; set; }
    public string UsedReferenceFlowSummary { get; set; } = string.Empty;

    /// <summary>Active prompt template version ID at generation time.</summary>
    public string? PromptVersionId { get; set; }

    /// <summary>Active prompt template version name.</summary>
    public string? PromptVersionName { get; set; }

    /// <summary>Estimated input token count.</summary>
    public int? EstimatedInputTokens { get; set; }

    /// <summary>Estimated output token count.</summary>
    public int? EstimatedOutputTokens { get; set; }

    /// <summary>Model selection reason (role binding, fallback, active).</summary>
    public string? SelectionReason { get; set; }

    /// <summary>
    /// Returns a desensitized copy safe for client transmission.
    /// Masks API keys, local paths, private IP addresses, and customer filenames.
    /// </summary>
    public AiPromptTrace Desensitize()
    {
        return new AiPromptTrace
        {
            Mode = Mode,
            Provider = Provider,
            Model = Model,
            BaseUrl = MaskBaseUrl(BaseUrl),
            Capabilities = Capabilities?.Clone(),
            SystemPrompt = MaskSensitivePatterns(SystemPrompt),
            UserPrompt = MaskSensitivePatterns(UserPrompt),
            AttachmentReport = AttachmentReport,
            UsedReferenceFlowSummary = MaskSensitivePatterns(UsedReferenceFlowSummary),
            PromptVersionId = PromptVersionId,
            PromptVersionName = PromptVersionName,
            EstimatedInputTokens = EstimatedInputTokens,
            EstimatedOutputTokens = EstimatedOutputTokens,
            SelectionReason = SelectionReason
        };
    }

    private static string? MaskBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        return Regex.Replace(url, @"(api[_-]?key|apikey|key|token|secret)=[^&]+",
            "$1=***", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string MaskSensitivePatterns(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Windows local paths: C:\..., D:\...
        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s""'\]\)>]+", "<local-path>");
        // Unix home paths: /home/..., /Users/...
        text = Regex.Replace(text, @"/(?:home|Users)/[^\s""'\]\)>]+", "<local-path>");
        // API keys: sk-..., xai-..., ghp_...
        text = Regex.Replace(text,
            @"(?:sk-[a-zA-Z0-9]{20,}|xai-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{20,})",
            "***API_KEY***");
        // Private IP addresses: 10.x.x.x, 172.16-31.x.x, 192.168.x.x
        text = Regex.Replace(text,
            @"(?<!\d)(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})(?!\d)",
            "<internal-ip>");
        // .local domains
        text = Regex.Replace(text, @"[\w.-]+\.local(?::\d+)?", "<internal-host>");
        return text;
    }
}
