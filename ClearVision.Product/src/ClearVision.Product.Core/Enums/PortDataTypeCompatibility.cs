namespace ClearVision.Product.Core.Enums;

public static class PortDataTypeCompatibility
{
    public static bool AreCompatible(PortDataType source, PortDataType target)
    {
        return source == PortDataType.Any ||
               target == PortDataType.Any ||
               GetFamily(source) == GetFamily(target);
    }

    private static CompatibilityFamily GetFamily(PortDataType dataType)
    {
        return dataType switch
        {
            PortDataType.Image => CompatibilityFamily.Image,
            PortDataType.Integer or PortDataType.Float => CompatibilityFamily.Number,
            PortDataType.Boolean => CompatibilityFamily.Boolean,
            PortDataType.String => CompatibilityFamily.String,
            PortDataType.Point or PortDataType.Rectangle or PortDataType.PointList => CompatibilityFamily.Geometry,
            PortDataType.Contour => CompatibilityFamily.Contour,
            PortDataType.DetectionResult or PortDataType.DetectionList => CompatibilityFamily.Detection,
            PortDataType.CircleData => CompatibilityFamily.CircleData,
            PortDataType.LineData => CompatibilityFamily.LineData,
            PortDataType.Any => CompatibilityFamily.Any,
            _ => CompatibilityFamily.Other
        };
    }

    private enum CompatibilityFamily
    {
        Image,
        Number,
        Boolean,
        String,
        Geometry,
        Contour,
        Detection,
        CircleData,
        LineData,
        Any,
        Other
    }
}
