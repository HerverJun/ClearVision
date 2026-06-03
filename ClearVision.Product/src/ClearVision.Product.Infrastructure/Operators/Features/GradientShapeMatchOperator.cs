using System.Security.Cryptography;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ValueObjects;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "梯度形状匹配",
    Description = "基于梯度方向特征的形状匹配，支持可选 ROI 搜索。",
    Category = "匹配定位",
    IconName = "shape-match",
    Version = "1.1.0"
)]
[InputPort("Image", "搜索图像", PortDataType.Image, IsRequired = true)]
[InputPort("Template", "模板图像", PortDataType.Image, IsRequired = false)]
[OutputPort("Image", "结果图像", PortDataType.Image)]
[OutputPort("Position", "匹配位置", PortDataType.Point)]
[OutputPort("Angle", "旋转角度", PortDataType.Float)]
[OutputPort("IsMatch", "是否匹配", PortDataType.Boolean)]
[OutputPort("Score", "匹配分数", PortDataType.Float)]
[OutputPort("Matches", "匹配列表", PortDataType.Any)]
[OperatorParam("TemplatePath", "模板路径", "file", DefaultValue = "")]
[OperatorParam("MinScore", "最小分数(%)", "double", DefaultValue = 80.0, Min = 0.0, Max = 100.0)]
[OperatorParam("TopK", "返回候选数", "int", DefaultValue = 1, Min = 1, Max = 10)]
[OperatorParam("AngleRange", "角度范围(度)", "int", DefaultValue = 180, Min = 0, Max = 180)]
[OperatorParam("AngleStep", "角度步长", "int", DefaultValue = 1, Min = 1, Max = 10)]
[OperatorParam("MagnitudeThreshold", "梯度阈值", "int", DefaultValue = 30, Min = 0, Max = 255)]
[OperatorParam("EnableCache", "启用缓存", "bool", DefaultValue = true)]
[OperatorParam("UseRoi", "使用 ROI", "bool", DefaultValue = false)]
[OperatorParam("RoiX", "ROI X", "int", DefaultValue = 0, Min = 0, Max = 100000)]
[OperatorParam("RoiY", "ROI Y", "int", DefaultValue = 0, Min = 0, Max = 100000)]
[OperatorParam("RoiWidth", "ROI Width", "int", DefaultValue = 0, Min = 0, Max = 100000)]
[OperatorParam("RoiHeight", "ROI Height", "int", DefaultValue = 0, Min = 0, Max = 100000)]
[AlgorithmInfo(
    Name = "Gradient Direction Template Match",
    CoreApi = "Custom GradientShapeMatcher (OpenCvSharp.Mat gradient computation, 8-bin direction quantization, coarse-to-fine peak search with per-template NMS)",
    ImplementationStrategy = "Train a bank of rotated gradient templates by quantizing edge directions into 8 bins. Match scene positions by directional agreement ratio. Supports TopK multi-match output with position-based NMS, optional ROI search, and SHA256-based template cache with LRU eviction.",
    TimeComplexity = "O(T * R * S) where T is template feature count, R is rotated template count, and S is scene pixels under search",
    TypicalLatency = "GradientShapeMatchGoldenRunner baseline: 130 cases passed, avg runtime about 92 ms on 512x384 synthetic images.",
    SpaceComplexity = "O(R * T) for rotated template storage plus bounded LRU cache (max 8 entries)",
    SuitableUseCases = new[]
    {
        "Edge-defined object localization under moderate lighting changes.",
        "Rotation-invariant matching when target has clear gradient structure and limited symmetry.",
        "Multi-instance detection with TopK output and position NMS."
    },
    UnsuitableUseCases = new[]
    {
        "Low-texture or blank templates that yield fewer than 10 gradient features.",
        "Scenes with heavy scale variation (fixed-scale template matching only).",
        "Sub-pixel precision measurement workflows."
    },
    KnownLimitations = new[]
    {
        "Score is a directional agreement ratio (matching features / total template features) x 100, not a correlation coefficient.",
        "Template cache is bounded to 8 entries with LRU eviction.",
        "Low-feature templates (< 10 valid gradient features) fail with InvalidTemplate."
    },
    Dependencies = new[] { "OpenCvSharp" }
)]
public class GradientShapeMatchOperator : OperatorBase
{
    private const int MaxMatcherCacheEntries = 8;
    private readonly Dictionary<string, MatcherCacheEntry> _matcherCache = new();
    private readonly LinkedList<string> _matcherCacheLru = new();
    private readonly object _cacheLock = new();

