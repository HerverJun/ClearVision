using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

[TestClassification(TestDomain.Core, TestPurpose.Regression, TestLane.Pr, TestEvidenceType.Contract, TestOracleType.Contract, TestResourceRequirement.None, TestExpectedDuration.Fast, TestFlakyPolicy.Blocking, "product", Suites = "Stage12Regression")]
public class Phase42RegionProcessingOperatorTests
{
    [Fact]
    public void RegionOperatorMetadata_ShouldExposeRegionPortTypes()
    {
        var metadataByType = new OperatorFactory()
            .GetAllMetadata()
            .ToDictionary(metadata => metadata.Type);

        var expectedRegionPorts = new Dictionary<OperatorType, (string[] Inputs, string[] Outputs)>
        {
            [OperatorType.RegionErosion] = (["Region"], ["Region"]),
            [OperatorType.RegionDilation] = (["Region"], ["Region"]),
            [OperatorType.RegionOpening] = (["Region"], ["Region"]),
            [OperatorType.RegionClosing] = (["Region"], ["Region"]),
            [OperatorType.RegionSkeleton] = (["Region"], ["Region"]),
            [OperatorType.RegionUnion] = (["Region1", "Region2"], ["Region"]),
            [OperatorType.RegionIntersection] = (["Region1", "Region2"], ["Region"]),
            [OperatorType.RegionDifference] = (["Region1", "Region2"], ["Region"]),
            [OperatorType.RegionComplement] = (["Region"], ["Region"])
        };

        foreach (var (operatorType, ports) in expectedRegionPorts)
        {
            metadataByType.Should().ContainKey(operatorType);
            var metadata = metadataByType[operatorType];

            foreach (var inputName in ports.Inputs)
            {
                metadata.InputPorts.Should().ContainSingle(port =>
                    port.Name == inputName &&
                    port.DataType == PortDataType.Region);
            }

            foreach (var outputName in ports.Outputs)
            {
                metadata.OutputPorts.Should().ContainSingle(port =>
                    port.Name == outputName &&
                    port.DataType == PortDataType.Region);
            }
        }

        metadataByType[OperatorType.RegionClosing].InputPorts
            .Should().ContainSingle(port => port.Name == "Image" && port.DataType == PortDataType.Image && !port.IsRequired);

        metadataByType[OperatorType.BinaryImageToRegion].InputPorts
            .Should().ContainSingle(port => port.Name == "Image" && port.DataType == PortDataType.Image);
        metadataByType[OperatorType.BinaryImageToRegion].OutputPorts
            .Should().ContainSingle(port => port.Name == "Region" && port.DataType == PortDataType.Region);
        metadataByType[OperatorType.BinaryImageToRegion].OutputPorts
            .Should().ContainSingle(port => port.Name == "Image" && port.DataType == PortDataType.Image);

        var converterMetadata = metadataByType[OperatorType.BinaryImageToRegion];
        converterMetadata.DisplayName.Should().Be("二值图转区域");
        converterMetadata.Description.Should().Contain("像素区域 Region");
        converterMetadata.Keywords.Should().Contain(new[] { "二值图转区域", "图像转区域", "掩膜", "Region", "mask" });

        foreach (var operatorType in new[]
                 {
                     OperatorType.RegionErosion,
                     OperatorType.RegionDilation,
                     OperatorType.RegionOpening,
                     OperatorType.RegionClosing
                 })
        {
            metadataByType[operatorType].InputPorts.Should().ContainSingle(port =>
                port.Name == "Region" &&
                port.IsRequired &&
                port.Description!.Contains("Image 或 Contour 不能直接替代", StringComparison.Ordinal));
            metadataByType[operatorType].InputPorts.Should().ContainSingle(port =>
                port.Name == "Image" &&
                !port.IsRequired &&
                port.Description!.Contains("仅用于参考图和结果可视化", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void RegionCompatibility_ShouldRemainStrictWithoutAnyFallback()
    {
        PortDataTypeCompatibility.AreCompatible(PortDataType.Contour, PortDataType.Region).Should().BeFalse();
        PortDataTypeCompatibility.AreCompatible(PortDataType.Image, PortDataType.Region).Should().BeFalse();
        PortDataTypeCompatibility.AreCompatible(PortDataType.Region, PortDataType.Region).Should().BeTrue();
        PortDataTypeCompatibility.AreCompatible(PortDataType.Any, PortDataType.Region).Should().BeTrue();
        PortDataTypeCompatibility.AreCompatible(PortDataType.BlobList, PortDataType.Region).Should().BeFalse();
        PortDataTypeCompatibility.AreCompatible(PortDataType.BlobFeatureList, PortDataType.BlobList).Should().BeFalse();
    }

    [Fact]
    public void OperatorFlow_ShouldRejectImageOutputConnectedToRegionInput()
    {
        var factory = new OperatorFactory();
        var threshold = factory.CreateOperator(OperatorType.Thresholding, "Threshold", 0, 0);
        var closing = factory.CreateOperator(OperatorType.RegionClosing, "RegionClosing", 100, 0);
        var flow = new OperatorFlow("Region Type Boundary");
        flow.AddOperator(threshold);
        flow.AddOperator(closing);

        var connection = new OperatorConnection(
            threshold.Id,
            threshold.OutputPorts.Single(port => port.Name == "Image").Id,
            closing.Id,
            closing.InputPorts.Single(port => port.Name == "Region").Id);

        var act = () => flow.AddConnection(connection);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Image -> Region*");
    }

    [Fact]
    public void FlowLinter_ShouldRejectImageToRegionWithProductPathSuggestion()
    {
        var factory = new OperatorFactory();
        var threshold = factory.CreateOperator(OperatorType.Thresholding, "Threshold", 0, 0);
        var closing = factory.CreateOperator(OperatorType.RegionClosing, "RegionClosing", 100, 0);
        var flow = new OperatorFlow("Region Type Boundary");
        flow.AddOperator(threshold);
        flow.AddOperator(closing);
        flow.Connections.Add(new OperatorConnection(
            threshold.Id,
            threshold.OutputPorts.Single(port => port.Name == "Image").Id,
            closing.Id,
            closing.InputPorts.Single(port => port.Name == "Region").Id));

        var result = new FlowLinter().Lint(flow);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "STRUCT_004" &&
            issue.Suggestion == "当前输出是 Image/图像，不是 Region；请插入 BinaryImageToRegion。");
    }

    [Fact]
    public void OperatorFlow_ShouldRejectContourOutputConnectedToRegionInput()
    {
        var factory = new OperatorFactory();
        var contour = factory.CreateOperator(OperatorType.ContourDetection, "ContourDetection", 0, 0);
        var erosion = factory.CreateOperator(OperatorType.RegionErosion, "RegionErosion", 100, 0);
        var flow = new OperatorFlow("Contour Region Type Boundary");
        flow.AddOperator(contour);
        flow.AddOperator(erosion);

        var connection = new OperatorConnection(
            contour.Id,
            contour.OutputPorts.Single(port => port.Name == "Contours").Id,
            erosion.Id,
            erosion.InputPorts.Single(port => port.Name == "Region").Id);

        var act = () => flow.AddConnection(connection);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Contour -> Region*");
    }

    [Fact]
    public void FlowLinter_ShouldRejectContourToRegionWithSemanticSuggestion()
    {
        var factory = new OperatorFactory();
        var contour = factory.CreateOperator(OperatorType.ContourDetection, "ContourDetection", 0, 0);
        var erosion = factory.CreateOperator(OperatorType.RegionErosion, "RegionErosion", 100, 0);
        var flow = new OperatorFlow("Contour Region Type Boundary");
        flow.AddOperator(contour);
        flow.AddOperator(erosion);
        flow.Connections.Add(new OperatorConnection(
            contour.Id,
            contour.OutputPorts.Single(port => port.Name == "Contours").Id,
            erosion.Id,
            erosion.InputPorts.Single(port => port.Name == "Region").Id));

        var result = new FlowLinter().Lint(flow);

        result.HasErrors.Should().BeTrue();
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "STRUCT_004" &&
            issue.Suggestion.Contains("Contour/轮廓", StringComparison.Ordinal) &&
            issue.Suggestion.Contains("Region/像素区域", StringComparison.Ordinal) &&
            issue.Suggestion.Contains("BinaryImageToRegion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BinaryImageToRegion_ShouldCreateRegionUsableByRegionClosing()
    {
        using var mat = new Mat(32, 32, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(8, 8, 10, 10), Scalar.White, -1);

        var converter = new BinaryImageToRegionOperator(Substitute.For<ILogger<BinaryImageToRegionOperator>>());
        var convertResult = await converter.ExecuteAsync(
            new Operator("BinaryImageToRegion", OperatorType.BinaryImageToRegion, 0, 0),
            new Dictionary<string, object> { ["Image"] = new ImageWrapper(mat.Clone()) });

        try
        {
            convertResult.IsSuccess.Should().BeTrue();
            var region = convertResult.OutputData!["Region"].Should().BeOfType<Region>().Subject;
            region.Area.Should().Be(100);

            var closing = new RegionClosingOperator(Substitute.For<ILogger<RegionClosingOperator>>());
            var closeResult = await closing.ExecuteAsync(
                new Operator("RegionClosing", OperatorType.RegionClosing, 0, 0),
                new Dictionary<string, object> { ["Region"] = region });

            try
            {
                closeResult.IsSuccess.Should().BeTrue();
                closeResult.OutputData!["Region"].Should().BeOfType<Region>();
            }
            finally
            {
                DisposeOutputImage(closeResult.OutputData);
            }
        }
        finally
        {
            DisposeOutputImage(convertResult.OutputData);
        }
    }

    [Fact]
    public async Task RegionErosion_ShouldReduceArea()
    {
        var sut = new RegionErosionOperator(Substitute.For<ILogger<RegionErosionOperator>>());
        var region = CreateRectangleRegion(10, 10, 30, 24);
        var op = new Operator("RegionErosion", OperatorType.RegionErosion, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var eroded = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        eroded.Area.Should().BeLessThan(region.Area);
    }

    [Fact]
    public async Task RegionDilation_ShouldIncreaseArea()
    {
        var sut = new RegionDilationOperator(Substitute.For<ILogger<RegionDilationOperator>>());
        var region = CreateRectangleRegion(20, 20, 20, 16);
        var op = new Operator("RegionDilation", OperatorType.RegionDilation, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var dilated = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        dilated.Area.Should().BeGreaterThan(region.Area);
    }

    [Fact]
    public async Task RegionOpening_ShouldSuppressSmallNoise()
    {
        var sut = new RegionOpeningOperator(Substitute.For<ILogger<RegionOpeningOperator>>());
        var region = CreateRegionWithNoise();
        var op = new Operator("RegionOpening", OperatorType.RegionOpening, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var opened = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        opened.Area.Should().BeLessOrEqualTo(region.Area);
    }

    [Fact]
    public async Task RegionClosing_ShouldFillSmallHole()
    {
        var sut = new RegionClosingOperator(Substitute.For<ILogger<RegionClosingOperator>>());
        var region = CreateRegionWithHole();
        var op = new Operator("RegionClosing", OperatorType.RegionClosing, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("KernelWidth", 7, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelHeight", 7, "int"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var closed = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        closed.Area.Should().BeGreaterThan(region.Area);
    }

    [Fact]
    public async Task RegionSkeleton_ShouldProduceThinnerRegion()
    {
        var sut = new RegionSkeletonOperator(Substitute.For<ILogger<RegionSkeletonOperator>>());
        var region = CreateThickCrossRegion();
        var op = new Operator("RegionSkeleton", OperatorType.RegionSkeleton, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var skeleton = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        skeleton.Area.Should().BeGreaterThan(0);
        skeleton.Area.Should().BeLessThan(region.Area);
    }

    [Fact]
    public async Task RegionSkeleton_ShouldPreserveOriginalCoordinates()
    {
        var sut = new RegionSkeletonOperator(Substitute.For<ILogger<RegionSkeletonOperator>>());
        var region = CreateOffsetThickCrossRegion();
        var op = new Operator("RegionSkeleton", OperatorType.RegionSkeleton, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var skeleton = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        skeleton.BoundingBox.X.Should().BeGreaterThanOrEqualTo(region.BoundingBox.X);
        skeleton.BoundingBox.Y.Should().BeGreaterThanOrEqualTo(region.BoundingBox.Y);
        skeleton.BoundingBox.Right.Should().BeLessThanOrEqualTo(region.BoundingBox.Right);
        skeleton.BoundingBox.Bottom.Should().BeLessThanOrEqualTo(region.BoundingBox.Bottom);
    }

    [Fact]
    public async Task RegionDilation_WithEvenSizedKernel_ShouldMatchOpenCv()
    {
        var sut = new RegionDilationOperator(Substitute.For<ILogger<RegionDilationOperator>>());
        using var mat = new Mat(40, 40, MatType.CV_8UC1, Scalar.Black);
        mat.Set(20, 20, (byte)255);
        var region = Region.FromMat(mat);

        var op = new Operator("RegionDilation", OperatorType.RegionDilation, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("KernelWidth", 4, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelHeight", 4, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelShape", "Rectangle", "string"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var dilated = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;

        using var expected = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(4, 4));
        Cv2.Dilate(mat, expected, kernel);
        var expectedRegion = Region.FromMat(expected);

        dilated.RunLengths.Should().Equal(expectedRegion.RunLengths);
    }

    [Fact]
    public async Task RegionBooleanOperators_ShouldProduceConsistentAreas()
    {
        var unionSut = new RegionUnionOperator(Substitute.For<ILogger<RegionUnionOperator>>());
        var intersectionSut = new RegionIntersectionOperator(Substitute.For<ILogger<RegionIntersectionOperator>>());
        var diffSut = new RegionDifferenceOperator(Substitute.For<ILogger<RegionDifferenceOperator>>());
        var complementSut = new RegionComplementOperator(Substitute.For<ILogger<RegionComplementOperator>>());

        var regionA = CreateRectangleRegion(10, 10, 20, 20);
        var regionB = CreateRectangleRegion(20, 18, 20, 20);

        var unionResult = await unionSut.ExecuteAsync(new Operator("RegionUnion", OperatorType.RegionUnion, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = regionA,
            ["Region2"] = regionB
        });
        var intersectionResult = await intersectionSut.ExecuteAsync(new Operator("RegionIntersection", OperatorType.RegionIntersection, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = regionA,
            ["Region2"] = regionB
        });
        var differenceResult = await diffSut.ExecuteAsync(new Operator("RegionDifference", OperatorType.RegionDifference, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = regionA,
            ["Region2"] = regionB
        });
        var complementResult = await complementSut.ExecuteAsync(new Operator("RegionComplement", OperatorType.RegionComplement, 0, 0), new Dictionary<string, object>
        {
            ["Region"] = regionA,
            ["ImageWidth"] = 60,
            ["ImageHeight"] = 50
        });

        unionResult.IsSuccess.Should().BeTrue();
        intersectionResult.IsSuccess.Should().BeTrue();
        differenceResult.IsSuccess.Should().BeTrue();
        complementResult.IsSuccess.Should().BeTrue();

        var union = (Region)unionResult.OutputData!["Region"];
        var intersection = (Region)intersectionResult.OutputData!["Region"];
        var difference = (Region)differenceResult.OutputData!["Region"];
        var complement = (Region)complementResult.OutputData!["Region"];

        union.Area.Should().Be(regionA.Area + regionB.Area - intersection.Area);
        difference.Area.Should().Be(regionA.Area - intersection.Area);
        complement.Area.Should().Be(60 * 50 - regionA.Area);
    }

    [Fact]
    public async Task RegionBooleanOperators_WithEmptyInputs_ShouldReturnStableEmptyOutputs()
    {
        var empty = new Region();
        var unionSut = new RegionUnionOperator(Substitute.For<ILogger<RegionUnionOperator>>());
        var intersectionSut = new RegionIntersectionOperator(Substitute.For<ILogger<RegionIntersectionOperator>>());
        var diffSut = new RegionDifferenceOperator(Substitute.For<ILogger<RegionDifferenceOperator>>());

        var unionResult = await unionSut.ExecuteAsync(new Operator("RegionUnion", OperatorType.RegionUnion, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = empty,
            ["Region2"] = empty
        });
        var intersectionResult = await intersectionSut.ExecuteAsync(new Operator("RegionIntersection", OperatorType.RegionIntersection, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = empty,
            ["Region2"] = empty
        });
        var differenceResult = await diffSut.ExecuteAsync(new Operator("RegionDifference", OperatorType.RegionDifference, 0, 0), new Dictionary<string, object>
        {
            ["Region1"] = empty,
            ["Region2"] = empty
        });

        unionResult.IsSuccess.Should().BeTrue();
        intersectionResult.IsSuccess.Should().BeTrue();
        differenceResult.IsSuccess.Should().BeTrue();

        ((Region)unionResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)intersectionResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)differenceResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        unionResult.OutputData!["Area"].Should().Be(0);
        intersectionResult.OutputData!["Area"].Should().Be(0);
        differenceResult.OutputData!["Area"].Should().Be(0);
    }

    [Fact]
    public async Task RegionUnion_WithDuplicateInputs_ShouldBeIdempotent()
    {
        var sut = new RegionUnionOperator(Substitute.For<ILogger<RegionUnionOperator>>());
        var region = CreateRegionWithHole();

        var result = await sut.ExecuteAsync(
            new Operator("RegionUnion", OperatorType.RegionUnion, 0, 0),
            new Dictionary<string, object>
            {
                ["Region1"] = region,
                ["Region2"] = region
            });

        result.IsSuccess.Should().BeTrue();
        var union = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        union.Area.Should().Be(region.Area);
        union.RunLengths.Should().Equal(region.RunLengths);
    }

    [Fact]
    public async Task RegionComplement_WithEmptyRegion_ShouldReturnWholeImage()
    {
        var sut = new RegionComplementOperator(Substitute.For<ILogger<RegionComplementOperator>>());
        var result = await sut.ExecuteAsync(
            new Operator("RegionComplement", OperatorType.RegionComplement, 0, 0),
            new Dictionary<string, object>
            {
                ["Region"] = new Region(),
                ["ImageWidth"] = 12,
                ["ImageHeight"] = 8
            });

        result.IsSuccess.Should().BeTrue();
        var complement = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        complement.Area.Should().Be(96);
        complement.ContainsPoint(0, 0).Should().BeTrue();
        complement.ContainsPoint(11, 7).Should().BeTrue();
    }

    [Fact]
    public async Task RegionComplement_WithFullImageRegion_ShouldReturnEmptyRegion()
    {
        var sut = new RegionComplementOperator(Substitute.For<ILogger<RegionComplementOperator>>());
        var fullRegion = CreateRectangleRegion(0, 0, 12, 8, imageWidth: 12, imageHeight: 8);

        var result = await sut.ExecuteAsync(
            new Operator("RegionComplement", OperatorType.RegionComplement, 0, 0),
            new Dictionary<string, object>
            {
                ["Region"] = fullRegion,
                ["ImageWidth"] = 12,
                ["ImageHeight"] = 8
            });

        result.IsSuccess.Should().BeTrue();
        var complement = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        complement.IsEmpty.Should().BeTrue();
        complement.Area.Should().Be(0);
    }

    [Fact]
    public async Task RegionComplement_ShouldClipInputRunsToExplicitImageBounds()
    {
        var sut = new RegionComplementOperator(Substitute.For<ILogger<RegionComplementOperator>>());
        var region = new Region(new[]
        {
            new RunLength(0, -3, 2),
            new RunLength(1, 8, 15),
            new RunLength(2, 12, 14)
        });

        var result = await sut.ExecuteAsync(
            new Operator("RegionComplement", OperatorType.RegionComplement, 0, 0),
            new Dictionary<string, object>
            {
                ["Region"] = region,
                ["ImageWidth"] = 10,
                ["ImageHeight"] = 4
            });

        result.IsSuccess.Should().BeTrue();
        var complement = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        complement.Area.Should().Be(35);
        complement.RunLengths.Should().OnlyContain(run => run.StartX >= 0 && run.EndX < 10 && run.Y >= 0 && run.Y < 4);
        complement.ContainsPoint(9, 1).Should().BeFalse();
        complement.ContainsPoint(5, 3).Should().BeTrue();
    }

    [Fact]
    public async Task RegionComplement_ShouldIgnoreRowsOutsideExplicitImageBounds()
    {
        var sut = new RegionComplementOperator(Substitute.For<ILogger<RegionComplementOperator>>());
        var region = new Region(new[]
        {
            new RunLength(-1, 0, 5),
            new RunLength(1, 2, 3),
            new RunLength(4, 0, 5)
        });

        var result = await sut.ExecuteAsync(
            new Operator("RegionComplement", OperatorType.RegionComplement, 0, 0),
            new Dictionary<string, object>
            {
                ["Region"] = region,
                ["ImageWidth"] = 6,
                ["ImageHeight"] = 3
            });

        result.IsSuccess.Should().BeTrue();
        var complement = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        complement.Area.Should().Be(16);
        complement.ContainsPoint(2, 1).Should().BeFalse();
        complement.ContainsPoint(4, 1).Should().BeTrue();
        complement.ContainsPoint(0, 0).Should().BeTrue();
        complement.RunLengths.Should().OnlyContain(run => run.Y >= 0 && run.Y < 3);
        result.OutputData!["ClippedInputArea"].Should().Be(2);
        ((double)result.OutputData!["FillRatio"]).Should().BeApproximately(2.0 / 18.0, 1e-9);
    }

    [Fact]
    public void RegionGetContourPoints_WithEmptyRegion_ShouldReturnEmptyCollection()
    {
        var empty = new Region();

        var points = empty.GetContourPoints();

        points.Should().BeEmpty();
    }

    [Fact]
    public async Task RegionMorphologyOperators_WithEmptyRegion_ShouldReturnEmptyOutputs()
    {
        var emptyRegion = new Region();
        var erosion = new RegionErosionOperator(Substitute.For<ILogger<RegionErosionOperator>>());
        var dilation = new RegionDilationOperator(Substitute.For<ILogger<RegionDilationOperator>>());
        var opening = new RegionOpeningOperator(Substitute.For<ILogger<RegionOpeningOperator>>());
        var closing = new RegionClosingOperator(Substitute.For<ILogger<RegionClosingOperator>>());
        var skeleton = new RegionSkeletonOperator(Substitute.For<ILogger<RegionSkeletonOperator>>());

        var erosionResult = await erosion.ExecuteAsync(new Operator("RegionErosion", OperatorType.RegionErosion, 0, 0), new Dictionary<string, object> { ["Region"] = emptyRegion });
        var dilationResult = await dilation.ExecuteAsync(new Operator("RegionDilation", OperatorType.RegionDilation, 0, 0), new Dictionary<string, object> { ["Region"] = emptyRegion });
        var openingResult = await opening.ExecuteAsync(new Operator("RegionOpening", OperatorType.RegionOpening, 0, 0), new Dictionary<string, object> { ["Region"] = emptyRegion });
        var closingResult = await closing.ExecuteAsync(new Operator("RegionClosing", OperatorType.RegionClosing, 0, 0), new Dictionary<string, object> { ["Region"] = emptyRegion });
        var skeletonResult = await skeleton.ExecuteAsync(new Operator("RegionSkeleton", OperatorType.RegionSkeleton, 0, 0), new Dictionary<string, object> { ["Region"] = emptyRegion });

        erosionResult.IsSuccess.Should().BeTrue();
        dilationResult.IsSuccess.Should().BeTrue();
        openingResult.IsSuccess.Should().BeTrue();
        closingResult.IsSuccess.Should().BeTrue();
        skeletonResult.IsSuccess.Should().BeTrue();

        ((Region)erosionResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)dilationResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)openingResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)closingResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();
        ((Region)skeletonResult.OutputData!["Region"]).IsEmpty.Should().BeTrue();

        erosionResult.OutputData!["Area"].Should().Be(0);
        dilationResult.OutputData!["Area"].Should().Be(0);
        openingResult.OutputData!["Area"].Should().Be(0);
        closingResult.OutputData!["Area"].Should().Be(0);
        skeletonResult.OutputData!["SkeletonLength"].Should().Be(0);
        skeletonResult.OutputData!["Connectivity"].Should().Be(8);
    }

    [Fact]
    public async Task RegionErosion_WithKernelLargerThanRegion_ShouldReturnEmptyRegion()
    {
        var sut = new RegionErosionOperator(Substitute.For<ILogger<RegionErosionOperator>>());
        var region = CreateRectangleRegion(12, 12, 3, 3);
        var op = new Operator("RegionErosion", OperatorType.RegionErosion, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("KernelWidth", 9, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelHeight", 9, "int"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var eroded = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        eroded.IsEmpty.Should().BeTrue();
        eroded.Area.Should().Be(0);
    }

    [Fact]
    public async Task RegionOpening_WithKernelLargerThanRegion_ShouldReturnEmptyRegion()
    {
        var sut = new RegionOpeningOperator(Substitute.For<ILogger<RegionOpeningOperator>>());
        var region = CreateRectangleRegion(12, 12, 5, 5);
        var op = new Operator("RegionOpening", OperatorType.RegionOpening, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("KernelWidth", 11, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelHeight", 11, "int"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var opened = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        opened.IsEmpty.Should().BeTrue();
        opened.Area.Should().Be(0);
    }

    [Fact]
    public async Task RegionClosing_WithLargeKernel_ShouldBridgeSmallGap()
    {
        var sut = new RegionClosingOperator(Substitute.For<ILogger<RegionClosingOperator>>());
        var region = CreateSplitRectanglesRegion();
        var op = new Operator("RegionClosing", OperatorType.RegionClosing, 0, 0);
        op.Parameters.Add(TestHelpers.CreateParameter("KernelWidth", 5, "int"));
        op.Parameters.Add(TestHelpers.CreateParameter("KernelHeight", 5, "int"));

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var closed = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        closed.Area.Should().BeGreaterThan(region.Area);
        closed.ContainsPoint(15, 16).Should().BeTrue();
        CountConnectedComponents(closed, ConnectivityType.EightConnected).Should().Be(1);
    }

    [Fact]
    public async Task RegionSkeleton_WithThinLine_ShouldPreserveEndpointsAtTightBounds()
    {
        var sut = new RegionSkeletonOperator(Substitute.For<ILogger<RegionSkeletonOperator>>());
        var region = CreateThinHorizontalLineRegion();
        var op = new Operator("RegionSkeleton", OperatorType.RegionSkeleton, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var skeleton = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        skeleton.RunLengths.Should().Equal(region.RunLengths);
        result.OutputData!["SkeletonLength"].Should().Be(region.Area);
        result.OutputData!["EndPoints"].Should().Be(2);
        result.OutputData!["BranchPoints"].Should().Be(0);
        result.OutputData!["Connectivity"].Should().Be(8);
        result.OutputData!["Algorithm"].Should().Be("Zhang-Suen");
    }

    [Fact]
    public async Task RegionSkeleton_ShouldRemainEightConnectedForCrossShape()
    {
        var sut = new RegionSkeletonOperator(Substitute.For<ILogger<RegionSkeletonOperator>>());
        var region = CreateThickCrossRegion();
        var op = new Operator("RegionSkeleton", OperatorType.RegionSkeleton, 0, 0);

        var result = await sut.ExecuteAsync(op, new Dictionary<string, object> { ["Region"] = region });

        result.IsSuccess.Should().BeTrue();
        var skeleton = result.OutputData!["Region"].Should().BeOfType<Region>().Subject;
        CountConnectedComponents(skeleton, ConnectivityType.EightConnected).Should().Be(1);
        result.OutputData!["Connectivity"].Should().Be(8);
        result.OutputData!["BranchPoints"].Should().BeOfType<int>().Which.Should().BeGreaterThan(0);
    }

    private static Region CreateRectangleRegion(int x, int y, int width, int height, int imageWidth = 80, int imageHeight = 80)
    {
        using var mat = new Mat(imageHeight, imageWidth, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(x, y, width, height), Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateRegionWithNoise()
    {
        using var mat = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(18, 18, 28, 20), Scalar.White, -1);
        Cv2.Circle(mat, new Point(8, 8), 1, Scalar.White, -1);
        Cv2.Circle(mat, new Point(70, 12), 1, Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateRegionWithHole()
    {
        using var mat = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(15, 15, 36, 30), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(30, 26, 4, 4), Scalar.Black, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateThickCrossRegion()
    {
        using var mat = new Mat(80, 80, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(34, 12, 12, 50), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(18, 30, 44, 12), Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateOffsetThickCrossRegion()
    {
        using var mat = new Mat(120, 120, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(58, 32, 12, 58), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(38, 52, 52, 12), Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateSplitRectanglesRegion()
    {
        using var mat = new Mat(40, 40, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(8, 12, 6, 10), Scalar.White, -1);
        Cv2.Rectangle(mat, new Rect(16, 12, 6, 10), Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static Region CreateThinHorizontalLineRegion()
    {
        using var mat = new Mat(40, 40, MatType.CV_8UC1, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(10, 20, 21, 1), Scalar.White, -1);
        return Region.FromMat(mat);
    }

    private static int CountConnectedComponents(Region region, ConnectivityType connectivity)
    {
        if (region.IsEmpty)
        {
            return 0;
        }

        var remaining = new HashSet<(int X, int Y)>(EnumeratePoints(region));
        var neighborOffsets = connectivity == ConnectivityType.FourConnected
            ? new (int dx, int dy)[] { (0, -1), (-1, 0), (1, 0), (0, 1) }
            : new (int dx, int dy)[]
            {
                (-1, -1), (0, -1), (1, -1),
                (-1, 0),            (1, 0),
                (-1, 1),  (0, 1),  (1, 1)
            };

        var components = 0;
        var queue = new Queue<(int X, int Y)>();

        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            remaining.Remove(seed);
            queue.Enqueue(seed);
            components++;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var (dx, dy) in neighborOffsets)
                {
                    var neighbor = (current.X + dx, current.Y + dy);
                    if (remaining.Remove(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return components;
    }

    private static IEnumerable<(int X, int Y)> EnumeratePoints(Region region)
    {
        foreach (var run in region.RunLengths)
        {
            for (int x = run.StartX; x <= run.EndX; x++)
            {
                yield return (x, run.Y);
            }
        }
    }

    private static void DisposeOutputImage(Dictionary<string, object>? outputData)
    {
        if (outputData?.TryGetValue("Image", out var image) == true && image is ImageWrapper imageWrapper)
        {
            imageWrapper.Dispose();
        }
    }
}
