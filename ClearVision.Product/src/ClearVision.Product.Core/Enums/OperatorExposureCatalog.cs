using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace ClearVision.Product.Core.Enums;

public enum OperatorExposure
{
    PackagePublic,
    PackageInternal,
    LegacyAlias,
    Disabled
}

public sealed record OperatorExposureEntry(
    OperatorType OperatorType,
    OperatorExposure Exposure,
    string Reason);

/// <summary>
/// The fail-closed source of truth for every <see cref="OperatorType"/> exposure decision.
/// Adding an enum member without adding exactly one row here makes catalog initialization fail.
/// </summary>
public static class OperatorExposureCatalog
{
    private static readonly OperatorExposureEntry[] DeclaredEntries =
    [
        Public(OperatorType.ImageAcquisition),
        Alias(OperatorType.Preprocessing),
        Public(OperatorType.Filtering),
        Public(OperatorType.EdgeDetection),
        Public(OperatorType.Thresholding),
        Public(OperatorType.Morphology),
        Public(OperatorType.BlobAnalysis),
        Public(OperatorType.TemplateMatching),
        Public(OperatorType.Measurement),
        Public(OperatorType.CodeRecognition),
        Public(OperatorType.DeepLearning),
        Public(OperatorType.ResultOutput),
        Public(OperatorType.ContourDetection),
        Public(OperatorType.MedianBlur),
        Public(OperatorType.BilateralFilter),
        Public(OperatorType.ImageResize),
        Public(OperatorType.ImageCrop),
        Public(OperatorType.ImageRotate),
        Public(OperatorType.PerspectiveTransform),
        Public(OperatorType.CircleMeasurement),
        Public(OperatorType.LineMeasurement),
        Public(OperatorType.ContourMeasurement),
        Public(OperatorType.AngleMeasurement),
        Public(OperatorType.GeometricTolerance),
        Public(OperatorType.CameraCalibration),
        Public(OperatorType.Undistort),
        Public(OperatorType.CoordinateTransform),
        Public(OperatorType.ModbusCommunication),
        Public(OperatorType.TcpCommunication),
        Public(OperatorType.DatabaseWrite),
        Public(OperatorType.ConditionalBranch),
        Public(OperatorType.ColorConversion),
        Public(OperatorType.AdaptiveThreshold),
        Public(OperatorType.HistogramEqualization),
        Public(OperatorType.GeometricFitting),
        Public(OperatorType.RoiManager),
        Public(OperatorType.RoiTransform),
        Public(OperatorType.ShapeMatching),
        Public(OperatorType.SubpixelEdgeDetection),
        Public(OperatorType.ColorDetection),
        Public(OperatorType.SerialCommunication),
        Public(OperatorType.SiemensS7Communication),
        Public(OperatorType.MitsubishiMcCommunication),
        Public(OperatorType.OmronFinsCommunication),
        Public(OperatorType.ResultJudgment),
        Public(OperatorType.DetectionSequenceJudge),
        Alias(OperatorType.ModbusRtuCommunication),
        Public(OperatorType.ClaheEnhancement),
        Public(OperatorType.MorphologicalOperation),
        Alias(OperatorType.GaussianBlur),
        Public(OperatorType.LaplacianSharpen),
        Alias(OperatorType.OnnxInference),
        Public(OperatorType.ImageAdd),
        Public(OperatorType.ImageSubtract),
        Public(OperatorType.ImageBlend),
        Public(OperatorType.VariableRead),
        Public(OperatorType.VariableWrite),
        Public(OperatorType.VariableIncrement),
        Public(OperatorType.TryCatch),
        Public(OperatorType.CycleCounter),
        Public(OperatorType.AkazeFeatureMatch),
        Public(OperatorType.OrbFeatureMatch),
        Public(OperatorType.GradientShapeMatch),
        Public(OperatorType.PyramidShapeMatch),
        Public(OperatorType.DualModalVoting),
        Public(OperatorType.OcrRecognition),
        Public(OperatorType.ImageDiff),
        Public(OperatorType.Statistics),
        Public(OperatorType.ForEach),
        Public(OperatorType.ArrayIndexer),
        Public(OperatorType.JsonExtractor),
        Public(OperatorType.MathOperation),
        Public(OperatorType.LogicGate),
        Public(OperatorType.TypeConvert),
        Public(OperatorType.HttpRequest),
        Disabled(OperatorType.MqttPublish, "Placeholder executor retained only for historical flow fail-closed execution (MQTT_PUBLISH_DISABLED)."),
        Public(OperatorType.StringFormat),
        Public(OperatorType.ImageSave),
        Public(OperatorType.Aggregator),
        Public(OperatorType.Comment),
        Public(OperatorType.Comparator),
        Public(OperatorType.Delay),
        Public(OperatorType.CaliperTool),
        Public(OperatorType.WidthMeasurement),
        Public(OperatorType.PointLineDistance),
        Public(OperatorType.LineLineDistance),
        Public(OperatorType.BoxNms),
        Public(OperatorType.BoxFilter),
        Public(OperatorType.SharpnessEvaluation),
        Public(OperatorType.PositionCorrection),
        Public(OperatorType.NPointCalibration),
        Public(OperatorType.CalibrationLoader),
        Public(OperatorType.UnitConvert),
        Public(OperatorType.TimerStatistics),
        Public(OperatorType.ScriptOperator),
        Public(OperatorType.TriggerModule),
        Internal(OperatorType.FrameChangeTrigger, "Runtime trigger stays product-internal and outside the package-public projection."),
        Public(OperatorType.PointAlignment),
        Public(OperatorType.PointCorrection),
        Public(OperatorType.GapMeasurement),
        Public(OperatorType.PolarUnwrap),
        Public(OperatorType.ShadingCorrection),
        Public(OperatorType.FrameAveraging),
        Public(OperatorType.AffineTransform),
        Public(OperatorType.ColorMeasurement),
        Public(OperatorType.SurfaceDefectDetection),
        Public(OperatorType.EdgePairDefect),
        Public(OperatorType.RectangleDetection),
        Public(OperatorType.TranslationRotationCalibration),
        Public(OperatorType.CornerDetection),
        Public(OperatorType.EdgeIntersection),
        Public(OperatorType.ParallelLineFind),
        Public(OperatorType.QuadrilateralFind),
        Public(OperatorType.GeoMeasurement),
        Public(OperatorType.ImageStitching),
        Public(OperatorType.ImageTiling),
        Public(OperatorType.ImageNormalize),
        Public(OperatorType.ImageCompose),
        Public(OperatorType.CopyMakeBorder),
        Public(OperatorType.TextSave),
        Public(OperatorType.PointSetTool),
        Public(OperatorType.BlobLabeling),
        Public(OperatorType.HistogramAnalysis),
        Public(OperatorType.PixelStatistics),
        Public(OperatorType.MeanFilter),
        Public(OperatorType.VoxelDownsample),
        Public(OperatorType.StatisticalOutlierRemoval),
        Public(OperatorType.RansacPlaneSegmentation),
        Public(OperatorType.EuclideanClusterExtraction),
        Public(OperatorType.PPFEstimation),
        Public(OperatorType.PPFMatch),
        Public(OperatorType.LawsTextureFilter),
        Public(OperatorType.GlcmTexture),
        Public(OperatorType.SemanticSegmentation),
        Public(OperatorType.AnomalyDetection),
        Public(OperatorType.HandEyeCalibration),
        Public(OperatorType.HandEyeCalibrationValidator),
        Public(OperatorType.FisheyeCalibration),
        Public(OperatorType.FisheyeUndistort),
        Public(OperatorType.StereoCalibration),
        Public(OperatorType.PixelToWorldTransform),
        Public(OperatorType.PlanarMatching),
        Public(OperatorType.LocalDeformableMatching),
        Public(OperatorType.DistanceTransform),
        Public(OperatorType.MinEnclosingGeometry),
        Public(OperatorType.RectangleRegion),
        Public(OperatorType.BinaryImageToRegion),
        Public(OperatorType.RegionErosion),
        Public(OperatorType.RegionDilation),
        Public(OperatorType.RegionOpening),
        Public(OperatorType.RegionClosing),
        Public(OperatorType.RegionSkeleton),
        Public(OperatorType.RegionUnion),
        Public(OperatorType.RegionIntersection),
        Public(OperatorType.RegionDifference),
        Public(OperatorType.RegionComplement),
        Public(OperatorType.ArcCaliper),
        Public(OperatorType.ContourExtrema),
        Public(OperatorType.FFT1D),
        Public(OperatorType.FrequencyFilter),
        Public(OperatorType.InverseFFT1D),
        Public(OperatorType.PhaseClosure)
    ];

