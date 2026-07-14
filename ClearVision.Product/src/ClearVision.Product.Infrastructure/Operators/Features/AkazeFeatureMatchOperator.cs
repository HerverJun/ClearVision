using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "AKAZE特征匹配",
    Description = "使用 AKAZE 局部特征与单应性校验进行模板定位，适合纹理目标的稳健匹配。",
    CategoryId = OperatorCategoryId.MatchingAndLocalization,
    IconName = "feature-match"
)]
[InputPort("Image", "搜索图像", PortDataType.Image, IsRequired = true)]
[InputPort("Template", "模板图像", PortDataType.Image, IsRequired = false)]
[OutputPort("Image", "结果图像", PortDataType.Image)]
[OutputPort("Position", "匹配位置", PortDataType.Point)]
[OutputPort("MatchPoint", "代表匹配点", PortDataType.Point)]
[OutputPort("IsMatch", "是否匹配", PortDataType.Boolean)]
[OutputPort("Score", "匹配分数", PortDataType.Float)]
[OperatorParam("TemplatePath", "模板路径", "file", DefaultValue = "")]
[OperatorParam("Threshold", "检测阈值", "double", DefaultValue = 0.001, Min = 0.0001, Max = 0.1)]
[OperatorParam("MinMatchCount", "最小匹配数", "int", DefaultValue = 10, Min = 3, Max = 100)]
[OperatorParam("EnableSymmetryTest", "对称测试", "bool", DefaultValue = true)]
[OperatorParam("MaxFeatures", "最大特征点", "int", DefaultValue = 500, Min = 100, Max = 2000)]
[OperatorParam("EnableCandidateProfile", "Enable Candidate Profile", "bool", DefaultValue = false)]
[OperatorParam("CandidateProfile", "Candidate Profile", "enum", DefaultValue = "default", Options = new[] { "default|Default", "default_v3|AKAZE default_v3" })]
[OutputPort("InlierRatio", "Inlier Ratio", PortDataType.Float)]
[OutputPort("MeanReprojectionError", "Mean Reprojection Error", PortDataType.Float)]
[OutputPort("MaxReprojectionError", "Max Reprojection Error", PortDataType.Float)]
[OutputPort("AreaRatio", "Projected Area Ratio", PortDataType.Float)]
[OutputPort("CornersInsideCount", "Projected Corners Inside", PortDataType.Integer)]
[OutputPort("ProjectedCenterInside", "Projected Center Inside", PortDataType.Boolean)]
[OutputPort("Corners", "Projected Corners", PortDataType.PointList)]
[OutputPort("HomographyFailureReason", "Homography Failure Reason", PortDataType.String)]
[OperatorParam("MatchRatio", "Match Ratio (Lowe's)", "double", DefaultValue = 0.75, Min = 0.5, Max = 0.95)]
[OperatorParam("RansacThreshold", "RANSAC Threshold (px)", "double", DefaultValue = 5.0, Min = 0.5, Max = 10.0)]
[OperatorParam("MinInlierRatio", "Min Inlier Ratio", "double", DefaultValue = 0.25, Min = 0.1, Max = 1.0)]
[OperatorParam("AllowCenterOnlyProjection", "Allow Center-Only Projection", "bool", DefaultValue = false)]
[OperatorParam("OriginMode", "Origin Mode", "enum", DefaultValue = "Center", Options = new[] { "Center|Center", "TopLeft|TopLeft", "Custom|Custom" })]
[OperatorParam("OriginX", "Origin X", "double", DefaultValue = 0.0)]
[OperatorParam("OriginY", "Origin Y", "double", DefaultValue = 0.0)]
[AlgorithmInfo(
    Name = "AKAZE Homography Feature Match",
    CoreApi = "OpenCvSharp.AKAZE + BFMatcher(Hamming) + FindHomography(RANSAC)",
    ImplementationStrategy = "Extract AKAZE binary features from the scene and template, optionally apply bidirectional symmetry filtering, estimate a RANSAC homography, and report both the configured reference Position and representative MatchPoint.",
    TimeComplexity = "O(P + T*S) where P is image pixels and T/S are retained template and scene descriptors",
    TypicalLatency = "FeatureMatchContractRunner baseline: 22 cases passed, avg runtime about 11.7 ms on synthetic contract images.",
    SpaceComplexity = "O(P + T + S) plus bounded static template cache entries for TemplatePath mode.",
    SuitableUseCases = new[]
    {
        "Textured labels, PCB marks, printed features, and local parts with enough corners or blob-like texture.",
        "Template localization where moderate rotation, scale, or perspective variation is expected.",
        "Pipelines that need a business-level NG result image instead of a framework-level failure for no-match cases."
    },
    UnsuitableUseCases = new[]
    {
        "Weak-texture, pure-color, or strongly repetitive targets where homography inliers are ambiguous.",
        "Subpixel metrology or robot-pick centers that require calibrated geometric center output.",
        "Very high-texture full-frame scenes without ROI constraints, because scene descriptors are not globally capped."
    },
    KnownLimitations = new[]
    {
        "Score is a homography verification score based on inlier evidence, not a normalized template-correlation score.",
        "MatchRatio, RANSAC threshold, MinMatchCount, and MinInlierRatio are configurable and should be validated with replay evidence.",
        "TemplatePath mode uses a bounded in-process cache keyed by file fingerprint and detector configuration."
    },
    Dependencies = new[] { "OpenCvSharp" }
)]
public class AkazeFeatureMatchOperator : FeatureMatchOperatorBase
{
    public override OperatorType OperatorType => OperatorType.AkazeFeatureMatch;

