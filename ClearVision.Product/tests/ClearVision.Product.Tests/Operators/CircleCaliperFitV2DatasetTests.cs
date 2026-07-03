using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClearVision.Product.Infrastructure.Operators;
using FluentAssertions;
using OpenCvSharp;

namespace ClearVision.Product.Tests.Operators;

public class CircleCaliperFitV2DatasetTests
{
    [Fact]
    public void Manifest_ShouldHaveStableIdentityVersionAndHash()
    {
        var manifestPath = DatasetPath("manifest.json");
        var hashPath = DatasetPath("manifest.sha256");

        var manifestBytes = File.ReadAllBytes(manifestPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        var expectedHash = File.ReadAllText(hashPath, Encoding.UTF8).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        actualHash.Should().Be(expectedHash);

        using var document = JsonDocument.Parse(manifestBytes);
        document.RootElement.GetProperty("datasetId").GetString().Should().Be("circle-caliper-fit-v2-synthetic-v1");
        document.RootElement.GetProperty("datasetVersion").GetString().Should().Be("1.0.0");
        document.RootElement.GetProperty("contractVersion").GetString().Should().Be(CircleCaliperFitV2Request.ContractVersionValue);
        document.RootElement.GetProperty("cases").GetArrayLength().Should().Be(16);
    }

    [Fact]
    public void ManifestCases_ShouldExecuteAgainstRealGeneratedPixels()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(DatasetPath("manifest.json")));
        var failures = new List<string>();