    public override OperatorType OperatorType => OperatorType.GradientShapeMatch;

    public GradientShapeMatchOperator(ILogger<GradientShapeMatchOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供输入图像"));
        }

        var templatePath = GetStringParam(@operator, "TemplatePath", "");
        var minScore = GetDoubleParam(@operator, "MinScore", 80.0, min: 0.0, max: 100.0);
        var angleRange = GetIntParam(@operator, "AngleRange", 180, min: 0, max: 180);
        var angleStep = GetIntParam(@operator, "AngleStep", 1, min: 1, max: 10);
        var magnitudeThreshold = GetIntParam(@operator, "MagnitudeThreshold", 30, min: 0, max: 255);
        var topK = GetIntParam(@operator, "TopK", 1, min: 1, max: 10);
        var enableCache = GetBoolParam(@operator, "EnableCache", true);
        var useRoi = GetBoolParam(@operator, "UseRoi", false);
        var roiX = GetIntParam(@operator, "RoiX", 0, min: 0);
        var roiY = GetIntParam(@operator, "RoiY", 0, min: 0);
        var roiWidth = GetIntParam(@operator, "RoiWidth", 0, min: 0);
        var roiHeight = GetIntParam(@operator, "RoiHeight", 0, min: 0);

        Mat? templateFromInput = null;
        if (TryGetInputImage(inputs, "Template", out var templateWrapper) && templateWrapper != null)
        {
            templateFromInput = templateWrapper.GetMat();
        }

        var srcImage = imageWrapper.GetMat();
        var searchRegion = BuildSearchRegion(useRoi, roiX, roiY, roiWidth, roiHeight, srcImage.Width, srcImage.Height);

