using System.Numerics;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.PointCloud;
using ClearVision.Product.Infrastructure.PointCloud.Matching;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;
using Xunit;
using MatPool = ClearVision.Product.Infrastructure.Memory.MatPool;
using PointCloudModel = ClearVision.Product.Infrastructure.PointCloud.PointCloud;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Matching, TestPurpose.Regression, TestLane.Nightly, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Long, TestFlakyPolicy.Blocking, "operator-quality", Suites = "PPFRegression")]
[Collection(PointCloudMatchingTestCollections.PointCloudMatching)]
public sealed class PPFMatchOperatorNightlyTests
{
    [Fact]
    public async Task ExecuteAsync_AsymmetricSceneWithCompetingPlacements_ShouldExposeAmbiguity()
    {
        var sut = new PPFMatchOperator(Substitute.For<ILogger<PPFMatchOperator>>());

        var op = new Operator("ppf_match_asymmetric_competing", OperatorType.PPFMatch, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "Seed", "Seed", string.Empty, "int", 417));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NormalRadius", "NormalRadius", string.Empty, "double", 0.06));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FeatureRadius", "FeatureRadius", string.Empty, "double", 0.12));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumSamples", "NumSamples", string.Empty, "int", 340));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ModelRefStride", "ModelRefStride", string.Empty, "int", 1));
        op.AddParameter(new Parameter(Guid.NewGuid(), "RansacIterations", "RansacIterations", string.Empty, "int", 2400));
        op.AddParameter(new Parameter(Guid.NewGuid(), "InlierThreshold", "InlierThreshold", string.Empty, "double", 0.01));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinInliers", "MinInliers", string.Empty, "int", 120));
        op.AddParameter(new Parameter(Guid.NewGuid(), "DistanceStep", "DistanceStep", string.Empty, "double", 0.01));
        op.AddParameter(new Parameter(Guid.NewGuid(), "AngleStepDeg", "AngleStepDeg", string.Empty, "double", 5.0));

        var gen = new SyntheticPointCloudGenerator(seed: 341);
        using var model = BuildCompetingAsymmetricModel(gen);
        using var sceneA = model.Transform(Matrix4x4.CreateFromYawPitchRoll(0.42f, -0.21f, 0.28f) * Matrix4x4.CreateTranslation(0.09f, -0.05f, 0.03f));
        using var sceneB = model.Transform(Matrix4x4.CreateFromYawPitchRoll(-0.14f, 0.19f, 1.02f) * Matrix4x4.CreateTranslation(-0.11f, 0.09f, -0.01f));
        using var scene = MergeTwo(sceneA, sceneB);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ModelPointCloud"] = model,
            ["ScenePointCloud"] = scene
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToBoolean(result.OutputData!["IsMatched"]).Should().BeFalse();
        Convert.ToBoolean(result.OutputData["IsMatch"]).Should().BeFalse();
        Convert.ToBoolean(result.OutputData["AmbiguityDetected"]).Should().BeTrue();
        Convert.ToBoolean(result.OutputData["VerificationPassed"]).Should().BeFalse();
        result.OutputData["FailureReason"].Should().Be("Ambiguous coarse pose solution.");
        Convert.ToDouble(result.OutputData["Score"]).Should().Be(0.0);
        Convert.ToInt32(result.OutputData["MatchCount"]).Should().Be(0);
        Convert.ToInt32(result.OutputData["InlierCount"]).Should().BeGreaterThanOrEqualTo(120);
        Convert.ToInt32(result.OutputData["CorrespondenceCount"]).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecuteAsync_SymmetricSphere_ShouldExposeAmbiguity()
    {
        var sut = new PPFMatchOperator(Substitute.For<ILogger<PPFMatchOperator>>());

        var op = new Operator("ppf_match_sphere", OperatorType.PPFMatch, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "NormalRadius", "NormalRadius", string.Empty, "double", 0.05));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FeatureRadius", "FeatureRadius", string.Empty, "double", 0.10));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumSamples", "NumSamples", string.Empty, "int", 160));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ModelRefStride", "ModelRefStride", string.Empty, "int", 2));
        op.AddParameter(new Parameter(Guid.NewGuid(), "RansacIterations", "RansacIterations", string.Empty, "int", 1200));
        op.AddParameter(new Parameter(Guid.NewGuid(), "InlierThreshold", "InlierThreshold", string.Empty, "double", 0.01));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinInliers", "MinInliers", string.Empty, "int", 120));

        var gen = new SyntheticPointCloudGenerator(seed: 311);
        using var model = gen.GenerateSphere(Vector3.Zero, radius: 0.20f, numPoints: 2600, noise: 0.0004f, includeColors: false, includeNormals: true);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.5f, 0.2f, -0.4f) * Matrix4x4.CreateTranslation(0.08f, -0.06f, 0.03f);
        using var scene = model.Transform(gt);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ModelPointCloud"] = model,
            ["ScenePointCloud"] = scene
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToBoolean(result.OutputData!["IsMatched"]).Should().BeFalse();
        Convert.ToBoolean(result.OutputData["IsMatch"]).Should().BeFalse();
        Convert.ToBoolean(result.OutputData["AmbiguityDetected"]).Should().BeTrue();
        Convert.ToBoolean(result.OutputData["VerificationPassed"]).Should().BeFalse();
        Convert.ToDouble(result.OutputData["Score"]).Should().Be(0.0);
        Convert.ToInt32(result.OutputData["MatchCount"]).Should().Be(0);
        Convert.ToInt32(result.OutputData["CorrespondenceCount"]).Should().BeGreaterThan(0);
        Convert.ToDouble(result.OutputData["InlierRatio"]).Should().BeGreaterThan(0.0);
        Convert.ToDouble(result.OutputData["StabilityScore"]).Should().BeLessThan(0.35);
        result.OutputData["FailureReason"].Should().Be("Ambiguous coarse pose solution.");
    }

    [Fact]
    public async Task ExecuteAsync_AxiallySymmetricCylinder_ShouldExposeAmbiguityDiagnostics()
    {
        var sut = new PPFMatchOperator(Substitute.For<ILogger<PPFMatchOperator>>());

        var op = new Operator("ppf_match_cylinder", OperatorType.PPFMatch, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "NormalRadius", "NormalRadius", string.Empty, "double", 0.05));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FeatureRadius", "FeatureRadius", string.Empty, "double", 0.11));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumSamples", "NumSamples", string.Empty, "int", 180));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ModelRefStride", "ModelRefStride", string.Empty, "int", 2));
        op.AddParameter(new Parameter(Guid.NewGuid(), "RansacIterations", "RansacIterations", string.Empty, "int", 1400));
        op.AddParameter(new Parameter(Guid.NewGuid(), "InlierThreshold", "InlierThreshold", string.Empty, "double", 0.01));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinInliers", "MinInliers", string.Empty, "int", 150));

        var gen = new SyntheticPointCloudGenerator(seed: 321);
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

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ModelPointCloud"] = model,
            ["ScenePointCloud"] = scene
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToBoolean(result.OutputData!["AmbiguityDetected"]).Should().BeTrue();
        Convert.ToBoolean(result.OutputData["VerificationPassed"]).Should().BeFalse();
        Convert.ToDouble(result.OutputData["AmbiguityScore"]).Should().BeGreaterThan(0.85);
        Convert.ToDouble(result.OutputData["NormalConsistency"]).Should().BeGreaterThan(PPFMatcher.MinimumRecommendedNormalConsistency);
    }

    [Fact]
    public async Task ExecuteAsync_NearSymmetricCylinderWithKeyFeature_ShouldStayMatched()
    {
        var sut = new PPFMatchOperator(Substitute.For<ILogger<PPFMatchOperator>>());

        var op = new Operator("ppf_match_near_symmetric", OperatorType.PPFMatch, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "NormalRadius", "NormalRadius", string.Empty, "double", 0.05));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FeatureRadius", "FeatureRadius", string.Empty, "double", 0.11));
        op.AddParameter(new Parameter(Guid.NewGuid(), "NumSamples", "NumSamples", string.Empty, "int", 180));
        op.AddParameter(new Parameter(Guid.NewGuid(), "ModelRefStride", "ModelRefStride", string.Empty, "int", 2));
        op.AddParameter(new Parameter(Guid.NewGuid(), "RansacIterations", "RansacIterations", string.Empty, "int", 1400));
        op.AddParameter(new Parameter(Guid.NewGuid(), "InlierThreshold", "InlierThreshold", string.Empty, "double", 0.01));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinInliers", "MinInliers", string.Empty, "int", 150));

        var gen = new SyntheticPointCloudGenerator(seed: 331);
        using var model = BuildNearSymmetricCylinderWithKey(gen);
        var gt = Matrix4x4.CreateFromYawPitchRoll(0.31f, -0.19f, 0.92f) * Matrix4x4.CreateTranslation(0.07f, -0.02f, 0.04f);
        using var scene = model.Transform(gt);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["ModelPointCloud"] = model,
            ["ScenePointCloud"] = scene
        });

        result.IsSuccess.Should().BeTrue();
        Convert.ToBoolean(result.OutputData!["IsMatched"]).Should().BeTrue();
        Convert.ToBoolean(result.OutputData["IsMatch"]).Should().BeTrue();
        Convert.ToBoolean(result.OutputData["AmbiguityDetected"]).Should().BeFalse();
        Convert.ToBoolean(result.OutputData["VerificationPassed"]).Should().BeTrue();
        result.OutputData["FailureReason"].Should().Be(string.Empty);
        Convert.ToDouble(result.OutputData["StabilityScore"]).Should().BeGreaterThan(PPFMatcher.MinimumRecommendedStabilityScore);
    }

    private static PointCloudModel BuildCompetingAsymmetricModel(SyntheticPointCloudGenerator gen)
    {
        using var sphere = gen.GenerateSphere(
            center: new Vector3(0.0f, 0.0f, 0.0f),
            radius: 0.18f,
            numPoints: 2500,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false,
            outlierRatio: 0.0f);

        using var cube = gen.GenerateCube(
            center: new Vector3(0.35f, 0.12f, -0.05f),
            edgeLength: 0.22f,
            numPoints: 1800,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false,
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
            numPoints: 2400,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false);

        using var cube = gen.GenerateCube(
            center: new Vector3(0.13f, 0.01f, 0.10f),
            edgeLength: 0.08f,
            numPoints: 650,
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

        return new PointCloudModel(points, colors, normals: null, isOrganized: false, pool: pool);
    }
}