    public AkazeFeatureMatchOperator(ILogger<AkazeFeatureMatchOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像。"));
        }

        var templatePath = GetStringParam(@operator, "TemplatePath", "");
        var threshold = GetDoubleParam(@operator, "Threshold", 0.001, min: 0.0001, max: 0.1);
        var minMatchCount = GetIntParam(@operator, "MinMatchCount", 10, min: 3, max: 100);
        var enableSymmetryTest = GetBoolParam(@operator, "EnableSymmetryTest", true);
        var maxFeatures = GetIntParam(@operator, "MaxFeatures", 500, min: 100, max: 2000);
        var matchRatio = GetDoubleParam(@operator, "MatchRatio", 0.75, min: 0.5, max: 0.95);
        var ransacThreshold = GetDoubleParam(@operator, "RansacThreshold", 5.0, min: 0.5, max: 10.0);
        var minInlierRatio = GetDoubleParam(@operator, "MinInlierRatio", 0.25, min: 0.1, max: 1.0);
        var allowCenterOnlyProjection = GetBoolParam(@operator, "AllowCenterOnlyProjection", false);
        var candidateProfile = ResolveFeatureMatchCandidateProfile(@operator);
        if (candidateProfile.Enabled && candidateProfile.Name == "default_v3")
        {
            threshold = 0.001;
            minMatchCount = 6;
            maxFeatures = 1200;
            matchRatio = 0.75;
            ransacThreshold = 5.0;
            minInlierRatio = 0.25;
            allowCenterOnlyProjection = false;
            candidateProfile = candidateProfile with { Applied = true };
        }

        var srcImage = imageWrapper.GetMat();
        using var srcGray = ToGray(srcImage);
        using var akaze = AKAZE.Create(threshold: (float)threshold);

        var srcDescriptors = new Mat();
        akaze.DetectAndCompute(srcGray, null, out KeyPoint[] srcKeyPoints, srcDescriptors);
        if (srcKeyPoints.Length < 4 || srcDescriptors.Empty())
        {
            srcDescriptors.Dispose();
            return Task.FromResult(CreateFailedOutput(srcImage, "场景特征点不足。", 0, 0, candidateProfile));
        }

        Mat? templateImage = null;
        Mat? templateDescriptors = null;
        KeyPoint[]? templateKeyPoints = null;
        var disposeTemplateImage = false;