        try
        {
            var cacheKey = BuildCacheKey(templatePath, templateFromInput, angleRange, angleStep, magnitudeThreshold);
            var lease = GetOrCreateMatcher(cacheKey, enableCache, templatePath, templateFromInput, angleRange, angleStep, magnitudeThreshold);
            if (lease == null)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("未提供模板图像或路径"));
            }

            try
            {
                var results = topK > 1
                    ? lease.Entry.Matcher.MatchTopK(srcImage, minScore, searchRegion, topK)
                    : new List<ShapeMatchResult> { lease.Entry.Matcher.Match(srcImage, minScore, searchRegion) };

                var bestResult = results.FirstOrDefault();
                var isMatch = bestResult.IsValid;
                var resultImage = srcImage.Clone();
                var boxColor = isMatch ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);

                foreach (var r in results)
                {
                    if (!r.IsValid)
                        continue;
                    var halfWidth = Math.Max(1, lease.Entry.TemplateWidth / 2);
                    var halfHeight = Math.Max(1, lease.Entry.TemplateHeight / 2);
                    Cv2.Rectangle(
                        resultImage,
                        new Point(r.Position.X - halfWidth, r.Position.Y - halfHeight),
                        new Point(r.Position.X + halfWidth, r.Position.Y + halfHeight),
                        boxColor,
                        2);
                    Cv2.DrawMarker(resultImage, r.Position, boxColor, MarkerTypes.Cross, 20, 2);
                    Cv2.PutText(
                        resultImage,
                        $"{r.Score:F1}% A={r.Angle:F0}",
                        new Point(r.Position.X - halfWidth, Math.Max(16, r.Position.Y - halfHeight - 5)),
                        HersheyFonts.HersheySimplex,
                        0.5,
                        boxColor,
                        1);
                }

                Cv2.PutText(resultImage, $"{(isMatch ? "OK" : "NG")}: Count={results.Count(r => r.IsValid)}", new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, boxColor, 2);

                var position = isMatch ? new Position(bestResult.Position.X, bestResult.Position.Y) : new Position(0, 0);
                var output = new Dictionary<string, object>
                {
                    ["IsMatch"] = isMatch,
                    ["Score"] = bestResult.Score,
                    ["Position"] = position,
                    ["X"] = position.X,
                    ["Y"] = position.Y,
                    ["Angle"] = bestResult.Angle,
                    ["TemplateWidth"] = lease.Entry.TemplateWidth,
                    ["TemplateHeight"] = lease.Entry.TemplateHeight,
                    ["DisplayWidth"] = lease.Entry.TemplateWidth,
                    ["DisplayHeight"] = lease.Entry.TemplateHeight,
                    ["CacheEnabled"] = enableCache,
                    ["TopK"] = topK,
                    ["MatchCount"] = results.Count(r => r.IsValid),
                    ["Matches"] = results.Where(r => r.IsValid).Select(r => new Dictionary<string, object>
                    {
                        ["Position"] = new Position(r.Position.X, r.Position.Y),
                        ["X"] = r.Position.X,
                        ["Y"] = r.Position.Y,
                        ["Angle"] = r.Angle,
                        ["Score"] = r.Score
                    }).ToList(),
                    ["SearchRegion"] = new Dictionary<string, object>
                    {
                        ["Enabled"] = useRoi,
                        ["X"] = searchRegion.X,
                        ["Y"] = searchRegion.Y,
                        ["Width"] = searchRegion.Width,
                        ["Height"] = searchRegion.Height
                    }
                };

                if (!isMatch)
                {
                    output["Message"] = "No gradient shape match above threshold.";
                }

                return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(resultImage, output)));
            }
            finally
            {
                if (!lease.FromCache)
                {
                    lease.Entry.Matcher.Dispose();
                }
            }
        }
        catch (GradientShapeMatchException ex) when (ex.FailureReason == "InvalidTemplate")
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(ex.Message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"梯度形状匹配失败: {ex.Message}"));
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var minScore = GetDoubleParam(@operator, "MinScore", 80.0);
        if (minScore < 0 || minScore > 100)
        {
            return ValidationResult.Invalid("最小分数必须在 0-100 之间");
        }

        var angleRange = GetIntParam(@operator, "AngleRange", 180);
        if (angleRange < 0 || angleRange > 180)
        {
            return ValidationResult.Invalid("角度范围必须在 0-180 之间");
        }

        if (GetBoolParam(@operator, "UseRoi", false))
        {
            var roiWidth = GetIntParam(@operator, "RoiWidth", 0);
            var roiHeight = GetIntParam(@operator, "RoiHeight", 0);
            if (roiWidth <= 0 || roiHeight <= 0)
            {
                return ValidationResult.Invalid("启用 ROI 时，RoiWidth 和 RoiHeight 必须大于 0");
            }
        }

        var topK = GetIntParam(@operator, "TopK", 1);
        if (topK < 1 || topK > 10)
        {
            return ValidationResult.Invalid("TopK 必须在 1-10 之间");
        }

        return ValidationResult.Valid();
    }

    private MatcherLease? GetOrCreateMatcher(
        string cacheKey,
        bool enableCache,
        string templatePath,
        Mat? templateFromInput,
        int angleRange,
        int angleStep,
        int magnitudeThreshold)
    {
        if (enableCache && TryGetCachedMatcher(cacheKey, out var cached))
        {
            return new MatcherLease(cached!, true);
        }

        Mat? templateImage = null;
        var shouldDispose = false;
        GradientShapeMatcher? matcher = null;
        try
        {
            if (templateFromInput != null)
            {
                templateImage = templateFromInput;
            }
            else if (!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))
            {
                templateImage = Cv2.ImRead(templatePath, ImreadModes.Color);
                shouldDispose = true;
            }

            if (templateImage == null || templateImage.Empty())
            {
                return null;
            }

            matcher = new GradientShapeMatcher(magnitudeThreshold, angleStep);
            matcher.Train(templateImage, angleRange);

            var entry = new MatcherCacheEntry(matcher, templateImage.Width, templateImage.Height);
            if (enableCache)
            {
                AddOrUpdateCache(cacheKey, entry);
                return new MatcherLease(entry, true);
            }

            return new MatcherLease(entry, false);
        }
        catch
        {
            matcher?.Dispose();
            throw;
        }
        finally
        {
            if (shouldDispose)
            {
                templateImage?.Dispose();
            }
        }
    }

    private static Rect BuildSearchRegion(bool useRoi, int roiX, int roiY, int roiWidth, int roiHeight, int imageWidth, int imageHeight)
    {
        if (!useRoi)
        {
            return new Rect(0, 0, imageWidth, imageHeight);
        }

        var x = Math.Clamp(roiX, 0, imageWidth);
        var y = Math.Clamp(roiY, 0, imageHeight);
        var width = Math.Clamp(roiWidth, 0, imageWidth - x);
        var height = Math.Clamp(roiHeight, 0, imageHeight - y);
        return new Rect(x, y, width, height);
    }

    private static string BuildCacheKey(string templatePath, Mat? templateFromInput, int angleRange, int angleStep, int magnitudeThreshold)
    {
        if (templateFromInput != null && !templateFromInput.Empty())
        {
            var encoded = templateFromInput.ToBytes(".png");
            var hash = Convert.ToHexString(SHA256.HashData(encoded));
            return $"input:{hash}:{angleRange}:{angleStep}:{magnitudeThreshold}";
        }

        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            return $"path:{templatePath}:missing:{angleRange}:{angleStep}:{magnitudeThreshold}";
        }

        var fullPath = Path.GetFullPath(templatePath);
        var info = new FileInfo(fullPath);
        using var stream = File.OpenRead(fullPath);
        var fileHash = Convert.ToHexString(SHA256.HashData(stream));
        return $"path:{fullPath}:{info.Length}:{info.LastWriteTimeUtc.Ticks}:{fileHash}:{angleRange}:{angleStep}:{magnitudeThreshold}";
    }

    private bool TryGetCachedMatcher(string cacheKey, out MatcherCacheEntry? entry)
    {
        lock (_cacheLock)
        {
            if (_matcherCache.TryGetValue(cacheKey, out var cached))
            {
                TouchCacheKey(cacheKey);
                entry = cached;
                return true;
            }
        }

        entry = null;
        return false;
    }

    private void AddOrUpdateCache(string cacheKey, MatcherCacheEntry entry)
    {
        lock (_cacheLock)
        {
            if (_matcherCache.TryGetValue(cacheKey, out var existing))
            {
                existing.Matcher.Dispose();
                _matcherCache[cacheKey] = entry;
                TouchCacheKey(cacheKey);
                return;
            }

            _matcherCache[cacheKey] = entry;
            _matcherCacheLru.AddFirst(cacheKey);

            while (_matcherCache.Count > MaxMatcherCacheEntries && _matcherCacheLru.Last != null)
            {
                var evictKey = _matcherCacheLru.Last.Value;
                _matcherCacheLru.RemoveLast();
                if (_matcherCache.Remove(evictKey, out var evicted))
                {
                    evicted.Matcher.Dispose();
                }
            }
        }
    }

    private void TouchCacheKey(string cacheKey)
    {
        var node = _matcherCacheLru.Find(cacheKey);
        if (node == null)
        {
            _matcherCacheLru.AddFirst(cacheKey);
            return;
        }

        if (node != _matcherCacheLru.First)
        {
            _matcherCacheLru.Remove(node);
            _matcherCacheLru.AddFirst(node);
        }
    }

    private sealed record MatcherCacheEntry(GradientShapeMatcher Matcher, int TemplateWidth, int TemplateHeight);

    private sealed record MatcherLease(MatcherCacheEntry Entry, bool FromCache);
}
