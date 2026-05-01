// TemplateMatchOperator.cs
// 模板匹配算子 - 在图像中查找模板位置
// 作者：ClearVision Team

using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 模板匹配算子 - 在图像中查找模板位置。
/// </summary>
[OperatorMeta(
    DisplayName = "模板匹配",
    Description = "Classic template matching with optional bounded rotation/scale pose search. Multi-match outputs are filtered by IoU-based NMS.",
    Category = "匹配定位",
    IconName = "template",
    Keywords = new[] { "模板匹配", "定位", "找图", "Template", "Match", "Locate" },
    Version = "1.2.0"
)]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = true)]
[InputPort("Template", "模板图像", PortDataType.Image, IsRequired = true)]
[InputPort("Mask", "搜索掩膜", PortDataType.Image, IsRequired = false)]
[OutputPort("Image", "结果图像", PortDataType.Image)]
[OutputPort("Position", "匹配位置", PortDataType.Point)]
[OutputPort("Score", "匹配分数", PortDataType.Float)]
[OutputPort("NormalizedScore", "规范化分数", PortDataType.Float)]
[OutputPort("RawResponse", "原始响应值", PortDataType.Float)]
[OutputPort("SubpixelOffsetX", "亚像素峰值 X 偏移", PortDataType.Float)]
[OutputPort("SubpixelOffsetY", "亚像素峰值 Y 偏移", PortDataType.Float)]
[OutputPort("PeakCurvature", "响应峰曲率", PortDataType.Float)]
[OutputPort("Angle", "匹配角度", PortDataType.Float)]
[OutputPort("Scale", "匹配尺度", PortDataType.Float)]
[OutputPort("IsMatch", "是否匹配", PortDataType.Boolean)]
[OutputPort("Matches", "匹配列表", PortDataType.Any)]
[OutputPort("MatchCount", "匹配数量", PortDataType.Integer)]
[OperatorParam("Method", "匹配方法", "enum", DefaultValue = "CCoeffNormed", Options = new[]
{
    "CCoeffNormed|CCoeffNormed",
    "SqDiff|SqDiff",
    "SqDiffNormed|SqDiffNormed",
    "CCorr|CCorr",
    "CCorrNormed|CCorrNormed",
    "CCoeff|CCoeff"
})]
[OperatorParam("Domain", "匹配域", "enum", DefaultValue = "Gray", Options = new[]
{
    "Gray|Gray",
    "Edge|Edge",
    "Gradient|Gradient"
})]
[OperatorParam("Threshold", "匹配分数阈值", "double", DefaultValue = 0.8, Min = 0.0, Max = 1.0)]
[OperatorParam("MaxMatches", "最大匹配数", "int", DefaultValue = 1, Min = 1, Max = 100)]
[OperatorParam("UseRoi", "使用 ROI", "bool", DefaultValue = false)]
[OperatorParam("RoiX", "ROI X", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiY", "ROI Y", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiWidth", "ROI Width", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiHeight", "ROI Height", "int", DefaultValue = 0, Min = 0)]
[OperatorParam("OriginMode", "Origin Mode", "enum", DefaultValue = "Center", Options = new[] { "Center|Center", "TopLeft|TopLeft", "Custom|Custom" })]
[OperatorParam("OriginX", "Origin X", "double", DefaultValue = 0.0)]
[OperatorParam("OriginY", "Origin Y", "double", DefaultValue = 0.0)]
[OperatorParam("EnablePoseSearch", "启用姿态搜索", "bool", DefaultValue = false)]
[OperatorParam("AngleStart", "角度起点", "double", DefaultValue = 0.0, Min = -180.0, Max = 180.0)]
[OperatorParam("AngleExtent", "角度范围", "double", DefaultValue = 0.0, Min = 0.0, Max = 360.0)]
[OperatorParam("AngleStep", "角度步长", "double", DefaultValue = 1.0, Min = 0.1, Max = 45.0)]
[OperatorParam("ScaleMin", "最小尺度", "double", DefaultValue = 1.0, Min = 0.2, Max = 3.0)]
[OperatorParam("ScaleMax", "最大尺度", "double", DefaultValue = 1.0, Min = 0.2, Max = 3.0)]
[OperatorParam("ScaleStep", "尺度步长", "double", DefaultValue = 0.05, Min = 0.01, Max = 1.0)]
[OperatorParam("PyramidLevels", "姿态搜索金字塔层数", "int", DefaultValue = 1, Min = 1, Max = 4)]
public class TemplateMatchOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.TemplateMatching;

    public TemplateMatchOperator(ILogger<TemplateMatchOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像。"));
        }

        if (!TryGetInputImage(inputs, "Template", out var templateWrapper) || templateWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供模板图像。"));
        }

        var threshold = GetDoubleParam(@operator, "Threshold", 0.8, min: 0, max: 1);
        var method = GetStringParam(@operator, "Method", "CCoeffNormed");
        var domain = GetStringParam(@operator, "Domain", "Gray");
        var maxMatches = GetIntParam(@operator, "MaxMatches", 1, min: 1, max: 100);
        var useRoi = GetBoolParam(@operator, "UseRoi", false);
        var roiX = GetIntParam(@operator, "RoiX", 0);
        var roiY = GetIntParam(@operator, "RoiY", 0);
        var roiWidth = GetIntParam(@operator, "RoiWidth", 0);
        var roiHeight = GetIntParam(@operator, "RoiHeight", 0);
        var poseSearch = ReadPoseSearchOptions(@operator);

        var src = imageWrapper.GetMat();
        var template = templateWrapper.GetMat();

        if (src.Empty() || template.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("无法解码图像。"));
        }

        if (template.Width > src.Width || template.Height > src.Height)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("模板尺寸不能大于源图像。"));
        }

        Mat searchRegion = src;
        Rect roi = new Rect(0, 0, src.Width, src.Height);
        var disposeSearchRegion = false;
        if (useRoi && roiWidth > 0 && roiHeight > 0)
        {
            roi = new Rect(roiX, roiY, roiWidth, roiHeight);
            roi = roi.Intersect(new Rect(0, 0, src.Width, src.Height));
            if (roi.Width > 0 && roi.Height > 0)
            {
                searchRegion = new Mat(src, roi);
                disposeSearchRegion = true;
            }
        }

        try
        {
            using var preparedSearch = PrepareMatchImage(searchRegion, domain);
            using var preparedTemplate = PrepareMatchImage(template, domain);
            using var searchMask = PrepareSearchMask(inputs, roi, preparedSearch.Size());

            if (!HasSufficientSignal(preparedTemplate))
            {
                return Task.FromResult(CreateNoMatchOutput(src, GetMethodDescriptor(ResolveMatchMethod(method), domain), preparedTemplate.Size(), "Template contains insufficient texture for stable matching.", poseSearch));
            }

            if (preparedTemplate.Width > preparedSearch.Width || preparedTemplate.Height > preparedSearch.Height)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("模板尺寸不能大于搜索区域。"));
            }

            var origin = ResolveReferenceOrigin(@operator, preparedTemplate.Size());
            var matchMethod = ResolveMatchMethod(method);

            var matches = poseSearch.Enabled
                ? FindPoseMatches(preparedSearch, preparedTemplate, maxMatches, threshold, matchMethod, searchMask, origin, poseSearch)
                : FindFixedPoseMatches(preparedSearch, preparedTemplate, maxMatches, threshold, matchMethod, searchMask, origin);
            if (roi.X != 0 || roi.Y != 0)
            {
                matches = matches.Select(match => match.Offset(roi.X, roi.Y)).ToList();
            }

            var isMatch = matches.Count > 0;
            var resultImage = src.Clone();
            foreach (var match in matches)
            {
                Cv2.Rectangle(resultImage, match.TopLeft, match.BottomRight, new Scalar(0, 255, 0), 2);
                var reference = match.GetReferencePosition();
                Cv2.DrawMarker(resultImage, new Point((int)Math.Round(reference.X), (int)Math.Round(reference.Y)), new Scalar(0, 0, 255), MarkerTypes.Cross, 20, 2);
                Cv2.PutText(
                    resultImage,
                    $"{match.NormalizedScore:F3}",
                    new Point(match.TopLeft.X, Math.Max(16, match.TopLeft.Y - 8)),
                    HersheyFonts.HersheySimplex,
                    0.5,
                    new Scalar(0, 255, 0),
                    1);
            }

            var bestMatch = matches.FirstOrDefault();
            var position = bestMatch?.GetReferencePosition() ?? new Position(0, 0);
            var methodDescriptor = GetMethodDescriptor(matchMethod, domain);
            if (!isMatch)
            {
                return Task.FromResult(CreateNoMatchOutput(src, methodDescriptor, preparedTemplate.Size(), "No match above threshold.", poseSearch));
            }

            var additionalData = new Dictionary<string, object>
            {
                ["IsMatch"] = true,
                ["Found"] = true,
                ["Score"] = bestMatch!.Score,
                ["NormalizedScore"] = bestMatch.NormalizedScore,
                ["RawResponse"] = bestMatch.RawResponse,
                ["SubpixelOffsetX"] = bestMatch.SubpixelOffsetX,
                ["SubpixelOffsetY"] = bestMatch.SubpixelOffsetY,
                ["PeakCurvature"] = bestMatch.PeakCurvature,
                ["Angle"] = bestMatch.AngleDeg,
                ["Scale"] = bestMatch.Scale,
                ["PoseSearchEnabled"] = poseSearch.Enabled,
                ["PyramidLevels"] = bestMatch.PyramidLevels,
                ["Method"] = methodDescriptor,
                ["FailureReason"] = string.Empty,
                ["Position"] = position,
                ["X"] = position.X,
                ["Y"] = position.Y,
                ["MatchCount"] = matches.Count,
                ["Matches"] = matches.Select(m => new Dictionary<string, object>
                {
                    ["Position"] = m.GetReferencePosition(),
                    ["Center"] = m.Center,
                    ["TopLeft"] = new Position(m.TopLeft.X, m.TopLeft.Y),
                    ["Score"] = m.Score,
                    ["NormalizedScore"] = m.NormalizedScore,
                    ["RawResponse"] = m.RawResponse,
                    ["SubpixelOffsetX"] = m.SubpixelOffsetX,
                    ["SubpixelOffsetY"] = m.SubpixelOffsetY,
                    ["PeakCurvature"] = m.PeakCurvature,
                    ["Angle"] = m.AngleDeg,
                    ["Scale"] = m.Scale,
                    ["PyramidLevels"] = m.PyramidLevels,
                    ["Width"] = m.TemplateWidth,
                    ["Height"] = m.TemplateHeight
                }).ToList(),
                ["TemplateWidth"] = preparedTemplate.Width,
                ["TemplateHeight"] = preparedTemplate.Height,
                ["MatchedTemplateWidth"] = bestMatch.TemplateWidth,
                ["MatchedTemplateHeight"] = bestMatch.TemplateHeight,
                ["Domain"] = NormalizeDomain(domain),
                ["OriginMode"] = GetStringParam(@operator, "OriginMode", "Center")
            };

            return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, additionalData)));
        }
        finally
        {
            if (disposeSearchRegion)
            {
                searchRegion.Dispose();
            }
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var threshold = GetDoubleParam(@operator, "Threshold", 0.8);
        if (threshold < 0 || threshold > 1)
        {
            return ValidationResult.Invalid("阈值必须在 0-1 之间。");
        }

        var method = GetStringParam(@operator, "Method", "CCoeffNormed");
        var validMethods = new[] { "NCC", "SqDiff", "SqDiffNormed", "CCorr", "CCorrNormed", "CCoeff", "CCoeffNormed" };
        if (!validMethods.Contains(method, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid($"不支持的匹配方法: {method}");
        }

        var maxMatches = GetIntParam(@operator, "MaxMatches", 1);
        if (maxMatches < 1 || maxMatches > 100)
        {
            return ValidationResult.Invalid("MaxMatches must be between 1 and 100.");
        }

        var domain = NormalizeDomain(GetStringParam(@operator, "Domain", "Gray"));
        if (domain is not ("Gray" or "Edge" or "Gradient"))
        {
            return ValidationResult.Invalid("Domain must be Gray, Edge, or Gradient.");
        }

        var roiWidth = GetIntParam(@operator, "RoiWidth", 0);
        var roiHeight = GetIntParam(@operator, "RoiHeight", 0);
        if (roiWidth < 0 || roiHeight < 0)
        {
            return ValidationResult.Invalid("ROI dimensions must be non-negative.");
        }

        if (GetBoolParam(@operator, "EnablePoseSearch", false))
        {
            var angleExtent = GetDoubleParam(@operator, "AngleExtent", 0.0);
            var angleStep = GetDoubleParam(@operator, "AngleStep", 1.0);
            var scaleMin = GetDoubleParam(@operator, "ScaleMin", 1.0);
            var scaleMax = GetDoubleParam(@operator, "ScaleMax", 1.0);
            var scaleStep = GetDoubleParam(@operator, "ScaleStep", 0.05);
            var pyramidLevels = GetIntParam(@operator, "PyramidLevels", 1);
            if (angleExtent < 0 || angleExtent > 360 || angleStep < 0.1 || angleStep > 45)
            {
                return ValidationResult.Invalid("AngleExtent must be 0..360 and AngleStep must be 0.1..45.");
            }

            if (scaleMin < 0.2 || scaleMax > 3.0 || scaleMin > scaleMax || scaleStep < 0.01 || scaleStep > 1.0)
            {
                return ValidationResult.Invalid("ScaleMin/ScaleMax must be within [0.2, 3.0], ScaleMin <= ScaleMax, and ScaleStep must be 0.01..1.0.");
            }

            if (pyramidLevels < 1 || pyramidLevels > 4)
            {
                return ValidationResult.Invalid("PyramidLevels must be between 1 and 4.");
            }
        }

        return ValidationResult.Valid();
    }

    private static Mat PrepareMatchImage(Mat src, string domain)
    {
        using var gray = ToGray(src);
        return NormalizeDomain(domain) switch
        {
            "Edge" => BuildEdgeMap(gray),
            "Gradient" => BuildGradientMap(gray),
            _ => gray.Clone()
        };
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

    private static Mat BuildEdgeMap(Mat gray)
    {
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        using var otsuSource = new Mat();
        var otsuThreshold = Cv2.Threshold(blurred, otsuSource, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
        var low = Math.Max(10.0, otsuThreshold * 0.5);
        var high = Math.Max(low + 10.0, otsuThreshold);

        var edges = new Mat();
        Cv2.Canny(blurred, edges, low, high);
        return edges;
    }

    private static Mat BuildGradientMap(Mat gray)
    {
        using var gradX = new Mat();
        using var gradY = new Mat();
        using var magnitude = new Mat();
        Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(gray, gradY, MatType.CV_32F, 0, 1, 3);
        Cv2.Magnitude(gradX, gradY, magnitude);

        var normalized = new Mat();
        Cv2.Normalize(magnitude, normalized, 0, 255, NormTypes.MinMax, MatType.CV_8UC1);
        return normalized;
    }

    private Mat? PrepareSearchMask(Dictionary<string, object>? inputs, Rect roi, Size searchSize)
    {
        if (!TryGetInputImage(inputs, "Mask", out var maskWrapper) || maskWrapper == null)
        {
            return null;
        }

        var sourceMask = maskWrapper.GetMat();
        if (sourceMask.Empty())
        {
            return null;
        }

        Mat maskRegion = sourceMask;
        var disposeMaskRegion = false;
        if (roi.X != 0 || roi.Y != 0 || roi.Width != sourceMask.Width || roi.Height != sourceMask.Height)
        {
            var clipped = roi.Intersect(new Rect(0, 0, sourceMask.Width, sourceMask.Height));
            if (clipped.Width <= 0 || clipped.Height <= 0)
            {
                return new Mat(searchSize, MatType.CV_8UC1, Scalar.Black);
            }

            maskRegion = new Mat(sourceMask, clipped);
            disposeMaskRegion = true;
        }

        try
        {
            using var grayMask = ToGray(maskRegion);
            var resized = new Mat();
            if (grayMask.Size() != searchSize)
            {
                Cv2.Resize(grayMask, resized, searchSize, 0, 0, InterpolationFlags.Nearest);
            }
            else
            {
                grayMask.CopyTo(resized);
            }

            Cv2.Threshold(resized, resized, 1, 255, ThresholdTypes.Binary);
            return resized;
        }
        finally
        {
            if (disposeMaskRegion)
            {
                maskRegion.Dispose();
            }
        }
    }

    private static TemplateMatchModes ResolveMatchMethod(string method)
    {
        return method.Trim().ToLowerInvariant() switch
        {
            "ncc" => TemplateMatchModes.CCoeffNormed,
            "sqdiff" => TemplateMatchModes.SqDiff,
            "sqdiffnormed" => TemplateMatchModes.SqDiffNormed,
            "ccorr" => TemplateMatchModes.CCorr,
            "ccorrnormed" => TemplateMatchModes.CCorrNormed,
            "ccoeff" => TemplateMatchModes.CCoeff,
            _ => TemplateMatchModes.CCoeffNormed
        };
    }

    private PoseSearchOptions ReadPoseSearchOptions(Operator @operator)
    {
        var enabled = GetBoolParam(@operator, "EnablePoseSearch", false);
        var angleStart = GetDoubleParam(@operator, "AngleStart", 0.0, min: -180.0, max: 180.0);
        var angleExtent = GetDoubleParam(@operator, "AngleExtent", 0.0, min: 0.0, max: 360.0);
        var angleStep = GetDoubleParam(@operator, "AngleStep", 1.0, min: 0.1, max: 45.0);
        var scaleMin = GetDoubleParam(@operator, "ScaleMin", 1.0, min: 0.2, max: 3.0);
        var scaleMax = GetDoubleParam(@operator, "ScaleMax", 1.0, min: 0.2, max: 3.0);
        var scaleStep = GetDoubleParam(@operator, "ScaleStep", 0.05, min: 0.01, max: 1.0);
        var pyramidLevels = GetIntParam(@operator, "PyramidLevels", 1, min: 1, max: 4);
        return new PoseSearchOptions(enabled, angleStart, angleExtent, angleStep, scaleMin, scaleMax, scaleStep, pyramidLevels);
    }

    private static List<TemplateMatchCandidate> FindFixedPoseMatches(
        Mat preparedSearch,
        Mat preparedTemplate,
        int maxMatches,
        double threshold,
        TemplateMatchModes matchMethod,
        Mat? searchMask,
        Position origin)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(preparedSearch, preparedTemplate, result, matchMethod);
        return FindMatches(result, preparedTemplate.Size(), maxMatches, threshold, matchMethod, searchMask, null, origin, 0.0, 1.0, 1);
    }

    private static List<TemplateMatchCandidate> FindPoseMatches(
        Mat preparedSearch,
        Mat preparedTemplate,
        int maxMatches,
        double threshold,
        TemplateMatchModes matchMethod,
        Mat? searchMask,
        Position origin,
        PoseSearchOptions options)
    {
        var candidates = new List<TemplateMatchCandidate>();
        var poseCandidates = BuildPoseCandidates(options);
        if (options.PyramidLevels > 1)
        {
            poseCandidates = SelectCoarsePoseCandidates(
                preparedSearch,
                preparedTemplate,
                threshold,
                matchMethod,
                searchMask,
                origin,
                options,
                Math.Max(maxMatches * 64, 64));
        }

        foreach (var pose in poseCandidates)
        {
            using var transformedTemplate = TransformTemplate(preparedTemplate, pose.AngleDeg, pose.Scale, origin, out var transformedOrigin);
            if (transformedTemplate.Image.Empty() ||
                transformedTemplate.Image.Width > preparedSearch.Width ||
                transformedTemplate.Image.Height > preparedSearch.Height ||
                !HasSufficientSignal(transformedTemplate.Image))
            {
                continue;
            }

            using var result = new Mat();
            MatchTemplate(preparedSearch, transformedTemplate.Image, result, matchMethod, transformedTemplate.Mask);
            candidates.AddRange(FindMatches(
                result,
                transformedTemplate.Image.Size(),
                Math.Max(maxMatches, 1),
                threshold,
                matchMethod,
                searchMask,
                transformedTemplate.Mask,
                transformedOrigin,
                pose.AngleDeg,
                pose.Scale,
                options.PyramidLevels));
        }

        return ApplyNms(candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.NormalizedScore)
                .ThenBy(candidate => Math.Abs(candidate.AngleDeg))
                .ThenBy(candidate => Math.Abs(candidate.Scale - 1.0)),
                0.35)
            .Take(maxMatches)
            .ToList();
    }

    private static IReadOnlyList<PoseCandidate> SelectCoarsePoseCandidates(
        Mat preparedSearch,
        Mat preparedTemplate,
        double threshold,
        TemplateMatchModes matchMethod,
        Mat? searchMask,
        Position origin,
        PoseSearchOptions options,
        int maxPoseCandidates)
    {
        var coarseLevel = options.PyramidLevels - 1;
        using var coarseSearch = ResizeForPyramidLevel(preparedSearch, coarseLevel, InterpolationFlags.Area);
        using var coarseTemplateSource = ResizeForPyramidLevel(preparedTemplate, coarseLevel, InterpolationFlags.Area);
        using var coarseMask = searchMask != null && !searchMask.Empty()
            ? ResizeForPyramidLevel(searchMask, coarseLevel, InterpolationFlags.Nearest)
            : null;

        var originScale = 1.0 / (1 << coarseLevel);
        var coarseOrigin = new Position(origin.X * originScale, origin.Y * originScale);
        var coarseThreshold = Math.Max(0.35, threshold - 0.2);
        var allPoseCandidates = BuildPoseCandidates(options);
        var scored = new List<ScoredPoseCandidate>();
        foreach (var pose in allPoseCandidates)
        {
            using var transformedTemplate = TransformTemplate(coarseTemplateSource, pose.AngleDeg, pose.Scale, coarseOrigin, out var transformedOrigin);
            if (transformedTemplate.Image.Empty() ||
                transformedTemplate.Image.Width > coarseSearch.Width ||
                transformedTemplate.Image.Height > coarseSearch.Height ||
                !HasSufficientSignal(transformedTemplate.Image))
            {
                continue;
            }

            using var result = new Mat();
            MatchTemplate(coarseSearch, transformedTemplate.Image, result, matchMethod, transformedTemplate.Mask);
            var matches = FindMatches(
                result,
                transformedTemplate.Image.Size(),
                1,
                coarseThreshold,
                matchMethod,
                coarseMask,
                transformedTemplate.Mask,
                transformedOrigin,
                pose.AngleDeg,
                pose.Scale,
                options.PyramidLevels);
            var best = matches.FirstOrDefault();
            if (best is not null)
            {
                scored.Add(new ScoredPoseCandidate(pose.AngleDeg, pose.Scale, best.Score, best.NormalizedScore));
            }
        }

        var selected = scored
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.NormalizedScore)
            .ThenBy(candidate => Math.Abs(candidate.AngleDeg))
            .ThenBy(candidate => Math.Abs(candidate.Scale - 1.0))
            .Take(maxPoseCandidates)
            .Select(candidate => new PoseCandidate(candidate.AngleDeg, candidate.Scale))
            .Distinct()
            .ToArray();

        if (selected.Length == 0)
        {
            return allPoseCandidates;
        }

        var expanded = ExpandCoarsePoseCandidates(allPoseCandidates, selected, options, coarseLevel);
        return expanded.Count == 0 ? allPoseCandidates : expanded;
    }

    private static IReadOnlyList<PoseCandidate> ExpandCoarsePoseCandidates(
        IReadOnlyList<PoseCandidate> allPoseCandidates,
        IReadOnlyList<PoseCandidate> selected,
        PoseSearchOptions options,
        int coarseLevel)
    {
        var angleRadius = Math.Max(options.AngleStep, options.AngleStep * (1 << (coarseLevel + 1)));
        var scaleRadius = Math.Max(options.ScaleStep, options.ScaleStep * (1 << coarseLevel));
        return allPoseCandidates
            .Where(candidate => selected.Any(seed =>
                Math.Abs(candidate.AngleDeg - seed.AngleDeg) <= angleRadius + 1e-9 &&
                Math.Abs(candidate.Scale - seed.Scale) <= scaleRadius + 1e-9))
            .Distinct()
            .ToArray();
    }

    private static List<TemplateMatchCandidate> FindMatches(
        Mat result,
        Size templateSize,
        int maxMatches,
        double threshold,
        TemplateMatchModes matchMethod,
        Mat? searchMask,
        Mat? templateMask,
        Position referenceOrigin,
        double angleDeg,
        double scale,
        int pyramidLevels)
    {
        using var scoreMap = BuildThresholdScoreMap(result, matchMethod, templateSize);
        using var normalizedScoreMap = BuildNormalizedScoreMap(result, matchMethod, templateSize);
        if (searchMask != null && !searchMask.Empty())
        {
            ApplySearchMask(scoreMap, searchMask, templateSize, templateMask);
            ApplySearchMask(normalizedScoreMap, searchMask, templateSize, templateMask);
        }

        SuppressNonFinite(scoreMap);
        SuppressNonFinite(normalizedScoreMap);

        using var working = scoreMap.Clone();
        var candidates = new List<TemplateMatchCandidate>();
        var candidateBudget = Math.Max(maxMatches * 16, maxMatches);

        for (var index = 0; index < candidateBudget; index++)
        {
            Cv2.MinMaxLoc(working, out _, out var maxVal, out _, out var maxLoc);
            if (maxVal < threshold)
            {
                break;
            }

            candidates.Add(CreateCandidate(
                maxLoc,
                templateSize,
                maxVal,
                normalizedScoreMap.At<float>(maxLoc.Y, maxLoc.X),
                result.At<float>(maxLoc.Y, maxLoc.X),
                normalizedScoreMap,
                referenceOrigin,
                angleDeg,
                scale,
                pyramidLevels));
            SuppressCandidateRegion(working, maxLoc, maxVal, threshold, templateSize);
        }

        return ApplyNms(candidates, 0.35)
            .Take(maxMatches)
            .ToList();
    }

    private static Mat BuildThresholdScoreMap(Mat result, TemplateMatchModes matchMethod, Size templateSize)
    {
        return matchMethod switch
        {
            TemplateMatchModes.SqDiff => BuildSqDiffScoreMap(result, templateSize.Width * templateSize.Height),
            TemplateMatchModes.SqDiffNormed => BuildInvertedNormedMap(result),
            _ => result.Clone()
        };
    }

    private static Mat BuildNormalizedScoreMap(Mat result, TemplateMatchModes matchMethod, Size templateSize)
    {
        return matchMethod switch
        {
            TemplateMatchModes.SqDiff => BuildSqDiffScoreMap(result, templateSize.Width * templateSize.Height),
            TemplateMatchModes.SqDiffNormed => BuildInvertedNormedMap(result),
            TemplateMatchModes.CCoeffNormed => BuildShiftedNormedMap(result),
            TemplateMatchModes.CCorrNormed => ClampToUnitRange(result),
            _ => BuildMinMaxNormalizedMap(result, invert: false)
        };
    }

    private static Mat BuildSqDiffScoreMap(Mat result, int templateArea)
    {
        var safeArea = Math.Max(1, templateArea);
        var maxPossibleResponse = safeArea * 255.0 * 255.0;
        var normalized = new Mat();
        result.ConvertTo(normalized, MatType.CV_32FC1, -1.0 / maxPossibleResponse, 1.0);
        ClampInPlace(normalized);
        return normalized;
    }

    private static Mat BuildMinMaxNormalizedMap(Mat result, bool invert)
    {
        var normalized = new Mat();
        Cv2.Normalize(result, normalized, 0, 1, NormTypes.MinMax, MatType.CV_32FC1);
        if (!invert)
        {
            return normalized;
        }

        var inverted = new Mat();
        Cv2.Subtract(Scalar.All(1.0), normalized, inverted);
        normalized.Dispose();
        return inverted;
    }

    private static Mat BuildInvertedNormedMap(Mat result)
    {
        using var clamped = ClampToUnitRange(result);
        var normalized = new Mat();
        Cv2.Subtract(Scalar.All(1.0), clamped, normalized);
        return normalized;
    }

    private static Mat BuildShiftedNormedMap(Mat result)
    {
        using var shifted = new Mat();
        Cv2.Add(result, Scalar.All(1.0), shifted);
        var normalized = new Mat();
        shifted.ConvertTo(normalized, MatType.CV_32FC1, 0.5);
        ClampInPlace(normalized);
        return normalized;
    }

    private static Mat ClampToUnitRange(Mat result)
    {
        var normalized = result.Clone();
        ClampInPlace(normalized);
        return normalized;
    }

    private static void ClampInPlace(Mat map)
    {
        Cv2.Min(map, Scalar.All(1.0), map);
        Cv2.Max(map, Scalar.All(0.0), map);
    }

    private static void ApplySearchMask(Mat scoreMap, Mat searchMask, Size templateSize, Mat? templateMask)
    {
        if (templateMask != null && !templateMask.Empty())
        {
            using var searchMaskFloat = new Mat();
            using var templateMaskFloat = new Mat();
            searchMask.ConvertTo(searchMaskFloat, MatType.CV_32FC1, 1.0 / 255.0);
            templateMask.ConvertTo(templateMaskFloat, MatType.CV_32FC1, 1.0 / 255.0);
            using var coverage = new Mat();
            Cv2.MatchTemplate(searchMaskFloat, templateMaskFloat, coverage, TemplateMatchModes.CCorr);
            var requiredCoverage = Cv2.CountNonZero(templateMask);
            for (var y = 0; y < scoreMap.Rows; y++)
            {
                for (var x = 0; x < scoreMap.Cols; x++)
                {
                    if (coverage.At<float>(y, x) < requiredCoverage - 0.5)
                    {
                        scoreMap.Set(y, x, 0f);
                    }
                }
            }

            return;
        }

        var maskIndexer = searchMask.GetGenericIndexer<byte>();
        var width = searchMask.Width;
        var height = searchMask.Height;
        var integral = new int[height + 1, width + 1];

        for (var y = 0; y < height; y++)
        {
            var rowSum = 0;
            for (var x = 0; x < width; x++)
            {
                rowSum += maskIndexer[y, x];
                integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
            }
        }

        var required = templateSize.Width * templateSize.Height * 255;
        for (var y = 0; y < scoreMap.Rows; y++)
        {
            for (var x = 0; x < scoreMap.Cols; x++)
            {
                var right = x + templateSize.Width;
                var bottom = y + templateSize.Height;
                var sum = integral[bottom, right] - integral[y, right] - integral[bottom, x] + integral[y, x];
                if (sum < required)
                {
                    scoreMap.Set(y, x, 0f);
                }
            }
        }
    }

    private static void SuppressNonFinite(Mat scoreMap)
    {
        for (var y = 0; y < scoreMap.Rows; y++)
        {
            for (var x = 0; x < scoreMap.Cols; x++)
            {
                var value = scoreMap.At<float>(y, x);
                if (!float.IsFinite(value))
                {
                    scoreMap.Set(y, x, 0f);
                }
            }
        }
    }

    private static TemplateMatchCandidate CreateCandidate(
        Point topLeft,
        Size templateSize,
        double score,
        double normalizedScore,
        double rawResponse,
        Mat normalizedScoreMap,
        Position referenceOrigin,
        double angleDeg,
        double scale,
        int pyramidLevels)
    {
        var bounds = new Rect(topLeft.X, topLeft.Y, templateSize.Width, templateSize.Height);
        var peak = EstimateSubpixelPeak(normalizedScoreMap, topLeft);
        var center = new Position(
            topLeft.X + peak.OffsetX + (templateSize.Width / 2.0),
            topLeft.Y + peak.OffsetY + (templateSize.Height / 2.0));
        return new TemplateMatchCandidate(
            topLeft,
            new Point(bounds.Right, bounds.Bottom),
            center,
            score,
            normalizedScore,
            rawResponse,
            peak.OffsetX,
            peak.OffsetY,
            peak.Curvature,
            referenceOrigin,
            angleDeg,
            scale,
            pyramidLevels,
            templateSize.Width,
            templateSize.Height,
            bounds);
    }

    private static TransformedTemplate TransformTemplate(Mat source, double angleDeg, double scale, Position origin, out Position transformedOrigin)
    {
        if (Math.Abs(angleDeg) < 1e-9 && Math.Abs(scale - 1.0) < 1e-9)
        {
            transformedOrigin = origin;
            return new TransformedTemplate(source.Clone(), null);
        }

        var center = new Point2f(source.Width / 2f, source.Height / 2f);
        using var matrix = Cv2.GetRotationMatrix2D(center, angleDeg, scale);
        var m00 = matrix.Get<double>(0, 0);
        var m01 = matrix.Get<double>(0, 1);
        var m10 = matrix.Get<double>(1, 0);
        var m11 = matrix.Get<double>(1, 1);
        var boundWidth = Math.Max(1, (int)Math.Ceiling((source.Width * Math.Abs(m00)) + (source.Height * Math.Abs(m01))));
        var boundHeight = Math.Max(1, (int)Math.Ceiling((source.Width * Math.Abs(m10)) + (source.Height * Math.Abs(m11))));

        matrix.Set(0, 2, matrix.Get<double>(0, 2) + (boundWidth / 2.0) - center.X);
        matrix.Set(1, 2, matrix.Get<double>(1, 2) + (boundHeight / 2.0) - center.Y);

        transformedOrigin = ApplyAffine(matrix, origin);
        var transformed = new Mat();
        Cv2.WarpAffine(
            source,
            transformed,
            matrix,
            new Size(boundWidth, boundHeight),
            InterpolationFlags.Linear,
            BorderTypes.Constant,
            Scalar.Black);

        using var validSource = new Mat(source.Size(), MatType.CV_8UC1, Scalar.White);
        var validMask = new Mat();
        Cv2.WarpAffine(
            validSource,
            validMask,
            matrix,
            new Size(boundWidth, boundHeight),
            InterpolationFlags.Nearest,
            BorderTypes.Constant,
            Scalar.Black);
        return new TransformedTemplate(transformed, validMask);
    }

    private static void MatchTemplate(Mat image, Mat template, Mat result, TemplateMatchModes matchMethod, Mat? templateMask)
    {
        if (templateMask != null && !templateMask.Empty())
        {
            Cv2.MatchTemplate(image, template, result, matchMethod, templateMask);
            return;
        }

        Cv2.MatchTemplate(image, template, result, matchMethod);
    }

    private static Position ApplyAffine(Mat matrix, Position point)
    {
        var x = (matrix.Get<double>(0, 0) * point.X) + (matrix.Get<double>(0, 1) * point.Y) + matrix.Get<double>(0, 2);
        var y = (matrix.Get<double>(1, 0) * point.X) + (matrix.Get<double>(1, 1) * point.Y) + matrix.Get<double>(1, 2);
        return new Position(x, y);
    }

    private static Mat ResizeForPyramidLevel(Mat source, int level, InterpolationFlags interpolation)
    {
        if (level <= 0)
        {
            return source.Clone();
        }

        var scale = 1.0 / (1 << level);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), 0, 0, interpolation);
        return resized;
    }

    private static IReadOnlyList<PoseCandidate> BuildPoseCandidates(PoseSearchOptions options)
    {
        return BuildScaleRange(options.ScaleMin, options.ScaleMax, options.ScaleStep)
            .SelectMany(scale => BuildAngleRange(options.AngleStart, options.AngleExtent, options.AngleStep)
                .Select(angle => new PoseCandidate(angle, scale)))
            .ToArray();
    }

    private static IReadOnlyList<double> BuildAngleRange(double start, double extent, double step)
    {
        var values = new List<double>();
        var safeStep = Math.Max(0.1, Math.Abs(step));
        var end = start + Math.Max(0.0, extent);
        for (var value = start; value <= end + 1e-9; value += safeStep)
        {
            values.Add(Math.Round(value, 6));
        }

        if (values.Count == 0)
        {
            values.Add(Math.Round(start, 6));
        }

        return values.Distinct().ToArray();
    }

    private static IReadOnlyList<double> BuildScaleRange(double minScale, double maxScale, double step)
    {
        var values = new List<double>();
        var safeMin = Math.Min(minScale, maxScale);
        var safeMax = Math.Max(minScale, maxScale);
        var safeStep = Math.Max(0.01, Math.Abs(step));
        for (var value = safeMin; value <= safeMax + 1e-9; value += safeStep)
        {
            values.Add(Math.Round(value, 6));
        }

        if (values.Count == 0)
        {
            values.Add(Math.Round(safeMin, 6));
        }

        return values.Distinct().ToArray();
    }

    private static SubpixelPeak EstimateSubpixelPeak(Mat scoreMap, Point peak)
    {
        if (peak.X <= 0 || peak.Y <= 0 || peak.X >= scoreMap.Cols - 1 || peak.Y >= scoreMap.Rows - 1)
        {
            return new SubpixelPeak(0, 0, 0);
        }

        var center = scoreMap.At<float>(peak.Y, peak.X);
        var left = scoreMap.At<float>(peak.Y, peak.X - 1);
        var right = scoreMap.At<float>(peak.Y, peak.X + 1);
        var top = scoreMap.At<float>(peak.Y - 1, peak.X);
        var bottom = scoreMap.At<float>(peak.Y + 1, peak.X);

        var offsetX = FitParabolicOffset(left, center, right);
        var offsetY = FitParabolicOffset(top, center, bottom);
        var curvatureX = Math.Max(0, (2 * center) - left - right);
        var curvatureY = Math.Max(0, (2 * center) - top - bottom);
        return new SubpixelPeak(offsetX, offsetY, (curvatureX + curvatureY) / 2.0);
    }

    private static double FitParabolicOffset(double before, double center, double after)
    {
        var denominator = before - (2 * center) + after;
        if (Math.Abs(denominator) < 1e-9)
        {
            return 0;
        }

        return Math.Clamp(0.5 * (before - after) / denominator, -0.5, 0.5);
    }

    private static void SuppressCandidateRegion(Mat working, Point peakLocation, double peakScore, double threshold, Size templateSize)
    {
        var suppressionFloor = Math.Max(threshold, peakScore - Math.Max(0.02, (peakScore - threshold) * 0.25));
        using var highResponseMask = new Mat();
        Cv2.Compare(working, new Scalar(suppressionFloor), highResponseMask, CmpType.GE);

        var paddingX = Math.Max(1, templateSize.Width / 4);
        var paddingY = Math.Max(1, templateSize.Height / 4);
        Rect suppressBounds;
        if (highResponseMask.At<byte>(peakLocation.Y, peakLocation.X) != 0)
        {
            using var floodMask = new Mat(highResponseMask.Rows + 2, highResponseMask.Cols + 2, MatType.CV_8UC1, Scalar.Black);
            Cv2.FloodFill(highResponseMask, floodMask, peakLocation, Scalar.All(128), out var componentBounds);
            suppressBounds = ExpandRect(componentBounds, paddingX, paddingY, working.Width, working.Height);
        }
        else
        {
            suppressBounds = ExpandRect(new Rect(peakLocation.X, peakLocation.Y, 1, 1), paddingX, paddingY, working.Width, working.Height);
        }

        if (suppressBounds.Width <= 0 || suppressBounds.Height <= 0)
        {
            working.Set(peakLocation.Y, peakLocation.X, 0f);
            return;
        }

        using var suppressionRegion = new Mat(working, suppressBounds);
        suppressionRegion.SetTo(0f);
    }

    private static Rect ExpandRect(Rect rect, int paddingX, int paddingY, int maxWidth, int maxHeight)
    {
        var left = Math.Max(0, rect.X - paddingX);
        var top = Math.Max(0, rect.Y - paddingY);
        var right = Math.Min(maxWidth, rect.Right + paddingX);
        var bottom = Math.Min(maxHeight, rect.Bottom + paddingY);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static IEnumerable<TemplateMatchCandidate> ApplyNms(
        IEnumerable<TemplateMatchCandidate> candidates,
        double iouThreshold)
    {
        var selected = new List<TemplateMatchCandidate>();
        foreach (var candidate in candidates)
        {
            if (selected.All(existing => CalculateIoU(existing.Bounds, candidate.Bounds) < iouThreshold))
            {
                selected.Add(candidate);
            }
        }

        return selected;
    }

    private static double CalculateIoU(Rect a, Rect b)
    {
        var intersection = a & b;
        if (intersection.Width <= 0 || intersection.Height <= 0)
        {
            return 0;
        }

        var intersectionArea = intersection.Width * intersection.Height;
        var unionArea = (a.Width * a.Height) + (b.Width * b.Height) - intersectionArea;
        return unionArea <= 0 ? 0 : (double)intersectionArea / unionArea;
    }

    private static string GetCanonicalMethodName(TemplateMatchModes method)
    {
        return method switch
        {
            TemplateMatchModes.SqDiff => "SqDiff",
            TemplateMatchModes.SqDiffNormed => "SqDiffNormed",
            TemplateMatchModes.CCorr => "CCorr",
            TemplateMatchModes.CCorrNormed => "CCorrNormed",
            TemplateMatchModes.CCoeff => "CCoeff",
            _ => "CCoeffNormed"
        };
    }

    private static string GetMethodDescriptor(TemplateMatchModes method, string domain)
    {
        var canonical = GetCanonicalMethodName(method);
        var normalizedDomain = NormalizeDomain(domain);
        return normalizedDomain == "Gray" ? canonical : $"{canonical}:{normalizedDomain}";
    }

    private static string NormalizeDomain(string domain)
    {
        return domain.Trim().ToLowerInvariant() switch
        {
            "edge" => "Edge",
            "gradient" => "Gradient",
            _ => "Gray"
        };
    }

    private static bool HasSufficientSignal(Mat image)
    {
        if (image.Empty())
        {
            return false;
        }

        Cv2.MinMaxLoc(image, out var minValue, out var maxValue, out Point _, out Point _);
        return (maxValue - minValue) >= 1.0;
    }

    private OperatorExecutionOutput CreateNoMatchOutput(
        Mat sourceImage,
        string methodDescriptor,
        Size templateSize,
        string failureReason,
        PoseSearchOptions? poseSearchOptions = null)
    {
        var resultImage = sourceImage.Clone();
        var position = new Position(0, 0);
        var poseSearchEnabled = poseSearchOptions?.Enabled ?? false;
        var output = new Dictionary<string, object>
        {
            ["IsMatch"] = false,
            ["Found"] = false,
            ["Score"] = 0.0,
            ["NormalizedScore"] = 0.0,
            ["RawResponse"] = 0.0,
            ["SubpixelOffsetX"] = 0.0,
            ["SubpixelOffsetY"] = 0.0,
            ["PeakCurvature"] = 0.0,
            ["Angle"] = 0.0,
            ["Scale"] = 1.0,
            ["PoseSearchEnabled"] = poseSearchEnabled,
            ["PyramidLevels"] = poseSearchOptions?.PyramidLevels ?? 1,
            ["Method"] = methodDescriptor,
            ["FailureReason"] = failureReason,
            ["Position"] = position,
            ["X"] = position.X,
            ["Y"] = position.Y,
            ["MatchCount"] = 0,
            ["Matches"] = Array.Empty<object>(),
            ["TemplateWidth"] = templateSize.Width,
            ["TemplateHeight"] = templateSize.Height,
            ["Message"] = failureReason
        };

        if (poseSearchEnabled && poseSearchOptions is not null)
        {
            output["AngleStart"] = poseSearchOptions.AngleStart;
            output["AngleExtent"] = poseSearchOptions.AngleExtent;
            output["AngleStep"] = poseSearchOptions.AngleStep;
            output["ScaleMin"] = poseSearchOptions.ScaleMin;
            output["ScaleMax"] = poseSearchOptions.ScaleMax;
            output["ScaleStep"] = poseSearchOptions.ScaleStep;
        }

        return OperatorExecutionOutput.Success(CreateImageOutput(resultImage, output));
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

    private sealed record TemplateMatchCandidate(
        Point TopLeft,
        Point BottomRight,
        Position Center,
        double Score,
        double NormalizedScore,
        double RawResponse,
        double SubpixelOffsetX,
        double SubpixelOffsetY,
        double PeakCurvature,
        Position ReferenceOrigin,
        double AngleDeg,
        double Scale,
        int PyramidLevels,
        int TemplateWidth,
        int TemplateHeight,
        Rect Bounds)
    {
        public Position GetReferencePosition()
        {
            return new Position(TopLeft.X + SubpixelOffsetX + ReferenceOrigin.X, TopLeft.Y + SubpixelOffsetY + ReferenceOrigin.Y);
        }

        public TemplateMatchCandidate Offset(int offsetX, int offsetY)
        {
            var offsetTopLeft = new Point(TopLeft.X + offsetX, TopLeft.Y + offsetY);
            var offsetBottomRight = new Point(BottomRight.X + offsetX, BottomRight.Y + offsetY);
            var offsetCenter = new Position(Center.X + offsetX, Center.Y + offsetY);
            var offsetBounds = new Rect(Bounds.X + offsetX, Bounds.Y + offsetY, Bounds.Width, Bounds.Height);
            return new TemplateMatchCandidate(offsetTopLeft, offsetBottomRight, offsetCenter, Score, NormalizedScore, RawResponse, SubpixelOffsetX, SubpixelOffsetY, PeakCurvature, ReferenceOrigin, AngleDeg, Scale, PyramidLevels, TemplateWidth, TemplateHeight, offsetBounds);
        }
    }

    private sealed record SubpixelPeak(double OffsetX, double OffsetY, double Curvature);
    private sealed record PoseCandidate(double AngleDeg, double Scale);
    private sealed record ScoredPoseCandidate(double AngleDeg, double Scale, double Score, double NormalizedScore);
    private sealed record TransformedTemplate(Mat Image, Mat? Mask) : IDisposable
    {
        public void Dispose()
        {
            Image.Dispose();
            Mask?.Dispose();
        }
    }

    private sealed record PoseSearchOptions(
        bool Enabled,
        double AngleStart,
        double AngleExtent,
        double AngleStep,
        double ScaleMin,
        double ScaleMax,
        double ScaleStep,
        int PyramidLevels);
}

