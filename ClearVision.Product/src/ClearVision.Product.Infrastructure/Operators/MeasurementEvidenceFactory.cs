using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.ValueObjects;

namespace ClearVision.Product.Infrastructure.Operators;

internal static class MeasurementEvidenceFactory
{
    public static MeasurementEvidence Create(
        Operator source,
        double value,
        string unit,
        string coordinateFrame,
        double? sigma,
        IReadOnlyList<double>? covariance,
        string provenance,
        string algorithm,
        IEnumerable<string>? qualityFlags = null)
    {
        return new MeasurementEvidence(
            value,
            unit,
            coordinateFrame,
            sigma is { } finiteSigma && double.IsFinite(finiteSigma) ? finiteSigma : null,
            covariance?.Where(double.IsFinite).ToArray(),
            provenance,
            source.Type.ToString(),
            algorithm,
            ComputeParameterFingerprint(source),
            qualityFlags?.Where(flag => !string.IsNullOrWhiteSpace(flag)).Distinct(StringComparer.Ordinal).OrderBy(flag => flag, StringComparer.Ordinal).ToArray()
                ?? Array.Empty<string>());
    }

    public static string ComputeParameterFingerprint(Operator source)
    {
        var payload = source.Parameters
            .OrderBy(parameter => parameter.Name, StringComparer.Ordinal)
            .Select(parameter => new
            {
                parameter.Name,
                parameter.DataType,
                Value = NormalizeValue(parameter.Value)
            })
            .ToArray();
        var json = JsonSerializer.Serialize(payload);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement element => element.Clone(),
            double number when double.IsFinite(number) => number,
            float number when float.IsFinite(number) => number,
            decimal number => number,
            int or long or short or byte or uint or ulong or ushort or sbyte or bool or string => value,
            _ => value.ToString()
        };
    }
}