        try
        {
            if (TryGetInputImage(inputs, "Template", out var templateWrapper) && templateWrapper != null)
            {
                templateImage = templateWrapper.GetMat();
                using var templateGray = ToGray(templateImage);
                templateDescriptors = new Mat();
                akaze.DetectAndCompute(templateGray, null, out templateKeyPoints, templateDescriptors);
            }
            else if (!string.IsNullOrWhiteSpace(templatePath))
            {
                var cached = GetOrLoadTemplate(
                    templatePath,
                    $"AKAZE:{threshold:F6}:{maxFeatures}",
                    templateGray =>
                    {
                        var descriptors = new Mat();
                        akaze.DetectAndCompute(templateGray, null, out KeyPoint[] keyPoints, descriptors);
                        return (keyPoints, descriptors);
                    });

                if (cached.HasValue)
                {
                    (templateImage, templateKeyPoints, templateDescriptors) = cached.Value;
                    disposeTemplateImage = true;
                }
            }

            if (templateImage == null || templateKeyPoints == null || templateKeyPoints.Length < 4 || templateDescriptors == null || templateDescriptors.Empty())
            {
                return Task.FromResult(CreateFailedOutput(srcImage, "模板特征点不足。", 0, 0, candidateProfile));
            }

            var (filteredTemplateKeyPoints, filteredTemplateDescriptors) = FilterFeatures(templateKeyPoints, templateDescriptors, maxFeatures);
            templateDescriptors.Dispose();
            templateDescriptors = filteredTemplateDescriptors;
            templateKeyPoints = filteredTemplateKeyPoints;

            List<DMatch> goodMatches;
            if (enableSymmetryTest)
            {
                goodMatches = MatchWithSymmetryTest(templateDescriptors, srcDescriptors, matchRatio);
            }
            else
            {
                using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);
                var matches = matcher.KnnMatch(templateDescriptors, srcDescriptors, k: 2);
                goodMatches = new List<DMatch>();
                foreach (var match in matches)
                {
                    if (match.Length >= 2 && match[0].Distance < matchRatio * match[1].Distance)
                    {
                        goodMatches.Add(match[0]);
                    }
                }
            }

            var (homography, corners, metrics) = EstimateAndVerifyHomography(
                templateKeyPoints,
                srcKeyPoints,
                goodMatches,
                new Size(templateImage.Width, templateImage.Height),
                srcImage.Size(),
                ransacThreshold,
                minMatchCount,
                minInliers: minMatchCount,
                minInlierRatio,
                allowCenterOnlyProjection);
            var origin = ResolveReferenceOrigin(@operator, templateImage.Size());

            var verificationScore = HomographyVerificationHelper.ComputeVerificationScore(metrics, ransacThreshold);
            var isMatch = metrics.VerificationPassed;
            var resultImage = srcImage.Clone();
            var boxColor = isMatch ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

            if (homography != null && !homography.Empty() && isMatch)
            {
                DrawPerspectiveBox(resultImage, homography, templateImage.Width, templateImage.Height, boxColor);
            }

            var representativePoint = new Point(resultImage.Width / 2, resultImage.Height / 2);
            if (goodMatches.Count > 0)
            {
                var bestMatch = goodMatches[0];
                representativePoint = new Point(
                    (int)srcKeyPoints[bestMatch.TrainIdx].Pt.X,
                    (int)srcKeyPoints[bestMatch.TrainIdx].Pt.Y);
                Cv2.DrawMarker(resultImage, representativePoint, boxColor, MarkerTypes.Cross, 20, 2);
            }

            var position = TryProjectReferencePoint(homography, origin, corners, out var projectedCenter)
                ? projectedCenter
                : new Position(representativePoint.X, representativePoint.Y);

            Cv2.PutText(
                resultImage,
                $"{(isMatch ? "OK" : "NG")}: Inliers={metrics.InlierCount}/{metrics.MatchCount}",
                new Point(10, 30),
                HersheyFonts.HersheySimplex,
                0.6,
                boxColor,
                2);

            if (!isMatch && !string.IsNullOrWhiteSpace(metrics.FailureReason))
            {
                Cv2.PutText(
                    resultImage,
                    ToOpenCvOverlayText(metrics.FailureReason),
                    new Point(10, 60),
                    HersheyFonts.HersheySimplex,
                    0.5,
                    boxColor,
                    2);
            }

            homography?.Dispose();

            var outputData = new Dictionary<string, object>
            {
                { "IsMatch", isMatch },
                { "Score", verificationScore },
                { "Inliers", metrics.InlierCount },
                { "TotalMatches", metrics.MatchCount },
                { "InlierRatio", metrics.InlierRatio },
                { "Position", position },
                { "MatchPoint", new Position(representativePoint.X, representativePoint.Y) },
                { "X", position.X },
                { "Y", position.Y },
                { "ScoreDefinition", "HomographyVerificationScore" },
                { "FailureReason", metrics.FailureReason },
                { "HomographyFailureReason", metrics.FailureReason },
                { "MeanReprojectionError", metrics.MeanReprojectionError },
                { "MaxReprojectionError", metrics.MaxReprojectionError },
                { "AreaRatio", metrics.AreaRatio },
                { "CornersInsideCount", metrics.CornersInsideCount },
                { "ProjectedCenterInside", metrics.ProjectedCenterInside },
                { "Corners", corners.Select(c => new Position(c.X, c.Y)).ToList() },
                { "OriginMode", GetStringParam(@operator, "OriginMode", "Center") }
            };
            AddFeatureMatchCandidateProfileOutputs(outputData, candidateProfile);