        foreach (var testCase in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            using var gray = GenerateCaseImage(testCase);
            var request = BuildRequest(testCase);
            var result = CircleCaliperFitV2Kernel.Fit(gray, request);
            var expected = testCase.GetProperty("expected");
            var caseId = testCase.GetProperty("id").GetString()!;

            var expectedSuccess = expected.GetProperty("success").GetBoolean();
            if (result.Success != expectedSuccess)
            {
                failures.Add($"{caseId}: expected success={expectedSuccess}, actual success={result.Success}, code={result.FailureCode}, message={result.FailureMessage}");
                continue;
            }

            result.ContractVersion.Should().Be(CircleCaliperFitV2Request.ContractVersionValue);
            if (expectedSuccess)
            {
                var centerTolerance = ReadDouble(expected, "centerTolerance", 1.0);
                var radiusTolerance = ReadDouble(expected, "radiusTolerance", 1.0);
                var expectedCenterX = ReadDouble(testCase, "centerX");
                var expectedCenterY = ReadDouble(testCase, "centerY");
                var expectedRadius = ReadDouble(testCase, "radius");

                if (Math.Abs(result.CenterX!.Value - expectedCenterX) > centerTolerance ||
                    Math.Abs(result.CenterY!.Value - expectedCenterY) > centerTolerance ||
                    Math.Abs(result.Radius!.Value - expectedRadius) > radiusTolerance ||
                    result.ResidualRmse > request.MaxResidualRmse ||
                    result.CoverageRatio < request.MinCoverageRatio ||
                    result.AngularCoverageDegrees < request.MinAngularCoverageDegrees)
                {
                    failures.Add(
                        $"{caseId}: center=({result.CenterX:F3},{result.CenterY:F3}) radius={result.Radius:F3} rmse={result.ResidualRmse:F3} coverage={result.CoverageRatio:F3}/{result.AngularCoverageDegrees:F1}");
                }
            }
            else
            {
                var expectedFailure = Enum.Parse<CircleCaliperFitV2FailureCode>(expected.GetProperty("failureCode").GetString()!);
                if (result.FailureCode != expectedFailure ||
                    result.CenterX.HasValue ||
                    result.CenterY.HasValue ||
                    result.Radius.HasValue)
                {
                    failures.Add($"{caseId}: expected failure {expectedFailure}, actual {result.FailureCode}, fakeCircle={result.CenterX.HasValue || result.Radius.HasValue}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    private static CircleCaliperFitV2Request BuildRequest(JsonElement testCase)
    {
        var radius = ReadDouble(testCase, "radius");
        return new CircleCaliperFitV2Request
        {
            SearchCenterX = ReadDouble(testCase, "centerX"),
            SearchCenterY = ReadDouble(testCase, "centerY"),
            MinRadius = ReadDouble(testCase, "minRadius", radius - 8.0),
            MaxRadius = ReadDouble(testCase, "maxRadius", radius + 8.0),
            NominalRadius = radius,
            CaliperCount = ReadInt(testCase, "caliperCount", 96),
            AveragingThickness = ReadDouble(testCase, "averagingThickness", 5.0),
            ProfileSampleCount = ReadInt(testCase, "profileSampleCount", 129),
            GaussianSigma = ReadDouble(testCase, "gaussianSigma", 1.2),
            EdgePolarity = Enum.Parse<CircleCaliperFitV2EdgePolarity>(ReadString(testCase, "edgePolarity", "Auto")),
            EdgeThreshold = ReadDouble(testCase, "edgeThreshold", 0.0),
            MinEdgeStrength = ReadDouble(testCase, "minEdgeStrength", 4.0),
            MinValidCalipers = ReadInt(testCase, "minValidCalipers", 28),
            MinCoverageRatio = ReadDouble(testCase, "minCoverageRatio", 0.35),
            MinAngularCoverageDegrees = ReadDouble(testCase, "minAngularCoverageDegrees", 180.0),
            OutlierMode = Enum.Parse<CircleCaliperFitV2OutlierMode>(ReadString(testCase, "outlierMode", "Mad")),
            OutlierThreshold = ReadDouble(testCase, "outlierThreshold", 3.5),
            MaxOutlierIterations = ReadInt(testCase, "maxOutlierIterations", 3),
            MaxResidualRmse = ReadDouble(testCase, "maxResidualRmse", 1.4)
        };
    }

    private static Mat GenerateCaseImage(JsonElement testCase)
    {
        var scene = testCase.GetProperty("scene").GetString()!;
        var width = ReadInt(testCase, "width");
        var height = ReadInt(testCase, "height");
        var background = (byte)ReadInt(testCase, "background");
        var foreground = (byte)ReadInt(testCase, "foreground");
        var centerX = ReadDouble(testCase, "centerX");
        var centerY = ReadDouble(testCase, "centerY");
        var radius = ReadDouble(testCase, "radius");

        return scene switch
        {
            "filled_circle" => CreateFilledCircleImage(width, height, centerX, centerY, radius, background, foreground),
            "gradient_circle" => CreateGradientCircleImage(width, height, centerX, centerY, radius, background, foreground),
            "glare_circle" => CreateGlareCircleImage(width, height, centerX, centerY, radius, background, foreground),
            "gap_circle" => CreateGapCircleImage(width, height, centerX, centerY, radius, background, foreground, ReadDouble(testCase, "gapStartDegrees"), ReadDouble(testCase, "gapEndDegrees")),
            "multi_circle_clear" => CreateMultiCircleClearImage(width, height, centerX, centerY, radius, background, foreground),
            "concentric_rings" => CreateConcentricRingsImage(width, height, centerX, centerY, background, foreground),
            "blank" => new Mat(height, width, MatType.CV_8UC1, new Scalar(background)),
            "filled_ellipse" => CreateFilledEllipseImage(width, height, centerX, centerY, radius, background, foreground),
            "random_noise" => CreateRandomNoiseImage(width, height, ReadInt(testCase, "seed")),
            _ => throw new InvalidOperationException($"Unknown scene '{scene}'.")
        };
    }

    private static Mat CreateFilledCircleImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground, int supersample = 8)
    {
        var scale = Math.Max(2, supersample);
        using var hiRes = new Mat(height * scale, width * scale, MatType.CV_8UC1, new Scalar(background));
        Cv2.Circle(
            hiRes,
            new Point((int)Math.Round(centerX * scale), (int)Math.Round(centerY * scale)),
            Math.Max(1, (int)Math.Round(radius * scale)),
            new Scalar(foreground),
            -1,
            LineTypes.AntiAlias);
        var lowRes = new Mat();
        Cv2.Resize(hiRes, lowRes, new Size(width, height), 0, 0, InterpolationFlags.Area);
        return lowRes;
    }

    private static Mat CreateGradientCircleImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground)
    {
        var gray = CreateFilledCircleImage(width, height, centerX, centerY, radius, background, foreground);
        for (var y = 0; y < gray.Height; y++)
        {
            for (var x = 0; x < gray.Width; x++)
            {
                var gradient = (x * 28 / Math.Max(gray.Width - 1, 1)) + (y * 18 / Math.Max(gray.Height - 1, 1));
                gray.Set(y, x, (byte)Math.Clamp(gray.At<byte>(y, x) + gradient, 0, 255));
            }
        }

        return gray;
    }

    private static Mat CreateGlareCircleImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground)
    {
        var gray = CreateFilledCircleImage(width, height, centerX, centerY, radius, background, foreground);
        Cv2.Circle(gray, new Point((int)Math.Round(centerX + radius + 3), (int)Math.Round(centerY - 10)), 10, new Scalar(foreground), -1, LineTypes.AntiAlias);
        EraseSector(gray, centerX, centerY, radius + 18, 220, 246, background);
        return gray;
    }

    private static Mat CreateGapCircleImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground, double startDegrees, double endDegrees)
    {
        var gray = CreateFilledCircleImage(width, height, centerX, centerY, radius, background, foreground);
        EraseSector(gray, centerX, centerY, radius + 18, startDegrees, endDegrees, background);
        return gray;
    }

