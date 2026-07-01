using System.Text.RegularExpressions;

namespace ClearVision.Product.Infrastructure.AI;

public static class AiSecretSanitizer
{
    private static readonly Regex BearerRegex = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.Compiled);

    private static readonly Regex HeaderSecretRegex = new(
        @"(?i)\b(authorization|x-api-key|api-key|apikey|api_key|token|access_token|refresh_token)\b\s*[:=]\s*[""']?[^""'\s,;{}]+",
        RegexOptions.Compiled);

    private static readonly Regex JsonSecretRegex = new(
        @"(?i)(""?(apiKey|api_key|apikey|authorization|x-api-key|api-key|token|accessToken|refreshToken)""?\s*:\s*)""[^""]*""",
        RegexOptions.Compiled);

    private static readonly Regex QuerySecretRegex = new(
        @"(?i)([?&](api_key|apikey|apiKey|key|token|access_token|signature|sig)=)[^&#\s]+",
        RegexOptions.Compiled);

    private static readonly Regex TokenPathRegex = new(
        @"(?i)/(sk-[A-Za-z0-9_-]{8,}|key-[A-Za-z0-9_-]{8,}|token-[A-Za-z0-9_-]{8,}|[A-Za-z0-9_-]{24,})(?=/|$)",
        RegexOptions.Compiled);

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sanitized = value;
        sanitized = BearerRegex.Replace(sanitized, "Bearer <redacted>");
        sanitized = HeaderSecretRegex.Replace(sanitized, match =>
        {
            var separator = match.Value.Contains('=') ? "=" : ":";
            var name = match.Value.Split(separator[0], 2)[0].Trim();
            return $"{name}{separator}<redacted>";
        });
        sanitized = JsonSecretRegex.Replace(sanitized, "$1\"<redacted>\"");
        sanitized = QuerySecretRegex.Replace(sanitized, "$1<redacted>");
        sanitized = TokenPathRegex.Replace(sanitized, "/<redacted-token>");
        return sanitized;
    }

    public static string? RedactNullable(string? value)
    {
        return value == null ? null : Redact(value);
    }

    public static string MaskApiKey(bool hasKey)
    {
        return hasKey ? "********" : string.Empty;
    }

    public static string? RedactBaseUrlForReport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "<redacted-url>";

        var scheme = string.IsNullOrWhiteSpace(uri.Scheme) ? "https" : uri.Scheme;
        return $"{scheme}://<redacted-host>{(HasMeaningfulPath(uri) ? "/<redacted-path>" : "/")}";
    }

    public static string RedactException(Exception exception)
    {
        return Redact(exception.Message);
    }

    private static bool HasMeaningfulPath(Uri uri)
    {
        return !string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/";
    }
}
