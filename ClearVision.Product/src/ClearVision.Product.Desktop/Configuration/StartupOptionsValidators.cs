using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Infrastructure.AI;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Configuration;

public sealed class StationIngressOptionsValidator : IValidateOptions<StationIngressOptions>
{
    public ValidateOptionsResult Validate(string? name, StationIngressOptions options)
    {
        var failures = new List<string>();

        if (options.Port is < 1 or > 65535)
        {
            failures.Add("StationIngress:Port must be between 1 and 65535.");
        }

        if (options.OfflineThresholdSeconds < 1)
        {
            failures.Add("StationIngress:OfflineThresholdSeconds must be at least 1.");
        }

        if (options.Enabled &&
            options.ListenMode == StationIngressListenMode.Lan &&
            string.IsNullOrWhiteSpace(options.SharedToken) &&
            !IsInsecureDevelopmentAllowed(options))
        {
            failures.Add("StationIngress:SharedToken is required for LAN mode.");
        }

        if (options.ResultBufferPerStation < 1 ||
            options.EventBufferSize < 1 ||
            options.HealthBufferPerStation < 1 ||
            options.LogBufferPerStation < 1 ||
            options.CommandBufferPerStation < 1)
        {
            failures.Add("StationIngress buffer sizes must all be positive.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsInsecureDevelopmentAllowed(StationIngressOptions options)
    {
#if DEBUG
        return options.AllowInsecureDevelopment;
#else
        return false;
#endif
    }
}

public sealed class AiGenerationOptionsValidator : IValidateOptions<AiGenerationOptions>
{
    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "anthropic",
        "azureopenai",
        "deepseek",
        "ollama",
        "openai",
        "openai-compatible",
        "qwen"
    };

    public ValidateOptionsResult Validate(string? name, AiGenerationOptions options)
    {
        var failures = new List<string>();
        var provider = (options.Provider ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(provider))
        {
            failures.Add("AiFlowGeneration:Provider is required.");
        }
        else if (!KnownProviders.Contains(provider))
        {
            failures.Add($"AiFlowGeneration:Provider '{provider}' is not recognized.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("AiFlowGeneration:Model is required.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            failures.Add("AiFlowGeneration:BaseUrl must be an absolute URL when configured.");
        }

        if (options.TimeoutSeconds < 1)
        {
            failures.Add("AiFlowGeneration:TimeoutSeconds must be at least 1.");
        }

        if (options.MaxRetries < 0)
        {
            failures.Add("AiFlowGeneration:MaxRetries must not be negative.");
        }

        if (options.MaxTokens < 1)
        {
            failures.Add("AiFlowGeneration:MaxTokens must be at least 1.");
        }

        if (options.Temperature is < 0 or > 2)
        {
            failures.Add("AiFlowGeneration:Temperature must be between 0 and 2.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