    private static Mat CreateMultiCircleClearImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground)
    {
        var gray = new Mat(height, width, MatType.CV_8UC1, new Scalar(background));
        Cv2.Circle(gray, new Point(105, 122), 36, new Scalar(foreground), -1, LineTypes.AntiAlias);
        Cv2.Circle(gray, new Point((int)Math.Round(centerX), (int)Math.Round(centerY)), (int)Math.Round(radius), new Scalar(foreground), -1, LineTypes.AntiAlias);
        return gray;
    }

    private static Mat CreateConcentricRingsImage(int width, int height, double centerX, double centerY, byte background, byte foreground)
    {
        var gray = new Mat(height, width, MatType.CV_8UC1, new Scalar(background));
        var center = new Point((int)Math.Round(centerX), (int)Math.Round(centerY));
        Cv2.Circle(gray, center, 50, new Scalar(foreground), 4, LineTypes.AntiAlias);
        Cv2.Circle(gray, center, 60, new Scalar(foreground), 4, LineTypes.AntiAlias);
        return gray;
    }

    private static Mat CreateFilledEllipseImage(int width, int height, double centerX, double centerY, double radius, byte background, byte foreground)
    {
        var gray = new Mat(height, width, MatType.CV_8UC1, new Scalar(background));
        Cv2.Ellipse(
            gray,
            new RotatedRect(
                new Point2f((float)centerX, (float)centerY),
                new Size2f((float)(radius * 2.5), (float)(radius * 1.45)),
                0),
            new Scalar(foreground),
            -1,
            LineTypes.AntiAlias);
        return gray;
    }

    private static Mat CreateRandomNoiseImage(int width, int height, int seed)
    {
        var gray = new Mat(height, width, MatType.CV_8UC1, Scalar.Black);
        var random = new Random(seed);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                gray.Set(y, x, (byte)random.Next(96, 160));
            }
        }

        return gray;
    }

    private static void EraseSector(Mat gray, double centerX, double centerY, double radius, double startDegrees, double endDegrees, byte color)
    {
        var points = new List<Point> { new((int)Math.Round(centerX), (int)Math.Round(centerY)) };
        for (var angle = startDegrees; angle <= endDegrees; angle += 3.0)
        {
            var radians = angle * Math.PI / 180.0;
            points.Add(new Point(
                (int)Math.Round(centerX + (Math.Cos(radians) * radius)),
                (int)Math.Round(centerY + (Math.Sin(radians) * radius))));
        }

        Cv2.FillConvexPoly(gray, points, new Scalar(color), LineTypes.AntiAlias);
    }

    private static string DatasetPath(string fileName)
    {
        return Path.Combine(FindRepoRoot(), "quality", "datasets", "circle-caliper-fit-v2", fileName);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "ClearVision.Product")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string ReadString(JsonElement element, string name, string defaultValue)
    {
        return element.TryGetProperty(name, out var property) ? property.GetString() ?? defaultValue : defaultValue;
    }

    private static int ReadInt(JsonElement element, string name, int defaultValue = 0)
    {
        return element.TryGetProperty(name, out var property) ? property.GetInt32() : defaultValue;
    }

    private static double ReadDouble(JsonElement element, string name, double defaultValue = 0.0)
    {
        return element.TryGetProperty(name, out var property) ? property.GetDouble() : defaultValue;
    }
}
