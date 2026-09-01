namespace ClearVision.Product.Application.Exports;

/// <summary>
/// Compatibility facade for the shared, dependency-free Core CSV formatter.
/// </summary>
public static class CsvSanitizer
{
    public static string ToCsvRow(params object?[] fields) =>
        Core.Exports.CsvSanitizer.ToCsvRow(fields);

    public static string FormatField(object? value) =>
        Core.Exports.CsvSanitizer.FormatField(value);

    public static string SanitizeText(string? value) =>
        Core.Exports.CsvSanitizer.SanitizeText(value);
}
