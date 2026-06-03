// FeatureMatchOperatorBase.cs
// Shared feature matching infrastructure.
// Author: ClearVision Team
using System.Security.Cryptography;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// Shared feature matching base for AKAZE/ORB style operators.
/// </summary>
public abstract class FeatureMatchOperatorBase : OperatorBase
{
    private const int TemplateCacheCapacity = 16;
    protected static readonly Dictionary<string, TemplateCacheEntry> TemplateCacheStore = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> TemplateCacheOrder = new();
    private static readonly object TemplateCacheLock = new();

    protected FeatureMatchOperatorBase(ILogger logger) : base(logger)
    {
    }

    protected sealed record FeatureMatchCandidateProfile(string Name, bool Enabled, bool Applied = false);

    protected FeatureMatchCandidateProfile ResolveFeatureMatchCandidateProfile(Operator @operator)
    {
        return new FeatureMatchCandidateProfile(
            NormalizeFeatureMatchCandidateProfile(GetStringParam(@operator, "CandidateProfile", "default")),
            GetBoolParam(@operator, "EnableCandidateProfile", false));
    }

    protected ValidationResult ValidateFeatureMatchCandidateProfile(Operator @operator, params string[] supportedProfiles)
    {
        var profile = ResolveFeatureMatchCandidateProfile(@operator);
        var supported = supportedProfiles
            .Append("default")
            .Select(NormalizeFeatureMatchCandidateProfile)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return supported.Contains(profile.Name)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid($"CandidateProfile must be one of: {string.Join(", ", supported.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}.");
    }

    protected static void AddFeatureMatchCandidateProfileOutputs(
        Dictionary<string, object> data,
        FeatureMatchCandidateProfile profile)
    {
        data["CandidateProfileEnabled"] = profile.Enabled;
        data["CandidateProfile"] = profile.Name;
        data["CandidateProfileApplied"] = profile.Applied;
    }

    private static string NormalizeFeatureMatchCandidateProfile(string raw)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? "default"
            : raw.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Performs a symmetry-checked descriptor match.
    /// </summary>
    protected List<DMatch> MatchWithSymmetryTest(Mat templateDesc, Mat sceneDesc, double matchRatio = 0.75)
    {
        using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: false);

        var forwardMatches = matcher.KnnMatch(templateDesc, sceneDesc, k: 2);
        var backwardMatches = matcher.KnnMatch(sceneDesc, templateDesc, k: 2);

        var backwardBest = new Dictionary<int, int>();
        foreach (var match in backwardMatches)
        {
            if (match.Length >= 2 && match[0].Distance < matchRatio * match[1].Distance)
            {
                backwardBest[match[0].QueryIdx] = match[0].TrainIdx;
            }
            else if (match.Length == 1)
            {
                backwardBest[match[0].QueryIdx] = match[0].TrainIdx;
            }
        }

        var goodMatches = new List<DMatch>();
        foreach (var match in forwardMatches)
        {
            if (match.Length < 2)
                continue;
            if (match[0].Distance >= matchRatio * match[1].Distance)
                continue;

            if (backwardBest.TryGetValue(match[0].TrainIdx, out var reverseTemplateIdx) &&
                reverseTemplateIdx == match[0].QueryIdx)
            {
                goodMatches.Add(match[0]);
            }
        }

