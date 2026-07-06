using System.Text.Json;
using ClearVision.Product.Infrastructure.Services;

namespace ClearVision.Product.Tests.Services;

public class PlanarScaleOffsetCalibrationServiceTests
{
    [Fact]
    public async Task SaveCalibrationAsync_WithSuccessfulResult_ShouldPersistCalibrationBundleV2()
    {
        var service = new PlanarScaleOffsetCalibrationService();
        var fileName = $"planar_scale_offset_v2_{Guid.NewGuid():N}.json";
        var tempFile = PlanarScaleOffsetCalibrationService.ResolveCalibrationSavePath(fileName);

        try
        {
            var result = new PlanarScaleOffsetCalibrationResult
            {
                Success = true,
                OriginX = 10.0,
                OriginY = 20.0,
                ScaleX = 0.02,
                ScaleY = 0.03,
                MeanErrorX = 0.01,
                MeanErrorY = 0.02,
                Rmse = 0.03,
                PointCount = 4
            };

            var saved = await service.SaveCalibrationAsync(result, fileName);
            Assert.True(saved);

            var json = await File.ReadAllTextAsync(tempFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("planarTransform2D", root.GetProperty("calibrationKind").GetString());
            Assert.Equal("scaleOffset", root.GetProperty("transformModel").GetString());
            Assert.True(root.GetProperty("quality").GetProperty("accepted").GetBoolean());
            Assert.Equal("PlanarScaleOffsetCalibrationService", root.GetProperty("producerOperator").GetString());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task SaveCalibrationAsync_WithAbsolutePath_ShouldReject()
    {
        var service = new PlanarScaleOffsetCalibrationService();
        var result = CreateAcceptedResult();
        var absolutePath = Path.Combine(Path.GetTempPath(), $"planar_scale_offset_v2_{Guid.NewGuid():N}.json");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCalibrationAsync(result, absolutePath));
        Assert.Contains("relative", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(absolutePath));
    }

    [Theory]
    [InlineData("..\\escape.json")]
    [InlineData("nested\\..\\..\\escape.json")]
    public async Task SaveCalibrationAsync_WithTraversalPath_ShouldReject(string fileName)
    {
        var service = new PlanarScaleOffsetCalibrationService();
        var result = CreateAcceptedResult();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCalibrationAsync(result, fileName));
        Assert.Contains("ClearVision calibration directory", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PlanarScaleOffsetCalibrationResult CreateAcceptedResult()
    {
        return new PlanarScaleOffsetCalibrationResult
        {
            Success = true,
            OriginX = 10.0,
            OriginY = 20.0,
            ScaleX = 0.02,
            ScaleY = 0.03,
            MeanErrorX = 0.01,
            MeanErrorY = 0.02,
            Rmse = 0.03,
            PointCount = 4
        };
    }
}
