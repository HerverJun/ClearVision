using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.ValueObjects;
using ClearVision.Product.Infrastructure.Operators;
using ClearVision.Product.Tests.Operators;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Integration;

[TestClassification(TestDomain.Matching, TestPurpose.Stability, TestLane.Nightly, TestEvidenceType.StatisticalDistribution, TestOracleType.Metamorphic, TestResourceRequirement.None, TestExpectedDuration.Medium, TestFlakyPolicy.Blocking, "operator-quality", SeedControl = "Fixed: Cv2.SetTheRNG(20260715) is called for each photometric perturbation")]
public sealed class MatchingStabilityTests
{
    [Fact]
    public async Task TemplateMatching_AcrossBrightnessPerturbations_ShouldKeepLocationStable()
    {
        var executor = new TemplateMatchOperator(NullLogger<TemplateMatchOperator>.Instance);
        var op = new Operator("template-stability", OperatorType.TemplateMatching, 0, 0);
        op.AddParameter(TestHelpers.CreateParameter("Domain", "Gradient", "string"));
        op.AddParameter(TestHelpers.CreateParameter("Threshold", 0.45, "double"));
        using var template = CreatePatternTemplate();
        var positions = new List<Position>();
        var scores = new List<double>();

        foreach (var brightnessOffset in new[] { 0.0, 15.0, 30.0, 45.0 })
        {
            Cv2.SetTheRNG(20260715UL);
            using var scene = new Mat(180, 180, MatType.CV_8UC3, new Scalar(25, 25, 25));
            using var perturbed = new Mat();
            template.MatReadOnly.ConvertTo(perturbed, template.MatReadOnly.Type(), 1.0, brightnessOffset);
            using (var roi = new Mat(scene, new Rect(72, 64, perturbed.Width, perturbed.Height)))
            {
                perturbed.CopyTo(roi);
            }

            var result = await executor.ExecuteAsync(op, new Dictionary<string, object>
            {
                ["Image"] = scene.ToBytes(".png"),
                ["Template"] = template.MatReadOnly.ToBytes(".png")
            });

            result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            result.OutputData!["IsMatch"].Should().Be(true);
            positions.Add(result.OutputData["Position"].Should().BeOfType<Position>().Subject);
            scores.Add(Convert.ToDouble(result.OutputData["Score"]));
            (result.OutputData["Image"] as ImageWrapper)?.Dispose();
        }

        positions.Select(position => position.X).Max().Should().BeApproximately(positions[0].X, 1e-6);
        positions.Select(position => position.X).Min().Should().BeApproximately(positions[0].X, 1e-6);
        positions.Select(position => position.Y).Max().Should().BeApproximately(positions[0].Y, 1e-6);
        positions.Select(position => position.Y).Min().Should().BeApproximately(positions[0].Y, 1e-6);
        scores.Should().OnlyContain(score => score > 0.45);
    }

    private static ImageWrapper CreatePatternTemplate()
    {
        var mat = new Mat(48, 48, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(mat, new Rect(4, 4, 40, 40), Scalar.White, -1);
        Cv2.Line(mat, new Point(4, 24), new Point(44, 24), Scalar.Black, 2);
        Cv2.Line(mat, new Point(24, 4), new Point(24, 44), Scalar.Black, 2);
        Cv2.Circle(mat, new Point(15, 15), 5, Scalar.Black, -1);
        return new ImageWrapper(mat);
    }
}