        return goodMatches;
    }
    /// <summary>
    /// Computes a homography from accepted feature matches.
    /// </summary>
    protected (Mat? Homography, int Inliers) ComputeHomography(
        KeyPoint[] templateKeyPoints,
        KeyPoint[] sceneKeyPoints,
        List<DMatch> goodMatches)
    {
        if (goodMatches.Count < 4)
            return (null, 0);

        var srcPts = goodMatches.Select(m => templateKeyPoints[m.QueryIdx].Pt).ToArray();
        var dstPts = goodMatches.Select(m => sceneKeyPoints[m.TrainIdx].Pt).ToArray();

        using var mask = new Mat();
        var h = Cv2.FindHomography(
            InputArray.Create(srcPts),
            InputArray.Create(dstPts),
            HomographyMethods.Ransac,
            5.0,
            mask);

        var inliers = mask.Empty() ? 0 : Cv2.CountNonZero(mask);
        return (h, inliers);
    }

    private protected (Mat? Homography, Point2f[] Corners, HomographyVerificationHelper.HomographyVerificationMetrics Metrics) EstimateAndVerifyHomography(
        KeyPoint[] templateKeyPoints,
        KeyPoint[] sceneKeyPoints,
        IReadOnlyList<DMatch> goodMatches,
        Size templateSize,
        Size searchImageSize,
        double ransacThreshold,
        int minMatchCount,
        int minInliers,
        double minInlierRatio,
        bool allowCenterOnlyProjection = false)
    {
        if (goodMatches.Count < 4)
        {
            return (null, Array.Empty<Point2f>(), new HomographyVerificationHelper.HomographyVerificationMetrics(
                VerificationPassed: false,
                MatchCount: goodMatches.Count,
                InlierCount: 0,
                InlierRatio: 0,
                MeanReprojectionError: double.PositiveInfinity,
                MaxReprojectionError: double.PositiveInfinity,
                AreaRatio: 0,
                CornersValid: false,
                CornersInsideCount: 0,
                ProjectedCenterInside: false,
                FailureReason: "At least four point correspondences are required."));
        }

        var srcPts = goodMatches.Select(match => templateKeyPoints[match.QueryIdx].Pt).ToArray();
        var dstPts = goodMatches.Select(match => sceneKeyPoints[match.TrainIdx].Pt).ToArray();
        var success = HomographyVerificationHelper.TryEstimateAndVerify(
            srcPts,
            dstPts,
            templateSize,
            searchImageSize,
            ransacThreshold,
            minMatchCount,
            minInliers,
            minInlierRatio,
            allowCenterOnlyProjection,
            out var homography,
            out var corners,
            out var metrics);
        if (!success)
        {
            homography?.Dispose();
            return (null, Array.Empty<Point2f>(), metrics);
        }

        return (homography, corners, metrics);
    }

    /// <summary>
    /// Draws the perspective-transformed template box.
    /// </summary>
    protected void DrawPerspectiveBox(Mat image, Mat homography, int templateWidth, int templateHeight, Scalar color)
    {
        var corners = new[]
        {
            new Point2f(0, 0),
            new Point2f(templateWidth, 0),
            new Point2f(templateWidth, templateHeight),
            new Point2f(0, templateHeight)
        };

        var projected = Cv2.PerspectiveTransform(corners, homography);
        var points = projected.Select(p => new Point((int)p.X, (int)p.Y)).ToArray();

        var areaRatio = Math.Abs(Cv2.ContourArea(points)) / (double)(templateWidth * templateHeight);
        if (Cv2.IsContourConvex(points) && areaRatio > 0.1 && areaRatio < 4.0)
        {
            for (var i = 0; i < 4; i++)
                Cv2.Line(image, points[i], points[(i + 1) % 4], color, 3);
        }
    }

    /// <summary>
    /// Gets or loads a template feature cache entry.
    /// Gets or loads a template feature cache entry.
    protected (Mat Template, KeyPoint[] KeyPoints, Mat Descriptors)? GetOrLoadTemplate(
        string templatePath,
        string cacheDiscriminator,
        Func<Mat, (KeyPoint[] KeyPoints, Mat Descriptors)> detector)
    {
        if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            return null;

        var fingerprint = ComputeFileFingerprint(templatePath);
        var cacheKey = $"{templatePath}|{cacheDiscriminator}|{fingerprint}";

        if (TryGetCachedTemplate(cacheKey, out var cached))
        {
            return cached;
        }

        // Load template image.
        using var template = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (template.Empty())
            return null;

        // Convert to grayscale before feature extraction.
        using var gray = new Mat();
        if (template.Channels() > 1)
            Cv2.CvtColor(template, gray, ColorConversionCodes.BGR2GRAY);
        else
            template.CopyTo(gray);

        var (keyPoints, descriptors) = detector(gray);

        if (keyPoints.Length == 0 || descriptors.Empty())
        {
            descriptors.Dispose();
            return null;
        }

        var entry = new TemplateCacheEntry
        {
            Template = template.Clone(),
            KeyPoints = keyPoints.ToArray(),
            Descriptors = descriptors.Clone()
        };
        descriptors.Dispose();

        AddTemplateCacheEntry(cacheKey, entry);
        return CloneTemplateEntry(entry);
    }

    /// <summary>
    /// Filters keypoints and descriptors down to the configured maximum.
    /// </summary>
    protected (KeyPoint[] FilteredKeyPoints, Mat FilteredDescriptors) FilterFeatures(
        KeyPoint[] keyPoints, Mat descriptors, int maxFeatures)
    {
        if (keyPoints.Length <= maxFeatures)
            return (keyPoints, descriptors.Clone());

        var indices = Enumerable.Range(0, keyPoints.Length)
            .OrderByDescending(i => keyPoints[i].Response)
            .Take(maxFeatures)
            .ToArray();

        var filteredKpts = new KeyPoint[maxFeatures];
        var filteredDesc = new Mat(maxFeatures, descriptors.Cols, descriptors.Type());

        for (var i = 0; i < maxFeatures; i++)
        {
            var originalIdx = indices[i];
            filteredKpts[i] = keyPoints[originalIdx];
            using var srcRow = descriptors.Row(originalIdx);
            using var dstRow = filteredDesc.Row(i);
            srcRow.CopyTo(dstRow);
        }

        return (filteredKpts, filteredDesc);
    }

    /// <summary>
    /// Cached template feature data.
    /// Cached template feature data.
    protected class TemplateCacheEntry
    {
        public required Mat Template { get; set; }
        public required KeyPoint[] KeyPoints { get; set; }
        public required Mat Descriptors { get; set; }
        public LinkedListNode<string>? OrderNode { get; set; }
    }

    private static bool TryGetCachedTemplate(string cacheKey, out (Mat Template, KeyPoint[] KeyPoints, Mat Descriptors) cached)
    {
        lock (TemplateCacheLock)
        {
            if (TemplateCacheStore.TryGetValue(cacheKey, out var entry))
            {
                TouchTemplateCacheEntry(cacheKey, entry);
                cached = CloneTemplateEntry(entry);
                return true;
            }
        }

        cached = default;
        return false;
    }

    private static void AddTemplateCacheEntry(string cacheKey, TemplateCacheEntry entry)
    {
        lock (TemplateCacheLock)
        {
            if (TemplateCacheStore.TryGetValue(cacheKey, out var existing))
            {
                entry.Template.Dispose();
                entry.Descriptors.Dispose();
                TouchTemplateCacheEntry(cacheKey, existing);
                return;
            }

            while (TemplateCacheStore.Count >= TemplateCacheCapacity)
            {
                var oldestKey = TemplateCacheOrder.First?.Value;
                if (oldestKey == null)
                {
                    break;
                }

                if (TemplateCacheStore.Remove(oldestKey, out var evicted))
                {
                    TemplateCacheOrder.RemoveFirst();
                    evicted.Template.Dispose();
                    evicted.Descriptors.Dispose();
                }
            }

            entry.OrderNode = TemplateCacheOrder.AddLast(cacheKey);
            TemplateCacheStore[cacheKey] = entry;
        }
    }

    private static void TouchTemplateCacheEntry(string cacheKey, TemplateCacheEntry entry)
    {
        if (entry.OrderNode != null)
        {
            TemplateCacheOrder.Remove(entry.OrderNode);
        }

        entry.OrderNode = TemplateCacheOrder.AddLast(cacheKey);
    }

    private static (Mat Template, KeyPoint[] KeyPoints, Mat Descriptors) CloneTemplateEntry(TemplateCacheEntry entry)
    {
        return (entry.Template.Clone(), entry.KeyPoints.ToArray(), entry.Descriptors.Clone());
    }

    private static string ComputeFileFingerprint(string templatePath)
    {
        using var stream = File.OpenRead(templatePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
