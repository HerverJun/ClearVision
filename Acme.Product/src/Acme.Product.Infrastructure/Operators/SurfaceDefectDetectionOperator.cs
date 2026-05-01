using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "表面缺陷检测",
    Description = "Detects surface defects using gradient, aligned reference diff, or local contrast.",
    Category = "AI检测",
    IconName = "surface-defect",
    Keywords = new[] { "surface defect", "scratch", "stain", "traditional detection" },
    Tags = new[] { "experimental", "industrial-remediation", "surface-defect" },
    Version = "2.0.0"
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[InputPort("Reference", "Reference", PortDataType.Image, IsRequired = false)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OutputPort("DefectMask", "Defect Mask", PortDataType.Image)]
[OutputPort("ResponseImage", "Response Image", PortDataType.Image)]
[OutputPort("DefectCount", "Defect Count", PortDataType.Integer)]
[OutputPort("DefectArea", "Defect Area", PortDataType.Float)]
[OutputPort("AlignmentScore", "Alignment Score", PortDataType.Float)]
[OutputPort("RejectedReason", "Rejected Reason", PortDataType.String)]
[OutputPort("Diagnostics", "Diagnostics", PortDataType.Any)]
[OperatorParam("Method", "Method", "enum", DefaultValue = "GradientMagnitude", Options = new[] { "GradientMagnitude|GradientMagnitude", "ReferenceDiff|ReferenceDiff", "LocalContrast|LocalContrast" })]
[OperatorParam("Threshold", "Threshold", "double", DefaultValue = 35.0, Min = 0.0, Max = 255.0)]
[OperatorParam("MinArea", "Min Area", "int", DefaultValue = 20, Min = 0, Max = 10000000)]
[OperatorParam("MaxArea", "Max Area", "int", DefaultValue = 1000000, Min = 0, Max = 10000000)]
[OperatorParam("MorphCleanSize", "Morph Clean Size", "int", DefaultValue = 3, Min = 1, Max = 301)]
[OperatorParam("MorphMode", "Morph Mode", "enum", DefaultValue = "OpenClose", Options = new[] { "None|None", "OpenClose|Open then close", "CloseOpen|Close then open", "CloseOnly|Close only" })]
[OperatorParam("AlignmentMode", "Alignment Mode", "enum", DefaultValue = "PhaseCorrelation", Options = new[] { "None|None", "PhaseCorrelation|PhaseCorrelation" })]
[OperatorParam("NormalizationMode", "Normalization Mode", "enum", DefaultValue = "LocalMean", Options = new[] { "None|None", "LocalMean|LocalMean", "ClaheLocalMean|CLAHE + LocalMean" })]
[OperatorParam("ThresholdMode", "Threshold Mode", "enum", DefaultValue = "Auto", Options = new[] { "Auto|Auto", "Manual|Manual", "Otsu|Otsu", "ReferenceStats|ReferenceStats" })]
[OperatorParam("BackgroundKernelSize", "Background Kernel Size", "int", DefaultValue = 31, Min = 3, Max = 301)]
[OperatorParam("ClaheClipLimit", "CLAHE Clip Limit", "double", DefaultValue = 2.0, Min = 0.1, Max = 40.0)]
[OperatorParam("ClaheTileGridSize", "CLAHE Tile Grid Size", "int", DefaultValue = 8, Min = 2, Max = 64)]
[OperatorParam("ReferenceStatsSigma", "Reference Stats Sigma", "double", DefaultValue = 2.5, Min = 0.1, Max = 10.0)]
[OperatorParam("RobustReferenceStats", "Robust Reference Stats", "bool", DefaultValue = false)]
[OperatorParam("ResponseNormalizeMode", "Response Normalize Mode", "enum", DefaultValue = "RawClamp", Options = new[] { "RawClamp|Raw clamp", "MinMax|Min/max", "PercentileClip|Percentile clip" })]
[OperatorParam("ComponentFilterMode", "Component Filter Mode", "enum", DefaultValue = "AreaOnly", Options = new[] { "AreaOnly|Area only", "ResponseStats|Response statistics", "ShapeAndResponseStats|Shape and response statistics" })]
[OperatorParam("SmallNoiseAreaMax", "Small Noise Area Max", "int", DefaultValue = 0, Min = 0, Max = 10000000)]
[OperatorParam("MinElongationForSmallComponent", "Min Elongation For Small Component", "double", DefaultValue = 0.0, Min = 0.0, Max = 50.0)]
[OperatorParam("CompactNoiseAreaMax", "Compact Noise Area Max", "int", DefaultValue = 0, Min = 0, Max = 10000000)]
[OperatorParam("CompactNoiseCircularityMin", "Compact Noise Circularity Min", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("CompactNoiseFillRatioMin", "Compact Noise Fill Ratio Min", "double", DefaultValue = 0.0, Min = 0.0, Max = 1.0)]
[OperatorParam("MinLocalResponseProminence", "Min Local Response Prominence", "double", DefaultValue = 0.0, Min = 0.0, Max = 255.0)]
[OperatorParam("EnableCandidateProfile", "Enable Candidate Profile", "bool", DefaultValue = false)]
[OperatorParam("CandidateProfile", "Candidate Profile", "enum", DefaultValue = "default", Options = new[] { "default|Default", "taxonomy_v2|Surface taxonomy v2" })]
public class SurfaceDefectDetectionOperator : OperatorBase
{
    private const double MinAcceptedPhaseCorrelationResponse = 0.02;
    private const double MaxAcceptedShiftRatio = 0.45;
    private const double MinAcceptedImprovementRatio = -0.04;
    private const string CandidateProfileDefault = "default";
    private const string CandidateProfileTaxonomyV2 = "taxonomy_v2";
    private const double TaxonomyV2ManualThresholdFloor = 15.0;

    public override OperatorType OperatorType => OperatorType.SurfaceDefectDetection;

    public SurfaceDefectDetectionOperator(ILogger<SurfaceDefectDetectionOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var method = GetStringParam(@operator, "Method", "GradientMagnitude");
        var manualThreshold = GetDoubleParam(@operator, "Threshold", 35.0, 0.0, 255.0);
        var minArea = GetIntParam(@operator, "MinArea", 20, 0, 10_000_000);
        var maxArea = GetIntParam(@operator, "MaxArea", 1_000_000, 0, 10_000_000);
        var cleanSize = GetIntParam(@operator, "MorphCleanSize", 3, 1, 301);
        var morphMode = GetStringParam(@operator, "MorphMode", "OpenClose");
        var alignmentMode = GetStringParam(@operator, "AlignmentMode", "PhaseCorrelation");
        var normalizationMode = GetStringParam(@operator, "NormalizationMode", "LocalMean");
        var thresholdMode = GetStringParam(@operator, "ThresholdMode", "Auto");
        var backgroundKernelSize = GetIntParam(@operator, "BackgroundKernelSize", 31, 3, 301);
        var claheClipLimit = GetDoubleParam(@operator, "ClaheClipLimit", 2.0, 0.1, 40.0);
        var claheTileGridSize = GetIntParam(@operator, "ClaheTileGridSize", 8, 2, 64);
        var referenceStatsSigma = GetDoubleParam(@operator, "ReferenceStatsSigma", 2.5, 0.1, 10.0);
        var robustReferenceStats = GetBoolParam(@operator, "RobustReferenceStats", false);
        var responseNormalizeMode = GetStringParam(@operator, "ResponseNormalizeMode", "RawClamp");
        var componentFilterMode = GetStringParam(@operator, "ComponentFilterMode", "AreaOnly");
        var smallNoiseAreaMax = GetIntParam(@operator, "SmallNoiseAreaMax", 0, 0, 10_000_000);
        var minElongationForSmallComponent = GetDoubleParam(@operator, "MinElongationForSmallComponent", 0.0, 0.0, 50.0);
        var compactNoiseAreaMax = GetIntParam(@operator, "CompactNoiseAreaMax", 0, 0, 10_000_000);
        var compactNoiseCircularityMin = GetDoubleParam(@operator, "CompactNoiseCircularityMin", 0.0, 0.0, 1.0);
        var compactNoiseFillRatioMin = GetDoubleParam(@operator, "CompactNoiseFillRatioMin", 0.0, 0.0, 1.0);
        var minLocalResponseProminence = GetDoubleParam(@operator, "MinLocalResponseProminence", 0.0, 0.0, 255.0);
        var candidateProfile = ResolveCandidateProfile(@operator);
        if (!IsSupportedCandidateProfile(candidateProfile.Profile))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("CandidateProfile must be 'default' or 'taxonomy_v2'."));
        }

        if (candidateProfile.Enabled && candidateProfile.Profile == CandidateProfileTaxonomyV2)
        {
            method = "LocalContrast";
            thresholdMode = "Manual";
            manualThreshold = Math.Max(manualThreshold, TaxonomyV2ManualThresholdFloor);
            minArea = Math.Max(minArea, 6);
            cleanSize = Math.Max(cleanSize, 3);
            morphMode = "CloseOpen";
            normalizationMode = "ClaheLocalMean";
            backgroundKernelSize = 21;
            claheClipLimit = 1.5;
            claheTileGridSize = 8;
            responseNormalizeMode = "RawClamp";
            componentFilterMode = "ShapeAndResponseStats";
            smallNoiseAreaMax = Math.Max(smallNoiseAreaMax, 32);
            minElongationForSmallComponent = Math.Max(minElongationForSmallComponent, 2.5);
            compactNoiseAreaMax = Math.Max(compactNoiseAreaMax, 64);
            compactNoiseCircularityMin = Math.Max(compactNoiseCircularityMin, 0.68);
            compactNoiseFillRatioMin = Math.Max(compactNoiseFillRatioMin, 0.45);
            minLocalResponseProminence = Math.Max(minLocalResponseProminence, 4.0);
            candidateProfile = candidateProfile with { Applied = true };
        }

        using var gray = OperatorImageDepthHelper.EnsureSingleChannelGray(src);

        using var response = BuildResponseMap(
            method,
            gray,
            inputs,
            alignmentMode,
            normalizationMode,
            responseNormalizeMode,
            backgroundKernelSize,
            claheClipLimit,
            claheTileGridSize,
            out var alignmentScore,
            out var alignmentShift,
            out var rejectedReason);

        if (method.Equals("ReferenceDiff", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(rejectedReason))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(rejectedReason));
        }

        if (response.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Failed to compute defect response map"));
        }

        using var binary = new Mat();
        var appliedThreshold = ApplyThreshold(response, binary, method, thresholdMode, manualThreshold, referenceStatsSigma, robustReferenceStats);
        var candidateAreaBeforeMorph = Cv2.CountNonZero(binary);

        var oddKernel = cleanSize % 2 == 0 ? cleanSize + 1 : cleanSize;
        if (oddKernel > 1 && !morphMode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(oddKernel, oddKernel));
            ApplyMorphology(binary, kernel, morphMode);
        }

        var candidateAreaAfterMorph = Cv2.CountNonZero(binary);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var resultImage = OperatorImageDepthHelper.EnsureBgrColor(src);
        var defectMask = new Mat(binary.Size(), MatType.CV_8UC1, Scalar.Black);
        var responseImage = response.Clone();

        var defectCount = 0;
        var defectArea = 0.0;
        var componentResponseRejectedCount = 0;
        var componentShapeRejectedCount = 0;
        var componentCompactNoiseRejectedCount = 0;
        var componentLocalProminenceRejectedCount = 0;
        Cv2.MeanStdDev(response, out var responseMean, out var responseStdDev);
        var responseStatsFilter =
            componentFilterMode.Equals("ResponseStats", StringComparison.OrdinalIgnoreCase) ||
            componentFilterMode.Equals("ShapeAndResponseStats", StringComparison.OrdinalIgnoreCase);
        var shapeStatsFilter = componentFilterMode.Equals("ShapeAndResponseStats", StringComparison.OrdinalIgnoreCase);
        var componentMeanGate = responseStatsFilter
            ? Math.Max(appliedThreshold * 0.55, responseMean.Val0 + responseStdDev.Val0 * 0.10)
            : 0.0;
        var componentPeakGate = responseStatsFilter
            ? Math.Max(appliedThreshold, responseMean.Val0 + responseStdDev.Val0 * 0.35)
            : 0.0;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < minArea || area > maxArea)
            {
                continue;
            }

            if (responseStatsFilter &&
                !AcceptComponentByResponseStats(response, contour, componentMeanGate, componentPeakGate, out _, out _))
            {
                componentResponseRejectedCount++;
                continue;
            }

            if (shapeStatsFilter &&
                !AcceptComponentByShapeStats(
                    contour,
                    area,
                    smallNoiseAreaMax,
                    minElongationForSmallComponent,
                    compactNoiseAreaMax,
                    compactNoiseCircularityMin,
                    compactNoiseFillRatioMin,
                    out _,
                    out _,
                    out _,
                    out var shapeRejectReason))
            {
                if (shapeRejectReason == "CompactTextureNoise")
                {
                    componentCompactNoiseRejectedCount++;
                }

                componentShapeRejectedCount++;
                continue;
            }

            if (shapeStatsFilter &&
                !AcceptComponentByLocalResponseProminence(
                    response,
                    contour,
                    area,
                    compactNoiseAreaMax,
                    minLocalResponseProminence,
                    out _,
                    out _,
                    out _))
            {
                componentLocalProminenceRejectedCount++;
                continue;
            }

            defectCount++;
            defectArea += area;

            Cv2.DrawContours(defectMask, new[] { contour }, -1, Scalar.White, -1);
            var rect = Cv2.BoundingRect(contour);
            Cv2.Rectangle(resultImage, rect, new Scalar(0, 0, 255), 2);
        }

        Cv2.PutText(resultImage, $"Defects:{defectCount} Area:{defectArea:F1}", new Point(8, 24), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 255), 2);
        Cv2.PutText(resultImage, $"Thr:{appliedThreshold:F1} Align:{alignmentScore:F2}", new Point(8, 48), HersheyFonts.HersheySimplex, 0.5, new Scalar(255, 255, 0), 1);

        var diagnostics = new Dictionary<string, object>
        {
            { "Method", method },
            { "AlignmentMode", alignmentMode },
            { "NormalizationMode", normalizationMode },
            { "ResponseNormalizeMode", responseNormalizeMode },
            { "ComponentFilterMode", componentFilterMode },
            { "ThresholdMode", ResolveThresholdMode(method, thresholdMode) },
            { "AppliedThreshold", appliedThreshold },
            { "ClaheClipLimit", claheClipLimit },
            { "ClaheTileGridSize", claheTileGridSize },
            { "RobustReferenceStats", robustReferenceStats },
            { "MorphMode", morphMode },
            { "CandidateAreaBeforeMorph", candidateAreaBeforeMorph },
            { "CandidateAreaAfterMorph", candidateAreaAfterMorph },
            { "AlignmentScore", alignmentScore },
            { "AlignmentShiftX", alignmentShift.X },
            { "AlignmentShiftY", alignmentShift.Y },
            { "CandidateCount", contours.Length },
            { "AcceptedCount", defectCount },
            { "ComponentRejectedCount", componentResponseRejectedCount + componentShapeRejectedCount + componentLocalProminenceRejectedCount },
            { "ComponentResponseRejectedCount", componentResponseRejectedCount },
            { "ComponentShapeRejectedCount", componentShapeRejectedCount },
            { "ComponentCompactNoiseRejectedCount", componentCompactNoiseRejectedCount },
            { "ComponentLocalProminenceRejectedCount", componentLocalProminenceRejectedCount },
            { "ComponentMeanGate", componentMeanGate },
            { "ComponentPeakGate", componentPeakGate },
            { "SmallNoiseAreaMax", smallNoiseAreaMax },
            { "MinElongationForSmallComponent", minElongationForSmallComponent },
            { "CompactNoiseAreaMax", compactNoiseAreaMax },
            { "CompactNoiseCircularityMin", compactNoiseCircularityMin },
            { "CompactNoiseFillRatioMin", compactNoiseFillRatioMin },
            { "MinLocalResponseProminence", minLocalResponseProminence },
            { "CandidateProfileEnabled", candidateProfile.Enabled },
            { "CandidateProfile", candidateProfile.Profile },
            { "CandidateProfileApplied", candidateProfile.Applied },
            { "RejectedReason", rejectedReason },
            { "ResponseMean", responseMean.Val0 },
            { "ResponseStdDev", responseStdDev.Val0 }
        };

        var additional = new Dictionary<string, object>
        {
            { "DefectMask", new ImageWrapper(defectMask) },
            { "ResponseImage", new ImageWrapper(responseImage) },
            { "DefectCount", defectCount },
            { "DefectArea", defectArea },
            { "AlignmentScore", alignmentScore },
            { "RejectedReason", rejectedReason },
            { "Diagnostics", diagnostics }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, additional)));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var method = GetStringParam(@operator, "Method", "GradientMagnitude");
        var validMethods = new[] { "GradientMagnitude", "ReferenceDiff", "LocalContrast" };
        if (!validMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("Method must be GradientMagnitude, ReferenceDiff or LocalContrast");
        }

        var minArea = GetIntParam(@operator, "MinArea", 20);
        var maxArea = GetIntParam(@operator, "MaxArea", 1_000_000);
        if (minArea < 0 || maxArea <= 0 || minArea > maxArea)
        {
            return ValidationResult.Invalid("Invalid MinArea/MaxArea range");
        }

        var alignmentMode = GetStringParam(@operator, "AlignmentMode", "PhaseCorrelation");
        var validAlignmentModes = new[] { "None", "PhaseCorrelation" };
        if (!validAlignmentModes.Contains(alignmentMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("AlignmentMode must be None or PhaseCorrelation");
        }

        var normalizationMode = GetStringParam(@operator, "NormalizationMode", "LocalMean");
        var validNormalizationModes = new[] { "None", "LocalMean", "ClaheLocalMean" };
        if (!validNormalizationModes.Contains(normalizationMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("NormalizationMode must be None, LocalMean or ClaheLocalMean");
        }

        var thresholdMode = GetStringParam(@operator, "ThresholdMode", "Auto");
        var validThresholdModes = new[] { "Auto", "Manual", "Otsu", "ReferenceStats" };
        if (!validThresholdModes.Contains(thresholdMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("ThresholdMode must be Auto, Manual, Otsu or ReferenceStats");
        }

        var morphMode = GetStringParam(@operator, "MorphMode", "OpenClose");
        var validMorphModes = new[] { "None", "OpenClose", "CloseOpen", "CloseOnly" };
        if (!validMorphModes.Contains(morphMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("MorphMode must be None, OpenClose, CloseOpen or CloseOnly");
        }

        var responseNormalizeMode = GetStringParam(@operator, "ResponseNormalizeMode", "RawClamp");
        var validResponseNormalizeModes = new[] { "RawClamp", "MinMax", "PercentileClip" };
        if (!validResponseNormalizeModes.Contains(responseNormalizeMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("ResponseNormalizeMode must be RawClamp, MinMax or PercentileClip");
        }

        var componentFilterMode = GetStringParam(@operator, "ComponentFilterMode", "AreaOnly");
        var validComponentFilterModes = new[] { "AreaOnly", "ResponseStats", "ShapeAndResponseStats" };
        if (!validComponentFilterModes.Contains(componentFilterMode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("ComponentFilterMode must be AreaOnly, ResponseStats or ShapeAndResponseStats");
        }

        var smallNoiseAreaMax = GetIntParam(@operator, "SmallNoiseAreaMax", 0);
        if (smallNoiseAreaMax < 0)
        {
            return ValidationResult.Invalid("SmallNoiseAreaMax must be non-negative.");
        }

        var minElongationForSmallComponent = GetDoubleParam(@operator, "MinElongationForSmallComponent", 0.0);
        if (minElongationForSmallComponent < 0.0 || minElongationForSmallComponent > 50.0)
        {
            return ValidationResult.Invalid("MinElongationForSmallComponent must be between 0 and 50.");
        }

        var compactNoiseAreaMax = GetIntParam(@operator, "CompactNoiseAreaMax", 0);
        if (compactNoiseAreaMax < 0)
        {
            return ValidationResult.Invalid("CompactNoiseAreaMax must be non-negative.");
        }

        var compactNoiseCircularityMin = GetDoubleParam(@operator, "CompactNoiseCircularityMin", 0.0);
        if (compactNoiseCircularityMin < 0.0 || compactNoiseCircularityMin > 1.0)
        {
            return ValidationResult.Invalid("CompactNoiseCircularityMin must be between 0 and 1.");
        }

        var compactNoiseFillRatioMin = GetDoubleParam(@operator, "CompactNoiseFillRatioMin", 0.0);
        if (compactNoiseFillRatioMin < 0.0 || compactNoiseFillRatioMin > 1.0)
        {
            return ValidationResult.Invalid("CompactNoiseFillRatioMin must be between 0 and 1.");
        }

        var minLocalResponseProminence = GetDoubleParam(@operator, "MinLocalResponseProminence", 0.0);
        if (minLocalResponseProminence < 0.0 || minLocalResponseProminence > 255.0)
        {
            return ValidationResult.Invalid("MinLocalResponseProminence must be between 0 and 255.");
        }

        var candidateProfile = ResolveCandidateProfile(@operator);
        if (!IsSupportedCandidateProfile(candidateProfile.Profile))
        {
            return ValidationResult.Invalid("CandidateProfile must be 'default' or 'taxonomy_v2'.");
        }

        var claheClipLimit = GetDoubleParam(@operator, "ClaheClipLimit", 2.0);
        if (claheClipLimit <= 0 || claheClipLimit > 40)
        {
            return ValidationResult.Invalid("ClaheClipLimit must be in (0, 40].");
        }

        var claheTileGridSize = GetIntParam(@operator, "ClaheTileGridSize", 8);
        if (claheTileGridSize < 2 || claheTileGridSize > 64)
        {
            return ValidationResult.Invalid("ClaheTileGridSize must be between 2 and 64.");
        }

        return ValidationResult.Valid();
    }

    private Mat BuildResponseMap(
        string method,
        Mat gray,
        Dictionary<string, object>? inputs,
        string alignmentMode,
        string normalizationMode,
        string responseNormalizeMode,
        int backgroundKernelSize,
        double claheClipLimit,
        int claheTileGridSize,
        out double alignmentScore,
        out Point2d alignmentShift,
        out string rejectedReason)
    {
        alignmentScore = 0;
        alignmentShift = new Point2d(0, 0);
        rejectedReason = string.Empty;

        switch (method.ToLowerInvariant())
        {
            case "gradientmagnitude":
            {
                using var normalized = NormalizeForComparison(gray, normalizationMode, backgroundKernelSize, claheClipLimit, claheTileGridSize);
                using var gradX = new Mat();
                using var gradY = new Mat();
                using var magnitude = new Mat();
                Cv2.Sobel(normalized, gradX, MatType.CV_32FC1, 1, 0, 3);
                Cv2.Sobel(normalized, gradY, MatType.CV_32FC1, 0, 1, 3);
                Cv2.Magnitude(gradX, gradY, magnitude);
                return NormalizeResponseToByte(magnitude, responseNormalizeMode);
            }

            case "referencediff":
            {
                if (!TryGetInputImage(inputs, "Reference", out var referenceWrapper) || referenceWrapper == null)
                {
                    throw new InvalidOperationException("Reference image is required in ReferenceDiff mode");
                }

                var reference = referenceWrapper.GetMat();
                if (reference.Empty())
                {
                    throw new InvalidOperationException("Reference image is invalid");
                }

                using var referenceGray = OperatorImageDepthHelper.EnsureSingleChannelGray(reference);

                using var resized = EnsureSize(referenceGray, gray.Size());
                using var aligned = AlignReferenceToSource(gray, resized, alignmentMode, out alignmentScore, out alignmentShift, out rejectedReason);
                using var normalizedSource = NormalizeForComparison(gray, normalizationMode, backgroundKernelSize, claheClipLimit, claheTileGridSize);
                using var normalizedReference = NormalizeForComparison(aligned, normalizationMode, backgroundKernelSize, claheClipLimit, claheTileGridSize);

                var result = new Mat();
                Cv2.Absdiff(normalizedSource, normalizedReference, result);
                return result;
            }

            case "localcontrast":
            {
                var localNormalizationMode = normalizationMode.Equals("None", StringComparison.OrdinalIgnoreCase)
                    ? "LocalMean"
                    : normalizationMode;
                return NormalizeForComparison(gray, localNormalizationMode, backgroundKernelSize, claheClipLimit, claheTileGridSize);
            }

            default:
                throw new InvalidOperationException("Unsupported defect detection method");
        }
    }

    private static Mat EnsureSize(Mat source, Size size)
    {
        if (source.Size() == size)
        {
            return source.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(source, resized, size);
        return resized;
    }

    private static void ApplyMorphology(Mat binary, Mat kernel, string morphMode)
    {
        if (morphMode.Equals("CloseOnly", StringComparison.OrdinalIgnoreCase))
        {
            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
            return;
        }

        if (morphMode.Equals("CloseOpen", StringComparison.OrdinalIgnoreCase))
        {
            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
            Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);
            return;
        }

        Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);
    }

    private static Mat NormalizeResponseToByte(Mat response32, string responseNormalizeMode)
    {
        var result = new Mat();
        if (responseNormalizeMode.Equals("RawClamp", StringComparison.OrdinalIgnoreCase))
        {
            response32.ConvertTo(result, MatType.CV_8UC1);
            return result;
        }

        if (responseNormalizeMode.Equals("MinMax", StringComparison.OrdinalIgnoreCase))
        {
            using var normalized = new Mat();
            Cv2.Normalize(response32, normalized, 0, 255, NormTypes.MinMax);
            normalized.ConvertTo(result, MatType.CV_8UC1);
            return result;
        }

        var (low, high) = EstimateFloatPercentiles(response32, 0.01, 0.99);
        if (!double.IsFinite(low) || !double.IsFinite(high) || high <= low)
        {
            using var normalized = new Mat();
            Cv2.Normalize(response32, normalized, 0, 255, NormTypes.MinMax);
            normalized.ConvertTo(result, MatType.CV_8UC1);
            return result;
        }

        var scale = 255.0 / (high - low);
        response32.ConvertTo(result, MatType.CV_8UC1, scale, -low * scale);
        return result;
    }

    private static (double Low, double High) EstimateFloatPercentiles(Mat values32f, double lowPercentile, double highPercentile)
    {
        var values = new List<float>(Math.Min(values32f.Rows * values32f.Cols, 262_144));
        var stride = Math.Max(1, (int)Math.Sqrt(Math.Max(1, values32f.Rows * values32f.Cols / 262_144.0)));
        for (var y = 0; y < values32f.Rows; y += stride)
        {
            for (var x = 0; x < values32f.Cols; x += stride)
            {
                var value = values32f.At<float>(y, x);
                if (float.IsFinite(value))
                {
                    values.Add(value);
                }
            }
        }

        if (values.Count == 0)
        {
            return (double.NaN, double.NaN);
        }

        values.Sort();
        return (
            values[PercentileIndex(values.Count, lowPercentile)],
            values[PercentileIndex(values.Count, highPercentile)]);
    }

    private static int PercentileIndex(int count, double percentile)
    {
        return (int)Math.Clamp(Math.Round((count - 1) * percentile), 0, count - 1);
    }

    private static Mat NormalizeForComparison(
        Mat gray,
        string normalizationMode,
        int backgroundKernelSize,
        double claheClipLimit,
        int claheTileGridSize)
    {
        if (normalizationMode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return gray.Clone();
        }

        var comparisonSource = CreateComparisonSource(gray, normalizationMode, claheClipLimit, claheTileGridSize);
        var kernelSize = backgroundKernelSize % 2 == 0 ? backgroundKernelSize + 1 : backgroundKernelSize;
        kernelSize = Math.Max(3, kernelSize);

        try
        {
            using var background = new Mat();
            Cv2.GaussianBlur(comparisonSource, background, new Size(kernelSize, kernelSize), 0);

            var normalized = new Mat();
            Cv2.Absdiff(comparisonSource, background, normalized);
            return normalized;
        }
        finally
        {
            comparisonSource.Dispose();
        }
    }

    private static Mat CreateComparisonSource(Mat gray, string normalizationMode, double claheClipLimit, int claheTileGridSize)
    {
        if (!normalizationMode.Equals("ClaheLocalMean", StringComparison.OrdinalIgnoreCase))
        {
            return gray.Clone();
        }

        using var gray8 = OperatorImageDepthHelper.ConvertSingleChannelToByte(gray, out _, out _);
        using var clahe = Cv2.CreateCLAHE(
            Math.Clamp(claheClipLimit, 0.1, 40.0),
            new Size(Math.Clamp(claheTileGridSize, 2, 64), Math.Clamp(claheTileGridSize, 2, 64)));
        var enhanced = new Mat();
        clahe.Apply(gray8, enhanced);
        return enhanced;
    }

    private static bool AcceptComponentByResponseStats(
        Mat response,
        Point[] contour,
        double componentMeanGate,
        double componentPeakGate,
        out double componentMean,
        out double componentPeak)
    {
        using var mask = new Mat(response.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(mask, new[] { contour }, -1, Scalar.White, -1);
        componentMean = Cv2.Mean(response, mask).Val0;
        Cv2.MinMaxLoc(response, out _, out componentPeak, out _, out _, mask);
        return componentMean >= componentMeanGate || componentPeak >= componentPeakGate;
    }

    private static bool AcceptComponentByShapeStats(
        Point[] contour,
        double area,
        int smallNoiseAreaMax,
        double minElongationForSmallComponent,
        int compactNoiseAreaMax,
        double compactNoiseCircularityMin,
        double compactNoiseFillRatioMin,
        out double elongation,
        out double fillRatio,
        out double circularity,
        out string rejectReason)
    {
        rejectReason = string.Empty;
        var rect = Cv2.BoundingRect(contour);
        var shorterSide = Math.Max(1, Math.Min(rect.Width, rect.Height));
        var longerSide = Math.Max(rect.Width, rect.Height);
        elongation = longerSide / (double)shorterSide;
        fillRatio = area / Math.Max(1.0, rect.Width * rect.Height);
        var perimeter = Cv2.ArcLength(contour, true);
        circularity = perimeter <= 1e-6
            ? 0.0
            : Math.Clamp((4.0 * Math.PI * area) / (perimeter * perimeter), 0.0, 1.0);

        var compactNoiseFilterEnabled =
            compactNoiseAreaMax > 0 &&
            area <= compactNoiseAreaMax &&
            (compactNoiseCircularityMin > 0.0 || compactNoiseFillRatioMin > 0.0);
        if (compactNoiseFilterEnabled)
        {
            var compactByCircularity = compactNoiseCircularityMin <= 0.0 || circularity >= compactNoiseCircularityMin;
            var compactByFill = compactNoiseFillRatioMin <= 0.0 || fillRatio >= compactNoiseFillRatioMin;
            if (compactByCircularity && compactByFill)
            {
                rejectReason = "CompactTextureNoise";
                return false;
            }
        }

        if (smallNoiseAreaMax <= 0 || minElongationForSmallComponent <= 0.0 || area > smallNoiseAreaMax)
        {
            return true;
        }

        if (elongation < minElongationForSmallComponent)
        {
            rejectReason = "SmallLowElongation";
            return false;
        }

        return true;
    }

    private static bool AcceptComponentByLocalResponseProminence(
        Mat response,
        Point[] contour,
        double area,
        int compactNoiseAreaMax,
        double minLocalResponseProminence,
        out double componentMean,
        out double ringMean,
        out double peakProminence)
    {
        componentMean = 0.0;
        ringMean = 0.0;
        peakProminence = 0.0;
        if (compactNoiseAreaMax <= 0 || minLocalResponseProminence <= 0.0 || area > compactNoiseAreaMax)
        {
            return true;
        }

        using var mask = new Mat(response.Size(), MatType.CV_8UC1, Scalar.Black);
        Cv2.DrawContours(mask, new[] { contour }, -1, Scalar.White, -1);
        componentMean = Cv2.Mean(response, mask).Val0;
        Cv2.MinMaxLoc(response, out _, out var componentPeak, out _, out _, mask);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9));
        using var dilated = new Mat();
        Cv2.Dilate(mask, dilated, kernel);
        using var ring = new Mat();
        Cv2.Subtract(dilated, mask, ring);
        if (Cv2.CountNonZero(ring) <= 0)
        {
            return true;
        }

        ringMean = Cv2.Mean(response, ring).Val0;
        peakProminence = componentPeak - ringMean;
        return peakProminence >= minLocalResponseProminence;
    }

    private sealed record CandidateProfileState(bool Enabled, string Profile, bool Applied = false);

    private CandidateProfileState ResolveCandidateProfile(Operator @operator)
    {
        return new CandidateProfileState(
            GetBoolParam(@operator, "EnableCandidateProfile", false),
            NormalizeCandidateProfile(GetStringParam(@operator, "CandidateProfile", CandidateProfileDefault)));
    }

    private static string NormalizeCandidateProfile(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? CandidateProfileDefault
            : raw.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedCandidateProfile(string profile)
    {
        return profile is CandidateProfileDefault or CandidateProfileTaxonomyV2;
    }

    private static Mat AlignReferenceToSource(
        Mat sourceGray,
        Mat referenceGray,
        string alignmentMode,
        out double alignmentScore,
        out Point2d alignmentShift,
        out string rejectedReason)
    {
        alignmentScore = 1.0;
        alignmentShift = new Point2d(0, 0);
        rejectedReason = string.Empty;

        if (alignmentMode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return referenceGray.Clone();
        }

        try
        {
            using var source32 = new Mat();
            using var reference32 = new Mat();
            sourceGray.ConvertTo(source32, MatType.CV_32FC1);
            referenceGray.ConvertTo(reference32, MatType.CV_32FC1);

            using var window = new Mat();
            var rawShift = Cv2.PhaseCorrelate(source32, reference32, window, out var response);
            // PhaseCorrelate reports the shift from source toward reference; invert it because we warp the reference onto the source.
            var shift = new Point2d(-rawShift.X, -rawShift.Y);
            alignmentScore = response;
            alignmentShift = shift;

            using var transform = new Mat(2, 3, MatType.CV_64FC1, Scalar.All(0));
            transform.Set(0, 0, 1.0);
            transform.Set(1, 1, 1.0);
            transform.Set(0, 2, shift.X);
            transform.Set(1, 2, shift.Y);

            var aligned = new Mat();
            Cv2.WarpAffine(referenceGray, aligned, transform, sourceGray.Size(), InterpolationFlags.Linear, BorderTypes.Replicate);

            var shiftMagnitude = Math.Sqrt((shift.X * shift.X) + (shift.Y * shift.Y));
            var maxAcceptedShift = Math.Min(sourceGray.Width, sourceGray.Height) * MaxAcceptedShiftRatio;
            var baselineDifference = ComputeMeanAbsoluteDifference(sourceGray, referenceGray);
            var alignedDifference = ComputeMeanAbsoluteDifference(sourceGray, aligned);
            var improvementRatio = baselineDifference <= 1e-6
                ? 0.0
                : (baselineDifference - alignedDifference) / baselineDifference;
            var allowedDifferenceIncrease = Math.Max(2.0, baselineDifference * 0.12);

            if (shiftMagnitude > maxAcceptedShift)
            {
                rejectedReason =
                    $"PhaseCorrelation translation alignment rejected: estimated shift ({shift.X:F2}, {shift.Y:F2}) exceeds the supported translation range.";
            }
            else if (response < MinAcceptedPhaseCorrelationResponse)
            {
                rejectedReason =
                    $"PhaseCorrelation translation alignment rejected: response {response:F3} is below {MinAcceptedPhaseCorrelationResponse:F3}.";
            }
            else if (shiftMagnitude > 0.5 &&
                     alignedDifference > baselineDifference + allowedDifferenceIncrease &&
                     improvementRatio < MinAcceptedImprovementRatio)
            {
                rejectedReason =
                    $"PhaseCorrelation translation alignment rejected: translation-only alignment changed similarity by only {improvementRatio:P1}.";
            }

            if (!string.IsNullOrEmpty(rejectedReason))
            {
                aligned.Dispose();
                return referenceGray.Clone();
            }

            return aligned;
        }
        catch (Exception ex)
        {
            alignmentScore = 0.0;
            alignmentShift = new Point2d(0, 0);
            rejectedReason = $"Alignment failed: {ex.Message}";
            return referenceGray.Clone();
        }
    }

    private static double ComputeMeanAbsoluteDifference(Mat first, Mat second)
    {
        using var diff = new Mat();
        Cv2.Absdiff(first, second, diff);
        return Cv2.Mean(diff).Val0;
    }

    private static double ApplyThreshold(
        Mat response,
        Mat binary,
        string method,
        string thresholdMode,
        double manualThreshold,
        double referenceStatsSigma,
        bool robustReferenceStats)
    {
        var resolvedMode = ResolveThresholdMode(method, thresholdMode);
        switch (resolvedMode.ToLowerInvariant())
        {
            case "manual":
                Cv2.Threshold(response, binary, manualThreshold, 255, ThresholdTypes.Binary);
                return manualThreshold;
            case "otsu":
                return Cv2.Threshold(response, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            case "referencestats":
                var computed = robustReferenceStats
                    ? ComputeRobustReferenceStatsThreshold(response, manualThreshold, referenceStatsSigma)
                    : ComputeReferenceStatsThreshold(response, manualThreshold, referenceStatsSigma);
                Cv2.Threshold(response, binary, computed, 255, ThresholdTypes.Binary);
                return computed;
            default:
                Cv2.Threshold(response, binary, manualThreshold, 255, ThresholdTypes.Binary);
                return manualThreshold;
        }
    }

    private static string ResolveThresholdMode(string method, string thresholdMode)
    {
        if (!thresholdMode.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return thresholdMode;
        }

        return method.Equals("ReferenceDiff", StringComparison.OrdinalIgnoreCase)
            ? "ReferenceStats"
            : "Otsu";
    }

    private static double ComputeReferenceStatsThreshold(Mat response, double manualFloor, double sigma)
    {
        Cv2.MeanStdDev(response, out var mean, out var stddev);
        var computed = mean.Val0 + (stddev.Val0 * sigma);
        return Math.Clamp(Math.Max(manualFloor, computed), 0.0, 255.0);
    }

    private static double ComputeRobustReferenceStatsThreshold(Mat response, double manualFloor, double sigma)
    {
        using var responseByte = response.Depth() == MatType.CV_8U
            ? response.Clone()
            : OperatorImageDepthHelper.ConvertSingleChannelToByte(response, out _, out _);

        var histogram = BuildByteHistogram(responseByte);
        var median = HistogramPercentile(histogram, 0.50);
        var mad = HistogramMedianAbsoluteDeviation(histogram, median);
        var robustSigma = 1.4826 * mad;
        var computedByte = Math.Clamp(Math.Max(manualFloor, median + (sigma * robustSigma)), 0.0, 255.0);

        return response.Depth() == MatType.CV_8U
            ? computedByte
            : OperatorImageDepthHelper.ResolveThresholdToNativeRange(response, computedByte);
    }

    private static int[] BuildByteHistogram(Mat image)
    {
        if (image.Type() != MatType.CV_8UC1)
        {
            throw new ArgumentException("Expected a single-channel 8-bit image.", nameof(image));
        }

        var histogram = new int[256];
        for (var y = 0; y < image.Rows; y++)
        {
            for (var x = 0; x < image.Cols; x++)
            {
                histogram[image.At<byte>(y, x)]++;
            }
        }

        return histogram;
    }

    private static double HistogramPercentile(IReadOnlyList<int> histogram, double percentile)
    {
        var total = histogram.Sum();
        if (total <= 0)
        {
            return 0.0;
        }

        var target = Math.Max(1, (int)Math.Ceiling(total * percentile));
        var cumulative = 0;
        for (var i = 0; i < histogram.Count; i++)
        {
            cumulative += histogram[i];
            if (cumulative >= target)
            {
                return i;
            }
        }

        return histogram.Count - 1;
    }

    private static double HistogramMedianAbsoluteDeviation(IReadOnlyList<int> histogram, double median)
    {
        var deviationHistogram = new int[256];
        for (var i = 0; i < histogram.Count; i++)
        {
            var deviation = (int)Math.Clamp(Math.Round(Math.Abs(i - median)), 0, 255);
            deviationHistogram[deviation] += histogram[i];
        }

        return HistogramPercentile(deviationHistogram, 0.50);
    }
}
