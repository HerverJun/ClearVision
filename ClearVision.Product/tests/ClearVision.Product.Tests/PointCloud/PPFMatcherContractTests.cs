using System.Numerics;
using System.Reflection;
using ClearVision.Product.Infrastructure.PointCloud.Matching;
using ClearVision.Product.Tests.TestSupport;
using FluentAssertions;
using OpenCvSharp;
using PointCloudModel = ClearVision.Product.Infrastructure.PointCloud.PointCloud;

namespace ClearVision.Product.Tests.PointCloud;

[TestClassification(TestDomain.PointCloud, TestPurpose.Smoke, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality", Suites = "PPFPrSmoke")]
[Collection(PointCloudMatchingTestCollections.PointCloudMatching)]
public sealed class PPFMatcherContractTests
{
    [Fact]
    public void Match_EmptyModelOrScene_ShouldFailClosed()
    {
        using var model = new PointCloudModel(new Mat(0, 3, MatType.CV_32FC1));
        using var scene = new PointCloudModel(new Mat(0, 3, MatType.CV_32FC1));

        var result = new PPFMatcher(seed: 1).Match(model, scene);

        result.IsMatched.Should().BeFalse();
        result.IsAmbiguous.Should().BeFalse();
        result.InlierCount.Should().Be(0);
        result.CorrespondenceCount.Should().Be(0);
        double.IsPositiveInfinity(result.RmsError).Should().BeTrue();
    }

    [Fact]
    public void IsAmbiguousPose_AsymmetricCompetitiveLandscape_ShouldStillReportAmbiguous()
    {
        var symmetry = CreatePrivateValue("SymmetryDescriptor", 0.24, 0.18, 0.31);
        var landscape = CreatePrivateValue("HypothesisLandscape", 0.31, 0.95, 0.78, 3, 0.85);
        var ambiguityScore = InvokePrivateStatic<double>(
            "ComputeAmbiguityScore",
            420,
            0.0045,
            0.91,
            399,
            0.0048,
            0.90,
            symmetry,
            landscape);
        var ambiguous = InvokePrivateStatic<bool>(
            "IsAmbiguousPose",
            420,
            399,
            Matrix4x4.Identity,
            Matrix4x4.CreateFromYawPitchRoll(0.0f, 0.0f, 0.42f) * Matrix4x4.CreateTranslation(0.08f, -0.03f, 0.01f),
            0.01f,
            ambiguityScore,
            symmetry,
            0.0045,
            0.0048,
            0.91,
            0.90,
            landscape);

        ambiguityScore.Should().BeGreaterThan(0.74);
        ambiguityScore.Should().BeLessThan(0.86);
        ambiguous.Should().BeTrue();
    }

    [Fact]
    public void NearSphericalSymmetry_WithClearDominantLandscape_ShouldNotBeForcedAmbiguous()
    {
        var symmetry = CreatePrivateValue("SymmetryDescriptor", 0.992, 0.12, 0.975);
        var landscape = CreatePrivateValue("HypothesisLandscape", 0.74, 0.58, 0.32, 1, 0.18);
        var ambiguityScore = InvokePrivateStatic<double>(
            "ComputeAmbiguityScore",
            420,
            0.0045,
            0.94,
            180,
            0.0058,
            0.88,
            symmetry,
            landscape);
        var forcedAmbiguity = InvokePrivateStatic<bool>(
            "ShouldForceSphericalAmbiguity",
            420,
            180,
            0.94,
            symmetry,
            landscape);
        var ambiguous = InvokePrivateStatic<bool>(
            "IsAmbiguousPose",
            420,
            180,
            Matrix4x4.Identity,
            Matrix4x4.CreateFromYawPitchRoll(0.0f, 0.0f, 0.18f) * Matrix4x4.CreateTranslation(0.018f, 0.0f, 0.0f),
            0.01f,
            ambiguityScore,
            symmetry,
            0.0045,
            0.0058,
            0.94,
            0.88,
            landscape);

        forcedAmbiguity.Should().BeFalse();
        ambiguous.Should().BeFalse();
        ambiguityScore.Should().BeLessThan(0.86);
    }

    [Fact]
    public void ComputeIsotropicSymmetryPrior_ShouldIgnoreExtentIsotropyWithoutSphericalEvidence()
    {
        var dominantEvidence = 0.22;
        var nearCubicButNotSpherical = CreatePrivateValue("SymmetryDescriptor", 0.41, 0.08, 0.99);
        var anisotropicReference = CreatePrivateValue("SymmetryDescriptor", 0.41, 0.08, 0.18);

        var nearCubicPrior = InvokePrivateStatic<double>(
            "ComputeIsotropicSymmetryPrior",
            nearCubicButNotSpherical,
            dominantEvidence);
        var anisotropicPrior = InvokePrivateStatic<double>(
            "ComputeIsotropicSymmetryPrior",
            anisotropicReference,
            dominantEvidence);

        nearCubicPrior.Should().BeApproximately(anisotropicPrior, 1e-9);
        nearCubicPrior.Should().BeApproximately(0.41 * (1.0 - (dominantEvidence * 0.90)), 1e-9);
    }

    private static object CreatePrivateValue(string nestedTypeName, params object[] arguments)
    {
        var nestedType = typeof(PPFMatcher).GetNestedType(nestedTypeName, BindingFlags.NonPublic);
        nestedType.Should().NotBeNull();
        var value = Activator.CreateInstance(nestedType!, arguments);
        value.Should().NotBeNull();
        return value!;
    }

    private static T InvokePrivateStatic<T>(string methodName, params object[] arguments)
    {
        var method = typeof(PPFMatcher).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var value = method!.Invoke(null, arguments);
        value.Should().NotBeNull();
        return (T)value!;
    }
}
