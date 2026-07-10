using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public sealed class DefaultFallbackGovernanceTests
{
    [Theory]
    [InlineData(null, 180)]
    [InlineData(15, 15)]
    public async Task PyramidShapeMatch_AngleRange_ShouldUseMetadataDefaultUnlessExplicit(
        int? explicitAngleRange,
        int expectedAngleRange)
    {
        using var scene = CreateTemplateImage();
        using var template = scene.Clone();
        var op = new Operator("pyramid-match", OperatorType.PyramidShapeMatch, 0, 0);
        if (explicitAngleRange.HasValue)
        {
            op.AddParameter(TestHelpers.CreateParameter("AngleRange", explicitAngleRange.Value, "int"));
        }

        var sut = new PyramidShapeMatchOperator(Substitute.For<ILogger<PyramidShapeMatchOperator>>());
        var result = await sut.ExecuteAsync(op, new Dictionary<string, object>
        {
            ["Image"] = new ImageWrapper(scene.Clone()),
            ["Template"] = new ImageWrapper(template.Clone())
        });

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        var matcherConfig = result.OutputData!["MatcherConfig"]
            .Should().BeAssignableTo<IReadOnlyDictionary<string, object>>().Subject;
        matcherConfig["AngleRange"].Should().Be(expectedAngleRange);
        (result.OutputData["Image"] as ImageWrapper)?.Release();
    }

    private static Mat CreateTemplateImage()
    {
        var image = new Mat(120, 120, MatType.CV_8UC3, Scalar.All(255));
        Cv2.Rectangle(image, new Point(20, 20), new Point(100, 100), Scalar.All(0), 3);
        Cv2.Line(image, new Point(60, 20), new Point(60, 100), Scalar.All(0), 2);
        Cv2.Line(image, new Point(20, 60), new Point(100, 60), Scalar.All(0), 2);
        Cv2.Circle(image, new Point(60, 60), 18, Scalar.All(0), 2);
        return image;
    }
}
