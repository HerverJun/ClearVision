using System.Numerics;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Operators;
using Acme.Product.Infrastructure.PointCloud;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using PointCloudModel = Acme.Product.Infrastructure.PointCloud.PointCloud;

namespace Acme.Product.Tests.Operators;

public sealed class VoxelDownsampleOperatorTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidPointCloud_ShouldReturnDownsampledCloud()
    {
        var sut = new VoxelDownsampleOperator(Substitute.For<ILogger<VoxelDownsampleOperator>>());
        var op = new Operator("voxel", OperatorType.VoxelDownsample, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "LeafSize", "LeafSize", string.Empty, "double", 0.03));

        var gen = new SyntheticPointCloudGenerator(seed: 41);
        using var cloud = gen.GenerateSphere(
            center: Vector3.Zero,
            radius: 0.2f,
            numPoints: 20_000,
            noise: 0.0005f,
            includeColors: true,
            includeNormals: true);

        var inputs = new Dictionary<string, object> { ["PointCloud"] = cloud };
        var result = await sut.ExecuteAsync(op, inputs);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.OutputData.Should().ContainKey("PointCloud");
        result.OutputData.Should().ContainKey("PointCount");

        var outCloud = result.OutputData!["PointCloud"].Should().BeOfType<PointCloudModel>().Subject;
        Convert.ToInt32(result.OutputData["PointCount"]).Should().Be(outCloud.Count);
        outCloud.Count.Should().BeLessThan(cloud.Count);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeCoordinatesAndSmallVoxelMotion_ShouldPreserveCentroidOffset()
    {
        var sut = new VoxelDownsampleOperator(Substitute.For<ILogger<VoxelDownsampleOperator>>());
        var op = new Operator("voxel-large-coordinate", OperatorType.VoxelDownsample, 0, 0);
        op.AddParameter(new Parameter(Guid.NewGuid(), "LeafSize", "LeafSize", string.Empty, "double", 1.0));

        using var points = new OpenCvSharp.Mat(4, 3, OpenCvSharp.MatType.CV_32FC1);
        var idx = points.GetGenericIndexer<float>();
        idx[0, 0] = 10_000.000f;
        idx[0, 1] = 20_000.000f;
        idx[0, 2] = 30_000.000f;
        idx[1, 0] = 10_000.125f;
        idx[1, 1] = 20_000.125f;
        idx[1, 2] = 30_000.125f;
        idx[2, 0] = 10_000.250f;
        idx[2, 1] = 20_000.250f;
        idx[2, 2] = 30_000.250f;
        idx[3, 0] = 10_000.375f;
        idx[3, 1] = 20_000.375f;
        idx[3, 2] = 30_000.375f;

        using var cloud = new PointCloudModel(points.Clone());
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["PointCloud"] = cloud });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var outCloud = result.OutputData!["PointCloud"].Should().BeOfType<PointCloudModel>().Subject;
        outCloud.Count.Should().Be(1);

        var outIdx = outCloud.Points.GetGenericIndexer<float>();
        outIdx[0, 0].Should().BeApproximately(10_000.1875f, 0.002f);
        outIdx[0, 1].Should().BeApproximately(20_000.1875f, 0.002f);
        outIdx[0, 2].Should().BeApproximately(30_000.1875f, 0.002f);
    }
}
