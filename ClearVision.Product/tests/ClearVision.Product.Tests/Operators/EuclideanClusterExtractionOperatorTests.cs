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
public sealed class EuclideanClusterExtractionOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WithTwoClusters_ShouldReturnClusterCountAndClouds()
    {
        var sut = new EuclideanClusterExtractionOperator(Substitute.For<ILogger<EuclideanClusterExtractionOperator>>());
        var op = new Operator("cluster", OperatorType.EuclideanClusterExtraction, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "ClusterTolerance", "ClusterTolerance", string.Empty, "double", 0.03));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinClusterSize", "MinClusterSize", string.Empty, "int", 100));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MaxClusterSize", "MaxClusterSize", string.Empty, "int", 100000));

        var gen = new SyntheticPointCloudGenerator(seed: 121);
        using var c1 = gen.GenerateSphere(
            center: new Vector3(-0.2f, 0, 0),
            radius: 0.06f,
            numPoints: 900,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false,
            outlierRatio: 0.0f);

        using var c2 = gen.GenerateSphere(
            center: new Vector3(0.2f, 0, 0),
            radius: 0.06f,
            numPoints: 850,
            noise: 0.0004f,
            includeColors: true,
            includeNormals: false,
            outlierRatio: 0.0f);

        using var cloud = MergeTwo(c1, c2);

        var inputs = new Dictionary<string, object> { ["PointCloud"] = cloud };
        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("ClusterCount");
        result.OutputData.Should().ContainKey("Clusters");
        result.OutputData.Should().ContainKey("ClusterPointClouds");

        Convert.ToInt32(result.OutputData!["ClusterCount"]).Should().Be(2);
        Convert.ToInt32(result.OutputData["CoreInvocationCount"]).Should().Be(1);
        result.OutputData["PointCloudsMaterialized"].Should().Be(true);
        foreach (var clusterCloud in result.OutputData["ClusterPointClouds"].Should().BeAssignableTo<List<ClearVision.Product.Infrastructure.PointCloud.PointCloud>>().Subject)
        {
            clusterCloud.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenMaterializationDisabled_ShouldReturnIndicesWithoutPointCloudAllocations()
    {
        var sut = new EuclideanClusterExtractionOperator(Substitute.For<ILogger<EuclideanClusterExtractionOperator>>());
        var op = new Operator("cluster", OperatorType.EuclideanClusterExtraction, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "ClusterTolerance", "ClusterTolerance", string.Empty, "double", 0.03));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MinClusterSize", "MinClusterSize", string.Empty, "int", 100));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MaxClusterSize", "MaxClusterSize", string.Empty, "int", 100000));
        op.AddParameter(new Parameter(Guid.NewGuid(), "MaterializePointClouds", "MaterializePointClouds", string.Empty, "bool", false));
        var gen = new SyntheticPointCloudGenerator(seed: 122);
        using var cloud = gen.GenerateSphere(Vector3.Zero, 0.06f, 900, 0.0004f, true, false, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["PointCloud"] = cloud });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        Convert.ToInt32(result.OutputData!["CoreInvocationCount"]).Should().Be(1);
        result.OutputData["PointCloudsMaterialized"].Should().Be(false);
        result.OutputData["ClusterPointClouds"].Should().BeAssignableTo<List<ClearVision.Product.Infrastructure.PointCloud.PointCloud>>().Which.Should().BeEmpty();
        result.OutputData["Clusters"].Should().BeAssignableTo<List<int[]>>().Which.Should().ContainSingle();
    }

    private static ClearVision.Product.Infrastructure.PointCloud.PointCloud MergeTwo(
        ClearVision.Product.Infrastructure.PointCloud.PointCloud a,
        ClearVision.Product.Infrastructure.PointCloud.PointCloud b)
    {
        // Duplicated helper to keep test self-contained.
        var pool = ClearVision.Product.Infrastructure.Memory.MatPool.Shared;
        var total = a.Count + b.Count;

        var points = pool.Rent(width: 3, height: total, type: OpenCvSharp.MatType.CV_32FC1);
        a.Points.CopyTo(points.RowRange(0, a.Count));
        b.Points.CopyTo(points.RowRange(a.Count, total));

        OpenCvSharp.Mat? colors = null;
        if (a.Colors != null && b.Colors != null)
        {
            colors = pool.Rent(width: 3, height: total, type: OpenCvSharp.MatType.CV_8UC1);
            a.Colors.CopyTo(colors.RowRange(0, a.Count));
            b.Colors.CopyTo(colors.RowRange(a.Count, total));
        }

        return new ClearVision.Product.Infrastructure.PointCloud.PointCloud(points, colors, normals: null, isOrganized: false, pool: pool);
    }
}
