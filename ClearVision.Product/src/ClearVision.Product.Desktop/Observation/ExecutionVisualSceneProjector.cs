using System.Collections;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ResultPaths;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using OpenCvSharp;
using Operator = ClearVision.Product.Core.Entities.Operator;

namespace ClearVision.Product.Desktop.Observation;

public sealed class ExecutionVisualSceneInput
{
    public Operator? TargetOperator { get; init; }
    public IReadOnlyDictionary<string, object>? OutputData { get; init; }
    public IReadOnlyList<ExecutionObservationOutputPortV1> OutputPorts { get; init; } = [];
}

public static class ExecutionVisualSceneProjector
{
    public const int MaxPrimitives = 300;
    public const int MaxPoints = 512;
    public const int MaxDiagnostics = 64;
    public const int MaxStringChars = 256;
    public const int MaxPrimitiveIdChars = 160;

    private static readonly HashSet<string> KnownPrimitiveKinds = new(StringComparer.Ordinal)
    {
        "rectangle",
        "circle",
        "point",
        "polyline",
        "text"
    };

    public static ExecutionVisualSceneV1 Create(ExecutionVisualSceneInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var context = new SceneProjectionContext(input);
        try
        {
            if (input.TargetOperator == null)
            {
                context.AddDiagnostic("visual-scene-operator-missing", "Target operator is unavailable; scene is empty.", null);
                return context.Build();
            }

            switch (input.TargetOperator.Type)
            {
                case OperatorType.RoiManager:
                    ProjectRoi(input.TargetOperator, context);
                    break;
                case OperatorType.CircleMeasurement:
                    ProjectCircleMeasurement(context);
                    break;
                case OperatorType.NPointCalibration:
                    ProjectNPointCalibration(input.TargetOperator, context);
                    break;
                default:
                    context.AddDiagnostic("visual-scene-operator-unsupported", $"Operator type {input.TargetOperator.Type} has no visual-scene adapter.", null);
                    break;
            }
        }
        catch (Exception ex)
        {
            context.AddDiagnostic("visual-scene-projector-error", $"Scene projection failed: {Clip(ex.GetBaseException().Message)}", null);
        }

        return context.Build();
    }