    private static readonly IReadOnlyDictionary<OperatorType, OperatorExposureEntry> ByType;

    static OperatorExposureCatalog()
    {
        var enumValues = Enum.GetValues<OperatorType>();
        var enumSet = enumValues.ToHashSet();
        var duplicateTypes = DeclaredEntries
            .GroupBy(entry => entry.OperatorType)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .ToArray();
        var unknownTypes = DeclaredEntries
            .Where(entry => !enumSet.Contains(entry.OperatorType))
            .Select(entry => entry.OperatorType)
            .Distinct()
            .ToArray();
        var declaredTypes = DeclaredEntries.Select(entry => entry.OperatorType).ToHashSet();
        var missingTypes = enumValues.Where(type => !declaredTypes.Contains(type)).ToArray();

        if (duplicateTypes.Length > 0 || unknownTypes.Length > 0 || missingTypes.Length > 0)
        {
            throw new InvalidOperationException(
                "Operator exposure catalog is invalid. " +
                $"Duplicate=[{string.Join(",", duplicateTypes)}]; " +
                $"Unknown=[{string.Join(",", unknownTypes)}]; " +
                $"Missing=[{string.Join(",", missingTypes)}].");
        }

        ByType = new ReadOnlyDictionary<OperatorType, OperatorExposureEntry>(
            DeclaredEntries.ToDictionary(entry => entry.OperatorType));
        PopulationFingerprint = ComputeFingerprint(DeclaredEntries);
    }