            return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, outputData)));
        }
        finally
        {
            srcDescriptors.Dispose();
            templateDescriptors?.Dispose();
            if (disposeTemplateImage)
            {
                templateImage?.Dispose();
            }
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var candidateValidation = ValidateFeatureMatchCandidateProfile(@operator, "default_v3");
        if (!candidateValidation.IsValid)
        {
            return candidateValidation;
        }

        var threshold = GetDoubleParam(@operator, "Threshold", 0.001);
        if (threshold < 0.0001 || threshold > 0.1)
        {
            return ValidationResult.Invalid("检测阈值必须在 0.0001-0.1 之间。");
        }

        var minMatchCount = GetIntParam(@operator, "MinMatchCount", 10);
        if (minMatchCount < 3 || minMatchCount > 100)
        {
            return ValidationResult.Invalid("最小匹配数必须在 3-100 之间。");
        }

        var matchRatio = GetDoubleParam(@operator, "MatchRatio", 0.75);
        if (matchRatio < 0.5 || matchRatio > 0.95)
        {
            return ValidationResult.Invalid("MatchRatio must be between 0.5 and 0.95.");
        }

        var ransacThreshold = GetDoubleParam(@operator, "RansacThreshold", 5.0);
        if (ransacThreshold < 0.5 || ransacThreshold > 10.0)
        {
            return ValidationResult.Invalid("RansacThreshold must be between 0.5 and 10.0.");
        }

        var minInlierRatio = GetDoubleParam(@operator, "MinInlierRatio", 0.25);
        if (minInlierRatio < 0.1 || minInlierRatio > 1.0)
        {
            return ValidationResult.Invalid("MinInlierRatio must be between 0.1 and 1.0.");
        }

        return ValidationResult.Valid();
    }

    private static Mat ToGray(Mat src)
    {
        if (src.Channels() == 1)
        {
            return src.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private OperatorExecutionOutput CreateFailedOutput(
        Mat input,
        string reason,
        int inliers,
        int totalMatches,
        FeatureMatchCandidateProfile candidateProfile)
    {
        var output = input.Clone();
        Cv2.PutText(output, $"NG: {ToOpenCvOverlayText(reason)}", new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
        Cv2.PutText(output, $"Score: {inliers}/{totalMatches}", new Point(10, 60), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);

        var outputData = new Dictionary<string, object>
        {
            { "IsMatch", false },
            { "Score", 0.0 },
            { "Inliers", inliers },
            { "TotalMatches", totalMatches },
            { "InlierRatio", 0.0 },
            { "Message", reason },
            { "FailureReason", reason },
            { "HomographyFailureReason", reason },
            { "Position", new Position(0, 0) },
            { "MatchPoint", new Position(0, 0) },
            { "X", 0 },
            { "Y", 0 },
            { "ScoreDefinition", "HomographyVerificationScore" },
            { "MeanReprojectionError", double.PositiveInfinity },
            { "MaxReprojectionError", double.PositiveInfinity },
            { "AreaRatio", 0.0 },
            { "CornersInsideCount", 0 },
            { "ProjectedCenterInside", false }
        };
        AddFeatureMatchCandidateProfileOutputs(outputData, candidateProfile);

        return OperatorExecutionOutput.Success(CreateImageOutput(output, outputData));
    }

    private Position ResolveReferenceOrigin(Operator @operator, Size templateSize)
    {
        var originMode = GetStringParam(@operator, "OriginMode", "Center");
        return originMode.Trim().ToLowerInvariant() switch
        {
            "topleft" => new Position(0, 0),
            "custom" => new Position(
                GetDoubleParam(@operator, "OriginX", 0.0),
                GetDoubleParam(@operator, "OriginY", 0.0)),
            _ => new Position(templateSize.Width / 2.0, templateSize.Height / 2.0)
        };
    }

    private static bool TryProjectReferencePoint(Mat? homography, Position origin, Point2f[] projectedCorners, out Position center)
    {
        center = new Position(0, 0);
        if (homography != null && !homography.Empty())
        {
            var projected = Cv2.PerspectiveTransform(new[] { new Point2f((float)origin.X, (float)origin.Y) }, homography);
            if (projected.Length == 1)
            {
                center = new Position(projected[0].X, projected[0].Y);
                return true;
            }
        }

        if (projectedCorners.Length != 4)
        {
            return false;
        }

        center = new Position(projectedCorners.Average(point => point.X), projectedCorners.Average(point => point.Y));
        return true;
    }
}
