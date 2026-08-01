using System.Numerics;
using ClearVision.Product.Infrastructure.PointCloud;
using ClearVision.Product.Infrastructure.PointCloud.Matching;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using OpenCvSharp;
using Xunit;
using MatPool = ClearVision.Product.Infrastructure.Memory.MatPool;
using PointCloudModel = ClearVision.Product.Infrastructure.PointCloud.PointCloud;

namespace ClearVision.Product.Tests.PointCloud;

[TestClassification(TestDomain.PointCloud, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Long, TestFlakyPolicy.Blocking, "operator-quality", Suites = "PPFRegression")]
[Collection(PointCloudMatchingTestCollections.PointCloudMatching)]
public sealed class PPFMatcherRegressionTests
{
    [Fact]
    public void Match_ModelToScene_WithOcclusionAndOutliers_ShouldRecoverPose()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 201);

        using var model = BuildAsymmetricModel(gen);

        // Ground truth pose: model -> scene
        var rot = Matrix4x4.CreateFromYawPitchRoll(0.55f, -0.25f, 0.35f);
        var gt = rot * Matrix4x4.CreateTranslation(0.12f, -0.08f, 0.05f);

        using var sceneFull = model.Transform(gt);

        // Occlusion: keep ~70% points by simple AABB crop on +X side (in scene space).
        var aabb = sceneFull.GetAABB();
        var crop = new AxisAlignedBoundingBox
        {
            Min = new Vector3(aabb.Min.X + (aabb.Extent.X * 0.15f), aabb.Min.Y, aabb.Min.Z),
            Max = aabb.Max
        };
        using var sceneCropped = sceneFull.Crop(crop);

        // Add sparse outliers far away.
        var farBounds = new AxisAlignedBoundingBox
        {
            Min = new Vector3(-2, -2, -2),
            Max = new Vector3(2, 2, 2)
        };
        var sceneWithOutliers = gen.AddOutliers(sceneCropped, outlierRatio: 0.1f, bounds: farBounds);
        using var scene = sceneWithOutliers;

        var matcher = new PPFMatcher(seed: 123);
        var result = matcher.Match(
            model,
            scene,
            normalRadius: 0.06f,
            featureRadius: 0.12f,
            distanceStep: 0.01f,
            angleStepRad: 5f * (MathF.PI / 180f),
            numSamples: 200,
            modelRefStride: 2,
            maxPairsPerKey: 64,
            maxCorrespondences: 5000,
            ransacIterations: 1200,
            inlierThreshold: 0.01f,
            minInliers: 120);

        result.IsMatched.Should().BeTrue(
            $"ambiguous={result.IsAmbiguous}, ambiguityScore={result.AmbiguityScore:F3}, stability={result.StabilityScore:F3}, normal={result.NormalConsistency:F3}, inliers={result.InlierCount}, rms={result.RmsError:F4}");
        result.IsAmbiguous.Should().BeFalse();
        result.InlierCount.Should().BeGreaterThanOrEqualTo(120);
        result.RmsError.Should().BeLessThan(0.02); // 20mm RMS on inliers
        result.NormalConsistency.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
        result.StabilityScore.Should().BeGreaterThan(0.15);
        var (translationError, rotationErrorDeg) = ComputePoseErrors(gt, result.TransformModelToScene);
        translationError.Should().BeLessThan(0.03);
        rotationErrorDeg.Should().BeLessThan(8.0);
    }

    [Fact]
    public void Match_AsymmetricModelAcrossSeeds_ShouldRemainPoseStable()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 251);
        using var model = BuildAsymmetricModel(gen);

        var gt = Matrix4x4.CreateFromYawPitchRoll(0.45f, -0.22f, 0.28f) * Matrix4x4.CreateTranslation(0.10f, -0.05f, 0.04f);
        using var scene = model.Transform(gt);

        var translationErrors = new List<double>();
        var rotationErrors = new List<double>();

        foreach (var seed in Enumerable.Range(700, 8))
        {
            var matcher = new PPFMatcher(seed: seed);
            var result = matcher.Match(
                model,
                scene,
                normalRadius: 0.06f,
                featureRadius: 0.12f,
                distanceStep: 0.01f,
                angleStepRad: 5f * (MathF.PI / 180f),
                numSamples: 220,
                modelRefStride: 2,
                maxPairsPerKey: 64,
                maxCorrespondences: 5000,
                ransacIterations: 1200,
                inlierThreshold: 0.01f,
                minInliers: 120);

            result.IsMatched.Should().BeTrue($"seed {seed} should remain stable");
            result.IsAmbiguous.Should().BeFalse();
            result.NormalConsistency.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
            result.StabilityScore.Should().BeGreaterThan(0.15);
            var (translationError, rotationErrorDeg) = ComputePoseErrors(gt, result.TransformModelToScene);
            translationErrors.Add(translationError);
            rotationErrors.Add(rotationErrorDeg);
        }

        Percentile95(translationErrors).Should().BeLessThan(0.03);
        Percentile95(rotationErrors).Should().BeLessThan(8.0);
    }

    [Fact]
    public void Match_AsymmetricModelWithGloballyFlippedSceneNormals_ShouldRecoverPose()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 261);
        using var model = BuildAsymmetricModel(gen, includeNormals: true);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.48f, -0.21f, 0.33f) * Matrix4x4.CreateTranslation(0.11f, -0.06f, 0.045f);
        using var scene = model.Transform(gt);
        using var flippedScene = FlipNormals(scene);

        var matcher = new PPFMatcher(seed: 721);
        var result = matcher.Match(
            model,
            flippedScene,
            normalRadius: 0.06f,
            featureRadius: 0.12f,
            distanceStep: 0.01f,
            angleStepRad: 5f * (MathF.PI / 180f),
            numSamples: 220,
            modelRefStride: 2,
            maxPairsPerKey: 64,
            maxCorrespondences: 5000,
            ransacIterations: 1200,
            inlierThreshold: 0.01f,
            minInliers: 120);

        result.IsMatched.Should().BeTrue(
            $"ambiguous={result.IsAmbiguous}, ambiguityScore={result.AmbiguityScore:F3}, stability={result.StabilityScore:F3}, normal={result.NormalConsistency:F3}, inliers={result.InlierCount}, rms={result.RmsError:F4}");
        result.IsAmbiguous.Should().BeFalse();
        result.NormalConsistency.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
        var (translationError, rotationErrorDeg) = ComputePoseErrors(gt, result.TransformModelToScene);
        translationError.Should().BeLessThan(0.03);
        rotationErrorDeg.Should().BeLessThan(8.0);
    }

    [Fact]
    public void Match_SymmetricSphere_ShouldReportAmbiguity()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 281);
        using var model = gen.GenerateSphere(Vector3.Zero, radius: 0.20f, numPoints: 2600, noise: 0.0004f, includeColors: false, includeNormals: true);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.52f, -0.18f, 0.41f) * Matrix4x4.CreateTranslation(0.06f, -0.04f, 0.03f);
        using var scene = model.Transform(gt);

        var matcher = new PPFMatcher(seed: 333);
        var result = matcher.Match(
            model,
            scene,
            normalRadius: 0.05f,
            featureRadius: 0.10f,
            distanceStep: 0.01f,
            angleStepRad: 5f * (MathF.PI / 180f),
            numSamples: 200,
            modelRefStride: 2,
            maxPairsPerKey: 64,
            maxCorrespondences: 5000,
            ransacIterations: 1400,
            inlierThreshold: 0.01f,
            minInliers: 140);

        result.IsAmbiguous.Should().BeTrue();
        result.IsMatched.Should().BeFalse();
        result.AmbiguityScore.Should().BeGreaterThan(0.9);
        result.StabilityScore.Should().BeLessThan(0.35);
    }

    [Fact]
    public void Match_AxiallySymmetricCylinder_ShouldReportAmbiguity()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 292);
        using var model = gen.GenerateCylinder(
            center: Vector3.Zero,
            axis: Vector3.UnitZ,
            radius: 0.12f,
            height: 0.45f,
            numPoints: 2800,
            noise: 0.0004f,
            includeColors: false,
            includeNormals: true);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.35f, -0.24f, 1.10f) * Matrix4x4.CreateTranslation(0.05f, -0.03f, 0.02f);
        using var scene = model.Transform(gt);

        var matcher = new PPFMatcher(seed: 444);
        var result = matcher.Match(
            model,
            scene,
            normalRadius: 0.05f,
            featureRadius: 0.11f,
            distanceStep: 0.01f,
            angleStepRad: 5f * (MathF.PI / 180f),
            numSamples: 220,
            modelRefStride: 2,
            maxPairsPerKey: 64,
            maxCorrespondences: 5000,
            ransacIterations: 1400,
            inlierThreshold: 0.01f,
            minInliers: 150);

        result.IsAmbiguous.Should().BeTrue();
        result.IsMatched.Should().BeFalse();
        result.AmbiguityScore.Should().BeGreaterThan(0.85);
        result.NormalConsistency.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
    }

    [Fact]
    public void Match_NearSymmetricCylinderWithKeyFeature_ShouldRemainStableAndUnambiguous()
    {
        var gen = new SyntheticPointCloudGenerator(seed: 304);
        using var model = BuildNearSymmetricCylinderWithKey(gen);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.31f, -0.19f, 0.92f) * Matrix4x4.CreateTranslation(0.07f, -0.02f, 0.04f);
        using var scene = model.Transform(gt);

        var matcher = new PPFMatcher(seed: 512);
        var result = matcher.Match(
            model,
            scene,
            normalRadius: 0.05f,
            featureRadius: 0.11f,
            distanceStep: 0.01f,
            angleStepRad: 5f * (MathF.PI / 180f),
            numSamples: 220,
            modelRefStride: 2,
            maxPairsPerKey: 64,
            maxCorrespondences: 5000,
            ransacIterations: 1400,
            inlierThreshold: 0.01f,
            minInliers: 150);

        result.IsMatched.Should().BeTrue();
        result.IsAmbiguous.Should().BeFalse();
        result.StabilityScore.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedStabilityScore);
        result.NormalConsistency.Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
        var (translationError, rotationErrorDeg) = ComputePoseErrors(gt, result.TransformModelToScene);
        translationError.Should().BeLessThan(0.03);
        rotationErrorDeg.Should().BeLessThan(8.0);
    }

    private static PointCloudModel BuildAsymmetricModel(SyntheticPointCloudGenerator gen, bool includeNormals = false)
    {
        // Combine two shapes at different offsets to break symmetry.
        using var sphere = gen.GenerateSphere(
            center: new Vector3(0.0f, 0.0f, 0.0f),
            radius: 0.18f,
            numPoints: 2500,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: includeNormals,
            outlierRatio: 0.0f);

        using var cube = gen.GenerateCube(
            center: new Vector3(0.35f, 0.12f, -0.05f),
            edgeLength: 0.22f,
            numPoints: 1800,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: includeNormals,
            outlierRatio: 0.0f);

        return MergeTwo(sphere, cube);
    }

    private static PointCloudModel BuildNearSymmetricCylinderWithKey(SyntheticPointCloudGenerator gen)
    {
        using var cylinder = gen.GenerateCylinder(
            center: Vector3.Zero,
            axis: Vector3.UnitZ,
            radius: 0.12f,
            height: 0.45f,
            numPoints: 2600,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false);

        using var cube = gen.GenerateCube(
            center: new Vector3(0.13f, 0.01f, 0.10f),
            edgeLength: 0.08f,
            numPoints: 700,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false,
            outlierRatio: 0.0f);

        return MergeTwo(cylinder, cube);
    }

    private static PointCloudModel MergeTwo(PointCloudModel a, PointCloudModel b)
    {
        var pool = MatPool.Shared;
        var total = a.Count + b.Count;

        var points = pool.Rent(width: 3, height: total, type: MatType.CV_32FC1);
        a.Points.CopyTo(points.RowRange(0, a.Count));
        b.Points.CopyTo(points.RowRange(a.Count, total));

        Mat? colors = null;
        if (a.Colors != null && b.Colors != null)
        {
            colors = pool.Rent(width: 3, height: total, type: MatType.CV_8UC1);
            a.Colors.CopyTo(colors.RowRange(0, a.Count));
            b.Colors.CopyTo(colors.RowRange(a.Count, total));
        }

        Mat? normals = null;
        if (a.Normals != null && b.Normals != null)
        {
            normals = pool.Rent(width: 3, height: total, type: MatType.CV_32FC1);
            a.Normals.CopyTo(normals.RowRange(0, a.Count));
            b.Normals.CopyTo(normals.RowRange(a.Count, total));
        }

        return new PointCloudModel(points, colors, normals, isOrganized: false, pool: pool);
    }

    // Note: Exact pose error checks are intentionally omitted here.

    private static (double TranslationError, double RotationErrorDeg) ComputePoseErrors(Matrix4x4 expected, Matrix4x4 actual)
    {
        var translationError = Math.Sqrt(
            Math.Pow(expected.M41 - actual.M41, 2) +
            Math.Pow(expected.M42 - actual.M42, 2) +
            Math.Pow(expected.M43 - actual.M43, 2));

        Matrix4x4.Decompose(expected, out _, out var expectedRotation, out _).Should().BeTrue();
        Matrix4x4.Decompose(actual, out _, out var actualRotation, out _).Should().BeTrue();
        expectedRotation = Quaternion.Normalize(expectedRotation);
        actualRotation = Quaternion.Normalize(actualRotation);
        var delta = Quaternion.Normalize(actualRotation * Quaternion.Conjugate(expectedRotation));
        var rotationErrorDeg = 2.0 * Math.Acos(Math.Clamp(Math.Abs(delta.W), 0.0f, 1.0f)) * 180.0 / Math.PI;
        return (translationError, rotationErrorDeg);
    }

    private static double Percentile95(IReadOnlyCollection<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static PointCloudModel FlipNormals(PointCloudModel source)
    {
        var flipped = source.Transform(Matrix4x4.Identity);
        var normals = flipped.Normals!.GetGenericIndexer<float>();
        for (int i = 0; i < flipped.Count; i++)
        {
            normals[i, 0] = -normals[i, 0];
            normals[i, 1] = -normals[i, 1];
            normals[i, 2] = -normals[i, 2];
        }

        return flipped;
    }
}
