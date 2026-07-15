using System.Numerics;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.PointCloud;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.PointCloud, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "operator-quality")]
public sealed class PPFEstimationOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidPointCloud_ShouldReturnPPFMapAndNormalsCloud()
    {
        var sut = new PPFEstimationOperator(Substitute.For<ILogger<PPFEstimationOperator>>());
        var op = new Operator("ppf", OperatorType.PPFEstimation, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "NormalRadius", "NormalRadius", string.Empty, "double", 0.03));
        op.AddParameter(new Parameter(Guid.NewGuid(), "FeatureRadius", "FeatureRadius", string.Empty, "double", 0.06));
        op.AddParameter(new Parameter(Guid.NewGuid(), "UseExistingNormals", "UseExistingNormals", string.Empty, "bool", true));

        var gen = new SyntheticPointCloudGenerator(seed: 141);
        using var cloud = gen.GenerateSphere(
            center: Vector3.Zero,
            radius: 0.2f,
            numPoints: 800,
            noise: 0.0002f,
            includeColors: true,
            includeNormals: true,
            outlierRatio: 0.0f);

        var inputs = new Dictionary<string, object> { ["PointCloud"] = cloud };
        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("PPFMap");
        result.OutputData.Should().ContainKey("PointCloudWithNormals");
        result.OutputData.Should().ContainKey("PointCount");

        var map = result.OutputData!["PPFMap"].Should().BeAssignableTo<Dictionary<int, List<ClearVision.Product.Infrastructure.PointCloud.Features.PPFFeature>>>().Subject;
        map.Should().ContainKey(0);

        var cloudWithNormals = result.OutputData["PointCloudWithNormals"].Should().BeOfType<ClearVision.Product.Infrastructure.PointCloud.PointCloud>().Subject;
        cloudWithNormals.Normals.Should().NotBeNull();
        cloudWithNormals.Colors.Should().NotBeNull();
        cloudWithNormals.Count.Should().Be(cloud.Count);
    }
}