    private static void ProjectRoi(Operator @operator, SceneProjectionContext context)
    {
        var shape = ReadParameterString(@operator, "Shape", "Rectangle");
        if (!shape.Equals("Rectangle", StringComparison.OrdinalIgnoreCase))
        {
            context.AddDiagnostic("visual-scene-roi-shape-unsupported", $"ROI shape {Clip(shape)} is not projected in G08.", null);
            return;
        }

        if (!TryReadParameterDouble(@operator, "X", out var x) ||
            !TryReadParameterDouble(@operator, "Y", out var y) ||
            !TryReadParameterDouble(@operator, "Width", out var width) ||
            !TryReadParameterDouble(@operator, "Height", out var height))
        {
            context.AddDiagnostic("visual-scene-roi-parameter-missing", "Rectangle ROI parameters X/Y/Width/Height are incomplete.", null);
            return;
        }

        var primitive = new ExecutionVisualScenePrimitiveV1
        {
            PrimitiveId = $"roi:rectangle:{@operator.Id:D}",
            Kind = "rectangle",
            Layer = "roi",
            ZOrder = 10,
            Visible = true,
            Selectable = true,
            Label = "ROI",
            Geometry = new ExecutionVisualSceneGeometryV1
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            },
            Style = new ExecutionVisualSceneStyleV1
            {
                Stroke = "#f59e0b",
                Fill = "rgba(245,158,11,0.14)",
                StrokeWidth = 2
            }
        };
        context.AddPrimitive(primitive);
    }

    private static void ProjectCircleMeasurement(SceneProjectionContext context)
    {
        var added = false;
        CircleCandidate? primary = null;
        if (context.TryGetOutput("Circle", out var circleValue) &&
            TryReadCircle(circleValue, "Circle", 0, out var circle))
        {
            primary = circle with { Source = "circle" };
            context.AddPrimitive(AttachRadiusResultPath(CreateCirclePrimitive("circle:primary", primary.Value, "Circle", 10), context));
            added = true;
        }

        if (primary == null &&
            context.TryGetOutput("Center", out var centerValue) &&
            context.TryGetOutput("Radius", out var radiusValue) &&
            TryReadPoint(centerValue, out var center) &&
            TryReadDouble(radiusValue, out var radius))
        {
            primary = new CircleCandidate(center.X, center.Y, radius, "center-radius", 0);
            context.AddPrimitive(AttachRadiusResultPath(CreateCirclePrimitive("circle:primary", primary.Value, "Circle", 10), context));
            added = true;
        }

        if (context.TryGetOutput("CircleDataList", out var listValue) &&
            TryReadCircleList(listValue, "circle-data-list", out var circlesFromList))
        {
            foreach (var candidate in circlesFromList)
            {
                if (primary.HasValue && IsSameCircle(primary.Value, candidate))
                {
                    continue;
                }

                context.AddPrimitive(CreateCirclePrimitive($"circle:data-list:{candidate.Ordinal.ToString(CultureInfo.InvariantCulture)}", candidate, $"Circle {candidate.Ordinal + 1}", 20 + candidate.Ordinal));
                added = true;
            }
        }
        else if (context.TryGetOutput("Circles", out var circlesValue) &&
                 TryReadCircleList(circlesValue, "circles", out var circlesFromDictionaries))
        {
            foreach (var candidate in circlesFromDictionaries)
            {
                if (primary.HasValue && IsSameCircle(primary.Value, candidate))
                {
                    continue;
                }

                context.AddPrimitive(CreateCirclePrimitive($"circle:circles:{candidate.Ordinal.ToString(CultureInfo.InvariantCulture)}", candidate, $"Circle {candidate.Ordinal + 1}", 20 + candidate.Ordinal));
                added = true;
            }
        }

        if (!added)
        {
            context.AddDiagnostic("visual-scene-circle-output-missing", "CircleMeasurement did not expose a supported Circle, Center/Radius, CircleDataList, or Circles payload.", null);
        }
    }

    private static void ProjectNPointCalibration(Operator @operator, SceneProjectionContext context)
    {
        var raw = ReadParameterValue(@operator, "PointPairs");
        if (!TryParsePointPairs(raw, out var pointPairs) || pointPairs.Count == 0)
        {
            context.AddDiagnostic("visual-scene-npoint-pointpairs-missing", "NPointCalibration PointPairs could not be parsed for scene display.", null);
            return;
        }

        var points = pointPairs
            .Select(pair => new ExecutionVisualScenePointV1 { X = pair.ImagePoint.X, Y = pair.ImagePoint.Y })
            .ToList();

        if (points.Count >= 2)
        {
            context.AddPrimitive(new ExecutionVisualScenePrimitiveV1
            {
                PrimitiveId = $"npoint:polyline:{@operator.Id:D}",
                Kind = "polyline",
                Layer = "calibration",
                ZOrder = 5,
                Visible = true,
                Selectable = false,
                Label = "Image sample path",
                Geometry = new ExecutionVisualSceneGeometryV1 { Points = points },
                Style = new ExecutionVisualSceneStyleV1
                {
                    Stroke = "#0ea5e9",
                    StrokeWidth = 1.5
                }
            });
        }

        for (var index = 0; index < pointPairs.Count; index++)
        {
            var ordinal = index + 1;
            var point = pointPairs[index].ImagePoint;
            context.AddPrimitive(new ExecutionVisualScenePrimitiveV1
            {
                PrimitiveId = $"npoint:point:{@operator.Id:D}:{ordinal.ToString(CultureInfo.InvariantCulture)}",
                Kind = "point",
                Layer = "calibration",
                ZOrder = 20 + index,
                Visible = true,
                Selectable = true,
                Label = ordinal.ToString(CultureInfo.InvariantCulture),
                Geometry = new ExecutionVisualSceneGeometryV1
                {
                    X = point.X,
                    Y = point.Y,
                    Radius = 4
                },
                Style = new ExecutionVisualSceneStyleV1
                {
                    Stroke = "#22c55e",
                    Fill = "rgba(34,197,94,0.75)",
                    StrokeWidth = 2
                }
            });
            context.AddPrimitive(new ExecutionVisualScenePrimitiveV1
            {
                PrimitiveId = $"npoint:label:{@operator.Id:D}:{ordinal.ToString(CultureInfo.InvariantCulture)}",
                Kind = "text",
                Layer = "calibration-label",
                ZOrder = 200 + index,
                Visible = true,
                Selectable = false,
                Label = ordinal.ToString(CultureInfo.InvariantCulture),
                Geometry = new ExecutionVisualSceneGeometryV1
                {
                    X = point.X + 6,
                    Y = point.Y - 6,
                    Text = ordinal.ToString(CultureInfo.InvariantCulture)
                },
                Style = new ExecutionVisualSceneStyleV1
                {
                    Stroke = "#eab308",
                    FontSize = 13
                }
            });
        }
    }

    private static ExecutionVisualScenePrimitiveV1 CreateCirclePrimitive(string primitiveId, CircleCandidate circle, string label, int zOrder) =>
        new()
        {
            PrimitiveId = primitiveId,
            Kind = "circle",
            Layer = "measurement",
            ZOrder = zOrder,
            Visible = true,
            Selectable = true,
            Label = label,
            Geometry = new ExecutionVisualSceneGeometryV1
            {
                CenterX = circle.CenterX,
                CenterY = circle.CenterY,
                Radius = circle.Radius
            },
            Style = new ExecutionVisualSceneStyleV1
            {
                Stroke = "#16a34a",
                Fill = "rgba(22,163,74,0.08)",
                StrokeWidth = 2
            }
        };

    private static ExecutionVisualScenePrimitiveV1 AttachRadiusResultPath(ExecutionVisualScenePrimitiveV1 primitive, SceneProjectionContext context)
    {
        if (!context.TryGetOutput("Radius", out var radiusValue) ||
            !context.TryResolveResultPath("Radius", "$", radiusValue, out var outputPortId, out var resultPath))
        {
            return primitive;
        }

        return ClonePrimitive(
            primitive,
            primitive.PrimitiveId,
            primitive.Geometry,
            primitive.Style,
            outputPortId,
            ResultPathV1.Version,
            resultPath);
    }

    private static bool TryReadCircleList(object? value, string source, out List<CircleCandidate> circles)
    {
        circles = new List<CircleCandidate>();
        switch (value)
        {
            case IReadOnlyList<CircleData> circleDataList:
                for (var index = 0; index < circleDataList.Count; index++)
                {
                    if (TryReadCircle(circleDataList[index], source, index, out var circle))
                    {
                        circles.Add(circle);
                    }
                }

                return circles.Count > 0;
            case IReadOnlyList<Dictionary<string, object>> dictionaries:
                for (var index = 0; index < dictionaries.Count; index++)
                {
                    if (TryReadCircle(dictionaries[index], source, index, out var circle))
                    {
                        circles.Add(circle);
                    }
                }

                return circles.Count > 0;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
            {
                var index = 0;
                foreach (var item in array.EnumerateArray())
                {
                    if (index >= MaxPrimitives)
                    {
                        break;
                    }

                    if (TryReadCircle(item, source, index, out var circle))
                    {
                        circles.Add(circle);
                    }

                    index++;
                }

                return circles.Count > 0;
            }
            default:
                return false;
        }
    }

    private static bool TryReadCircle(object? value, string source, int ordinal, out CircleCandidate circle)
    {
        circle = default;
        if (value is CircleData circleData)
        {
            circle = new CircleCandidate(circleData.CenterX, circleData.CenterY, circleData.Radius, source, ordinal);
            return true;
        }

        if (value is JsonElement element)
        {
            if (TryGetNumberProperty(element, "CenterX", out var centerX) &&
                TryGetNumberProperty(element, "CenterY", out var centerY) &&
                TryGetNumberProperty(element, "Radius", out var radius))
            {
                circle = new CircleCandidate(centerX, centerY, radius, source, ordinal);
                return true;
            }

            if (TryGetNestedPoint(element, "Center", out var center) &&
                TryGetNumberProperty(element, "Radius", out radius))
            {
                circle = new CircleCandidate(center.X, center.Y, radius, source, ordinal);
                return true;
            }

            return false;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            if (TryGetDouble(dictionary, "CenterX", out var centerX) &&
                TryGetDouble(dictionary, "CenterY", out var centerY) &&
                TryGetDouble(dictionary, "Radius", out var radius))
            {
                circle = new CircleCandidate(centerX, centerY, radius, source, ordinal);
                return true;
            }

            if (TryGetDictionaryValue(dictionary, "Center", out var centerObj) &&
                TryReadPoint(centerObj, out var center) &&
                TryGetDouble(dictionary, "Radius", out radius))
            {
                circle = new CircleCandidate(center.X, center.Y, radius, source, ordinal);
                return true;
            }

            return false;
        }

        if (value is IDictionary legacy)
        {
            return TryReadCircle(NormalizeDictionary(legacy), source, ordinal, out circle);
        }

        return false;
    }

    private static bool TryParsePointPairs(object? raw, out List<PointPairCandidate> pointPairs)
    {
        pointPairs = new List<PointPairCandidate>();
        if (raw == null)
        {
            return false;
        }

        if (raw is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (pointPairs.Count >= MaxPrimitives)
                    {
                        break;
                    }

                    if (TryParsePointPair(item, out var pair))
                    {
                        pointPairs.Add(pair);
                    }
                }

                return pointPairs.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        if (raw is JsonElement { ValueKind: JsonValueKind.Array } array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (pointPairs.Count >= MaxPrimitives)
                {
                    break;
                }

                if (TryParsePointPair(item, out var pair))
                {
                    pointPairs.Add(pair);
                }
            }

            return pointPairs.Count > 0;
        }

        if (raw is IReadOnlyList<Dictionary<string, object>> dictionaries)
        {
            for (var index = 0; index < Math.Min(dictionaries.Count, MaxPrimitives); index++)
            {
                if (TryParsePointPair(dictionaries[index], out var pair))
                {
                    pointPairs.Add(pair);
                }
            }

            return pointPairs.Count > 0;
        }

        return false;
    }

    private static bool TryParsePointPair(object? raw, out PointPairCandidate pair)
    {
        pair = default;
        if (raw == null)
        {
            return false;
        }

        if (raw is JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetNumberProperty(element, "ImageX", out var imageX) &&
                TryGetNumberProperty(element, "ImageY", out var imageY) &&
                TryGetNumberProperty(element, "WorldX", out var worldX) &&
                TryGetNumberProperty(element, "WorldY", out var worldY) &&
                AreFinite(imageX, imageY, worldX, worldY))
            {
                pair = new PointPairCandidate(new PointCandidate(imageX, imageY));
                return true;
            }

            if (TryGetNestedPoint(element, "ImagePoint", out var imagePoint) &&
                TryGetNestedPoint(element, "WorldPoint", out var worldPoint) &&
                AreFinite(imagePoint.X, imagePoint.Y, worldPoint.X, worldPoint.Y))
            {
                pair = new PointPairCandidate(imagePoint);
                return true;
            }

            if (TryGetNumberProperty(element, "PixelX", out imageX) &&
                TryGetNumberProperty(element, "PixelY", out imageY) &&
                TryGetNumberProperty(element, "PhysicalX", out worldX) &&
                TryGetNumberProperty(element, "PhysicalY", out worldY) &&
                AreFinite(imageX, imageY, worldX, worldY))
            {
                pair = new PointPairCandidate(new PointCandidate(imageX, imageY));
                return true;
            }

            return false;
        }

        if (raw is IDictionary<string, object> dictionary)
        {
            if (TryGetDouble(dictionary, "ImageX", out var imageX) &&
                TryGetDouble(dictionary, "ImageY", out var imageY) &&
                TryGetDouble(dictionary, "WorldX", out var worldX) &&
                TryGetDouble(dictionary, "WorldY", out var worldY) &&
                AreFinite(imageX, imageY, worldX, worldY))
            {
                pair = new PointPairCandidate(new PointCandidate(imageX, imageY));
                return true;
            }

            if (TryGetDictionaryValue(dictionary, "ImagePoint", out var imagePointObj) &&
                TryGetDictionaryValue(dictionary, "WorldPoint", out var worldPointObj) &&
                TryReadPoint(imagePointObj, out var imagePoint) &&
                TryReadPoint(worldPointObj, out var worldPoint) &&
                AreFinite(imagePoint.X, imagePoint.Y, worldPoint.X, worldPoint.Y))
            {
                pair = new PointPairCandidate(imagePoint);
                return true;
            }

            return false;
        }

        if (raw is IDictionary legacy)
        {
            return TryParsePointPair(NormalizeDictionary(legacy), out pair);
        }

        return false;
    }

    private static bool TryReadPoint(object? raw, out PointCandidate point)
    {
        point = default;
        switch (raw)
        {
            case Position position when AreFinite(position.X, position.Y):
                point = new PointCandidate(position.X, position.Y);
                return true;
            case JsonElement element:
                if (TryGetNumberProperty(element, "X", out var x) &&
                    TryGetNumberProperty(element, "Y", out var y) &&
                    AreFinite(x, y))
                {
                    point = new PointCandidate(x, y);
                    return true;
                }

                return false;
            case IDictionary<string, object> dictionary:
                if (TryGetDouble(dictionary, "X", out x) &&
                    TryGetDouble(dictionary, "Y", out y) &&
                    AreFinite(x, y))
                {
                    point = new PointCandidate(x, y);
                    return true;
                }

                return false;
            case IDictionary legacy:
                return TryReadPoint(NormalizeDictionary(legacy), out point);
            default:
                return false;
        }
    }

    private static object? ReadParameterValue(Operator @operator, string name)
    {
        var parameter = @operator.Parameters.FirstOrDefault(item =>
            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (parameter == null)
        {
            return null;
        }

        try
        {
            return parameter.GetValue();
        }
        catch
        {
            return null;
        }
    }

    private static string ReadParameterString(Operator @operator, string name, string fallback)
    {
        var raw = ReadParameterValue(@operator, name);
        return raw switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()?.Trim() ?? fallback,
            _ => fallback
        };
    }

    private static bool TryReadParameterDouble(Operator @operator, string name, out double value) =>
        TryReadDouble(ReadParameterValue(@operator, name), out value);

    private static bool TryReadDouble(object? raw, out double value)
    {
        switch (raw)
        {
            case double d when double.IsFinite(d):
                value = d;
                return true;
            case float f when float.IsFinite(f):
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case decimal m:
                value = (double)m;
                return double.IsFinite(value);
            case string text when double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed):
                value = parsed;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var jsonDouble) && double.IsFinite(jsonDouble):
                value = jsonDouble;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.String &&
                                          double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var jsonStringDouble) &&
                                          double.IsFinite(jsonStringDouble):
                value = jsonStringDouble;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryGetDouble(IDictionary<string, object> dictionary, string key, out double value)
    {
        value = 0;
        return TryGetDictionaryValue(dictionary, key, out var raw) && TryReadDouble(raw, out value);
    }

    private static bool TryGetDictionaryValue(IDictionary<string, object> dictionary, string key, out object? value)
    {
        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetNumberProperty(JsonElement obj, string propertyName, out double value)
    {
        value = 0;
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return TryReadDouble(property.Value, out value);
        }

        return false;
    }

    private static bool TryGetNestedPoint(JsonElement parent, string name, out PointCandidate point)
    {
        point = default;
        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return TryReadPoint(property.Value, out point);
            }
        }

        return false;
    }

    private static Dictionary<string, object> NormalizeDictionary(IDictionary dictionary)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key == null)
            {
                continue;
            }

            result[entry.Key.ToString() ?? string.Empty] = entry.Value ?? string.Empty;
        }

        return result;
    }

    private static bool IsSameCircle(CircleCandidate left, CircleCandidate right) =>
        Math.Abs(left.CenterX - right.CenterX) < 0.0001 &&
        Math.Abs(left.CenterY - right.CenterY) < 0.0001 &&
        Math.Abs(left.Radius - right.Radius) < 0.0001;

    private static bool AreFinite(params double[] values) => values.All(double.IsFinite);

    private static string Clip(string text) =>
        text.Length <= MaxStringChars ? text : text[..MaxStringChars] + "...";

    private static ExecutionVisualScenePrimitiveV1 ClonePrimitive(
        ExecutionVisualScenePrimitiveV1 source,
        string primitiveId,
        ExecutionVisualSceneGeometryV1 geometry,
        ExecutionVisualSceneStyleV1 style,
        Guid? outputPortId,
        int? resultPathVersion,
        string? resultPath) =>
        new()
        {
            PrimitiveId = primitiveId,
            Kind = source.Kind,
            Layer = source.Layer,
            ZOrder = source.ZOrder,
            Visible = source.Visible,
            Selectable = source.Selectable,
            Label = source.Label,
            Geometry = geometry,
            Style = style,
            OutputPortId = outputPortId,
            ResultPathVersion = resultPathVersion,
            ResultPath = resultPath
        };

    private sealed class SceneProjectionContext
    {
        private readonly ExecutionVisualSceneInput _input;
        private readonly Dictionary<string, List<ExecutionObservationOutputPortV1>> _portsByName;
        private readonly HashSet<string> _seenPrimitiveIds = new(StringComparer.Ordinal);
        private readonly List<ExecutionVisualScenePrimitiveV1> _primitives = new();
        private readonly List<ExecutionVisualSceneDiagnosticV1> _diagnostics = new();
        private bool _truncated;

        public SceneProjectionContext(ExecutionVisualSceneInput input)
        {
            _input = input;
            _portsByName = (input.OutputPorts ?? [])
                .Where(port => port.Id != Guid.Empty && !string.IsNullOrWhiteSpace(port.Name))
                .GroupBy(port => port.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        }

        public bool TryGetOutput(string name, out object? value)
        {
            value = null;
            if (_input.OutputData == null)
            {
                return false;
            }

            foreach (var pair in _input.OutputData)
            {
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        public bool TryResolveResultPath(string outputPortName, string resultPath, object? rootValue, out Guid outputPortId, out string canonicalPath)
        {
            outputPortId = Guid.Empty;
            canonicalPath = resultPath;
            if (!_portsByName.TryGetValue(outputPortName, out var ports) || ports.Count != 1)
            {
                return false;
            }

            var resolved = ResultPathResolver.Resolve(ResultPathV1.Version, resultPath, rootValue);
            if (!resolved.Succeeded || resolved.Path == null)
            {
                return false;
            }

            outputPortId = ports[0].Id;
            canonicalPath = resolved.Path.CanonicalPath;
            return true;
        }

        public void AddPrimitive(ExecutionVisualScenePrimitiveV1 primitive)
        {
            if (_primitives.Count >= MaxPrimitives)
            {
                _truncated = true;
                AddDiagnostic("visual-scene-primitive-limit", $"Primitive limit {MaxPrimitives.ToString(CultureInfo.InvariantCulture)} reached.", primitive.PrimitiveId);
                return;
            }

            var sanitized = SanitizePrimitive(primitive);
            if (sanitized == null)
            {
                return;
            }

            var primitiveId = sanitized.PrimitiveId;
            if (!_seenPrimitiveIds.Add(primitiveId))
            {
                var suffix = 2;
                string candidate;
                do
                {
                    candidate = $"{primitiveId}#duplicate-{suffix.ToString(CultureInfo.InvariantCulture)}";
                    suffix++;
                } while (!_seenPrimitiveIds.Add(candidate));

                AddDiagnostic("visual-scene-duplicate-primitive-id", "Duplicate primitiveId was deterministically renamed.", primitiveId);
                sanitized = ClonePrimitive(sanitized, candidate, sanitized.Geometry, sanitized.Style, sanitized.OutputPortId, sanitized.ResultPathVersion, sanitized.ResultPath);
            }

            _primitives.Add(sanitized);
        }

        public void AddDiagnostic(string code, string message, string? primitiveId)
        {
            if (_diagnostics.Count >= MaxDiagnostics)
            {
                _truncated = true;
                return;
            }

            _diagnostics.Add(new ExecutionVisualSceneDiagnosticV1
            {
                Code = code.Length <= 96 ? code : code[..96],
                Message = Clip(message),
                PrimitiveId = string.IsNullOrWhiteSpace(primitiveId) ? null : ClipPrimitiveId(primitiveId)
            });
        }

        public ExecutionVisualSceneV1 Build()
        {
            var (width, height) = ResolveImageSize();
            var ordered = _primitives
                .OrderBy(item => item.Layer, StringComparer.Ordinal)
                .ThenBy(item => item.ZOrder)
                .ThenBy(item => item.PrimitiveId, StringComparer.Ordinal)
                .ToList();

            return new ExecutionVisualSceneV1
            {
                ImageWidth = width,
                ImageHeight = height,
                Primitives = ordered,
                Diagnostics = _diagnostics,
                Truncated = _truncated
            };
        }

        private ExecutionVisualScenePrimitiveV1? SanitizePrimitive(ExecutionVisualScenePrimitiveV1 primitive)
        {
            if (string.IsNullOrWhiteSpace(primitive.PrimitiveId))
            {
                AddDiagnostic("visual-scene-primitive-invalid", "Primitive was skipped because primitiveId is empty.", null);
                return null;
            }

            var kind = primitive.Kind.Trim().ToLowerInvariant();
            if (!KnownPrimitiveKinds.Contains(kind))
            {
                AddDiagnostic("visual-scene-primitive-kind-unsupported", $"Primitive kind {Clip(kind)} is unsupported.", primitive.PrimitiveId);
                return null;
            }

            if (!TrySanitizeGeometry(kind, primitive.Geometry, primitive.PrimitiveId, out var geometry))
            {
                return null;
            }

            if (!TrySanitizeStyle(primitive.Style, primitive.PrimitiveId, out var style))
            {
                return null;
            }

            return ClonePrimitive(
                primitive,
                ClipPrimitiveId(primitive.PrimitiveId),
                geometry,
                style,
                primitive.OutputPortId,
                primitive.ResultPathVersion,
                primitive.ResultPath == null ? null : Clip(primitive.ResultPath));
        }

        private bool TrySanitizeGeometry(string kind, ExecutionVisualSceneGeometryV1 geometry, string primitiveId, out ExecutionVisualSceneGeometryV1 sanitized)
        {
            sanitized = new ExecutionVisualSceneGeometryV1();
            switch (kind)
            {
                case "rectangle":
                    if (!HasFinite(geometry.X, geometry.Y, geometry.Width, geometry.Height) || geometry.Width <= 0 || geometry.Height <= 0)
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Rectangle requires finite X/Y and positive Width/Height.", primitiveId);
                        return false;
                    }

                    sanitized = geometry;
                    return true;
                case "circle":
                    if (!HasFinite(geometry.CenterX, geometry.CenterY, geometry.Radius) || geometry.Radius <= 0)
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Circle requires finite CenterX/CenterY and positive Radius.", primitiveId);
                        return false;
                    }

                    sanitized = geometry;
                    return true;
                case "point":
                    if (!HasFinite(geometry.X, geometry.Y))
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Point requires finite X/Y.", primitiveId);
                        return false;
                    }

                    sanitized = geometry;
                    return true;
                case "polyline":
                    if (geometry.Points == null || geometry.Points.Count < 2)
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Polyline requires at least two points.", primitiveId);
                        return false;
                    }

                    var points = geometry.Points
                        .Take(MaxPoints)
                        .Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
                        .Select(point => new ExecutionVisualScenePointV1 { X = point.X, Y = point.Y })
                        .ToList();
                    if (points.Count < 2)
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Polyline finite point count is less than two.", primitiveId);
                        return false;
                    }

                    if (geometry.Points.Count > MaxPoints)
                    {
                        _truncated = true;
                        AddDiagnostic("visual-scene-point-limit", $"Point limit {MaxPoints.ToString(CultureInfo.InvariantCulture)} reached.", primitiveId);
                    }

                    sanitized = new ExecutionVisualSceneGeometryV1 { Points = points };
                    return true;
                case "text":
                    if (!HasFinite(geometry.X, geometry.Y))
                    {
                        AddDiagnostic("visual-scene-geometry-invalid", "Text requires finite X/Y.", primitiveId);
                        return false;
                    }

                    sanitized = new ExecutionVisualSceneGeometryV1
                    {
                        X = geometry.X,
                        Y = geometry.Y,
                        Text = geometry.Text == null ? null : Clip(geometry.Text)
                    };
                    return true;
                default:
                    return false;
            }
        }

        private bool TrySanitizeStyle(ExecutionVisualSceneStyleV1 style, string primitiveId, out ExecutionVisualSceneStyleV1 sanitized)
        {
            if ((style.StrokeWidth.HasValue && !double.IsFinite(style.StrokeWidth.Value)) ||
                (style.FontSize.HasValue && !double.IsFinite(style.FontSize.Value)))
            {
                AddDiagnostic("visual-scene-style-invalid", "Style numeric values must be finite.", primitiveId);
                sanitized = new ExecutionVisualSceneStyleV1();
                return false;
            }

            sanitized = new ExecutionVisualSceneStyleV1
            {
                Stroke = style.Stroke == null ? null : Clip(style.Stroke),
                Fill = style.Fill == null ? null : Clip(style.Fill),
                StrokeWidth = style.StrokeWidth,
                FontSize = style.FontSize
            };
            return true;
        }

        private (int Width, int Height) ResolveImageSize()
        {
            if (TryReadSizePair("Width", "Height", out var width, out var height) ||
                TryReadSizePair("ImageWidth", "ImageHeight", out width, out height))
            {
                return (width, height);
            }

            if (TryGetOutput("Image", out var image))
            {
                switch (image)
                {
                    case ImageWrapper { IsDecoded: true } wrapper:
                        return (Math.Max(0, wrapper.Width), Math.Max(0, wrapper.Height));
                    case Mat mat when !mat.Empty():
                        return (Math.Max(0, mat.Width), Math.Max(0, mat.Height));
                }
            }

            return (0, 0);
        }

        private bool TryReadSizePair(string widthKey, string heightKey, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!TryGetOutput(widthKey, out var widthValue) ||
                !TryGetOutput(heightKey, out var heightValue) ||
                !TryReadDouble(widthValue, out var rawWidth) ||
                !TryReadDouble(heightValue, out var rawHeight))
            {
                return false;
            }

            width = Math.Max(0, (int)Math.Round(rawWidth));
            height = Math.Max(0, (int)Math.Round(rawHeight));
            return true;
        }

        private static bool HasFinite(params double?[] values) =>
            values.All(value => value.HasValue && double.IsFinite(value.Value));

        private static string ClipPrimitiveId(string primitiveId) =>
            primitiveId.Length <= MaxPrimitiveIdChars ? primitiveId : primitiveId[..MaxPrimitiveIdChars];
    }

    private readonly record struct PointCandidate(double X, double Y);

    private readonly record struct CircleCandidate(double CenterX, double CenterY, double Radius, string Source, int Ordinal);

    private readonly record struct PointPairCandidate(PointCandidate ImagePoint);
}
