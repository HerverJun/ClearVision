using System.Reflection;
using Acme.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace Acme.Product.Tests.Operators;

public class FeatureMatchOperatorBaseTests
{
    [Fact]
    public void EstimateAndVerifyHomography_WhenVerificationFails_ShouldDropProjectedPoseData()
    {
        var sut = new AkazeFeatureMatchOperator(Substitute.For<ILogger<AkazeFeatureMatchOperator>>());
        var method = typeof(AkazeFeatureMatchOperator).BaseType!.GetMethod(
            "EstimateAndVerifyHomography",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var templateKeyPoints = new[]
        {
            new KeyPoint(0, 0, 1),
            new KeyPoint(20, 0, 1),
            new KeyPoint(20, 20, 1),
            new KeyPoint(0, 20, 1)
        };
        var searchKeyPoints = new[]
        {
            new KeyPoint(-100, -100, 1),
            new KeyPoint(200, -100, 1),
            new KeyPoint(200, 200, 1),
            new KeyPoint(-100, 200, 1)
        };
        var matches = Enumerable.Range(0, templateKeyPoints.Length)
            .Select(index => new DMatch(index, index, 0))
            .ToList();

        var result = method!.Invoke(sut, new object[]
        {
            templateKeyPoints,
            searchKeyPoints,
            matches,
            new Size(20, 20),
            new Size(30, 30),
            5.0,
            4,
            4,
            0.25,
            false
        });

        result.Should().NotBeNull();
        var tupleType = result!.GetType();
        var homography = (Mat?)tupleType.GetField("Item1")!.GetValue(result);
        var corners = (Point2f[])tupleType.GetField("Item2")!.GetValue(result)!;
        var metrics = tupleType.GetField("Item3")!.GetValue(result)!;
        var verificationPassed = (bool)metrics.GetType().GetProperty("VerificationPassed")!.GetValue(metrics)!;

        verificationPassed.Should().BeFalse();
        homography.Should().BeNull();
        corners.Should().BeEmpty();
    }

    [Fact]
    public void EstimateAndVerifyHomography_WhenPlaneIsPartiallyCroppedButCenterVisible_ShouldPass()
    {
        var sut = new AkazeFeatureMatchOperator(Substitute.For<ILogger<AkazeFeatureMatchOperator>>());
        var method = typeof(AkazeFeatureMatchOperator).BaseType!.GetMethod(
            "EstimateAndVerifyHomography",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var templateKeyPoints = new[]
        {
            new KeyPoint(0, 0, 1),
            new KeyPoint(100, 0, 1),
            new KeyPoint(100, 100, 1),
            new KeyPoint(0, 100, 1)
        };
        var searchKeyPoints = new[]
        {
            new KeyPoint(-40, 0, 1),
            new KeyPoint(60, 0, 1),
            new KeyPoint(60, 100, 1),
            new KeyPoint(-40, 100, 1)
        };
        var matches = Enumerable.Range(0, templateKeyPoints.Length)
            .Select(index => new DMatch(index, index, 0))
            .ToList();

        var result = method!.Invoke(sut, new object[]
        {
            templateKeyPoints,
            searchKeyPoints,
            matches,
            new Size(100, 100),
            new Size(100, 100),
            5.0,
            4,
            4,
            0.25,
            false
        });

        result.Should().NotBeNull();
        var tupleType = result!.GetType();
        using var homography = (Mat?)tupleType.GetField("Item1")!.GetValue(result);
        var corners = (Point2f[])tupleType.GetField("Item2")!.GetValue(result)!;
        var metrics = tupleType.GetField("Item3")!.GetValue(result)!;
        var verificationPassed = (bool)metrics.GetType().GetProperty("VerificationPassed")!.GetValue(metrics)!;

        verificationPassed.Should().BeTrue();
        homography.Should().NotBeNull();
        corners.Should().HaveCount(4);
    }

    [Fact]
    public void EstimateAndVerifyHomography_WhenOnlyCenterVisibleAndOptedIn_ShouldPass()
    {
        var sut = new AkazeFeatureMatchOperator(Substitute.For<ILogger<AkazeFeatureMatchOperator>>());
        var method = typeof(AkazeFeatureMatchOperator).BaseType!.GetMethod(
            "EstimateAndVerifyHomography",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var templateKeyPoints = new[]
        {
            new KeyPoint(0, 0, 1),
            new KeyPoint(100, 0, 1),
            new KeyPoint(100, 100, 1),
            new KeyPoint(0, 100, 1)
        };
        var searchKeyPoints = new[]
        {
            new KeyPoint(-80, -80, 1),
            new KeyPoint(180, -80, 1),
            new KeyPoint(180, 180, 1),
            new KeyPoint(-80, 180, 1)
        };
        var matches = Enumerable.Range(0, templateKeyPoints.Length)
            .Select(index => new DMatch(index, index, 0))
            .ToList();

        var result = method!.Invoke(sut, new object[]
        {
            templateKeyPoints,
            searchKeyPoints,
            matches,
            new Size(100, 100),
            new Size(100, 100),
            5.0,
            4,
            4,
            0.25,
            true
        });

        result.Should().NotBeNull();
        var tupleType = result!.GetType();
        using var homography = (Mat?)tupleType.GetField("Item1")!.GetValue(result);
        var corners = (Point2f[])tupleType.GetField("Item2")!.GetValue(result)!;
        var metrics = tupleType.GetField("Item3")!.GetValue(result)!;
        var verificationPassed = (bool)metrics.GetType().GetProperty("VerificationPassed")!.GetValue(metrics)!;
        var cornersInsideCount = (int)metrics.GetType().GetProperty("CornersInsideCount")!.GetValue(metrics)!;

        verificationPassed.Should().BeTrue();
        cornersInsideCount.Should().Be(0);
        homography.Should().NotBeNull();
        corners.Should().HaveCount(4);
    }

    [Fact]
    public void EstimateAndVerifyHomography_WhenOnlyCenterVisibleButDefaultOff_ShouldFail()
    {
        var sut = new AkazeFeatureMatchOperator(Substitute.For<ILogger<AkazeFeatureMatchOperator>>());
        var method = typeof(AkazeFeatureMatchOperator).BaseType!.GetMethod(
            "EstimateAndVerifyHomography",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var templateKeyPoints = new[]
        {
            new KeyPoint(0, 0, 1),
            new KeyPoint(100, 0, 1),
            new KeyPoint(100, 100, 1),
            new KeyPoint(0, 100, 1)
        };
        var searchKeyPoints = new[]
        {
            new KeyPoint(-80, -80, 1),
            new KeyPoint(180, -80, 1),
            new KeyPoint(180, 180, 1),
            new KeyPoint(-80, 180, 1)
        };
        var matches = Enumerable.Range(0, templateKeyPoints.Length)
            .Select(index => new DMatch(index, index, 0))
            .ToList();

        var result = method!.Invoke(sut, new object[]
        {
            templateKeyPoints,
            searchKeyPoints,
            matches,
            new Size(100, 100),
            new Size(100, 100),
            5.0,
            4,
            4,
            0.25,
            false
        });

        result.Should().NotBeNull();
        var tupleType = result!.GetType();
        var homography = (Mat?)tupleType.GetField("Item1")!.GetValue(result);
        var corners = (Point2f[])tupleType.GetField("Item2")!.GetValue(result)!;
        var metrics = tupleType.GetField("Item3")!.GetValue(result)!;
        var verificationPassed = (bool)metrics.GetType().GetProperty("VerificationPassed")!.GetValue(metrics)!;
        var failureReason = (string)metrics.GetType().GetProperty("FailureReason")!.GetValue(metrics)!;

        verificationPassed.Should().BeFalse();
        failureReason.Should().Be("Projected quadrilateral is invalid.");
        homography.Should().BeNull();
        corners.Should().BeEmpty();
    }

    [Fact]
    public void CenterOnlyProjectionGate_WhenRiskyHomography_ShouldReject()
    {
        var method = typeof(AkazeFeatureMatchOperator).Assembly
            .GetType("Acme.Product.Infrastructure.Operators.HomographyVerificationHelper")!
            .GetMethod("IsCenterOnlyProjectionAcceptable", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var externalCorners = new[]
        {
            new Point2f(-80, -80),
            new Point2f(180, -80),
            new Point2f(180, 180),
            new Point2f(-80, 180)
        };

        var largeExternalProjection = (bool)method!.Invoke(null, new object[]
        {
            externalCorners,
            6.76,
            0.8,
            2.0
        })!;
        var lowInlierProjection = (bool)method.Invoke(null, new object[]
        {
            externalCorners,
            1.0,
            0.6,
            0.5
        })!;

        largeExternalProjection.Should().BeFalse();
        lowInlierProjection.Should().BeFalse();
    }
}
