using System.Globalization;

namespace ClearVision.Product.Application.Exports;

/// <summary>
/// Pure CSV cell formatting shared by application exports and file-writing operators.
/// Spreadsheet applications can interpret a formula after leading whitespace, tabs, or line breaks,
/// so the formula marker is checked after all leading Unicode whitespace rather than only at index zero.
/// </summary>
public static class CsvSanitizer
{
    public static string ToCsvRow(params object?[] fields) =>
        string.Join(",", fields.Select(FormatField));

    public static string FormatField(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        text = SanitizeText(text);
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    public static string SanitizeText(string? value)
    {
        var text = value ?? string.Empty;
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index < text.Length && text[index] is '=' or '+' or '-' or '@'
            ? "'" + text
            : text;
    }
}