    public static IReadOnlyList<OperatorExposureEntry> Entries { get; } =
        Array.AsReadOnly(DeclaredEntries.OrderBy(entry => (int)entry.OperatorType).ToArray());

    public static string PopulationFingerprint { get; }

    public static OperatorExposureEntry GetEntry(OperatorType type) =>
        ByType.TryGetValue(type, out var entry)
            ? entry
            : throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown operator exposure classification.");

    public static OperatorExposure GetExposure(OperatorType type) => GetEntry(type).Exposure;

    public static string GetSlug(OperatorType type) => GetExposure(type) switch
    {
        OperatorExposure.PackagePublic => "package-public",
        OperatorExposure.PackageInternal => "package-internal",
        OperatorExposure.LegacyAlias => "legacy-alias",
        OperatorExposure.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown operator exposure classification.")
    };

    public static bool IsPackagePublic(OperatorType type) => GetExposure(type) == OperatorExposure.PackagePublic;

    public static bool IsDisabled(OperatorType type) => GetExposure(type) == OperatorExposure.Disabled;

    public static bool IsLegacyAlias(OperatorType type) => GetExposure(type) == OperatorExposure.LegacyAlias;

    public static bool IsProductVisible(OperatorType type) =>
        GetExposure(type) is OperatorExposure.PackagePublic or OperatorExposure.PackageInternal;

    private static OperatorExposureEntry Public(OperatorType type) =>
        new(type, OperatorExposure.PackagePublic, "Supported package-public operator.");

    private static OperatorExposureEntry Internal(OperatorType type, string reason) =>
        new(type, OperatorExposure.PackageInternal, reason);

    private static OperatorExposureEntry Alias(OperatorType type) =>
        new(type, OperatorExposure.LegacyAlias, "Compatibility alias; resolve to its canonical operator type.");

    private static OperatorExposureEntry Disabled(OperatorType type, string reason) =>
        new(type, OperatorExposure.Disabled, reason);

    private static string ComputeFingerprint(IEnumerable<OperatorExposureEntry> entries)
    {
        var canonical = string.Join(
            "\n",
            entries
                .OrderBy(entry => (int)entry.OperatorType)
                .Select(entry => $"{(int)entry.OperatorType}:{entry.OperatorType}:{GetSlug(entry.OperatorType)}:{entry.Reason}"));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
