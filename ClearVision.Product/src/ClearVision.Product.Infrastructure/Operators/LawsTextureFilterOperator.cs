using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.ImageProcessing;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "Laws纹理滤波",
    Description = "应用 5x5 Laws 纹理滤波并计算局部能量。",
    CategoryId = OperatorCategoryId.FeatureExtraction,
    IconName = "texture",
    Keywords = new[] { "Texture", "Laws", "Energy", "Filter", "GLCM" },
    Version = "1.0.1"
)]
[AlgorithmInfo(
    Name = "Laws 5x5 texture energy filtering",
    CoreApi = "LawsTextureFilter.Apply -> OpenCV Filter2D -> LawsTextureFilter.ComputeEnergy -> local mean squared response",
    ImplementationStrategy = "Converts the image to normalized gray float data, optionally subtracts local mean illumination, applies the configured 5x5 separable Laws kernel pair, and computes a local energy image from squared filter response.",
    TimeComplexity = "O(W*H*(K^2+M^2+E^2))",
    TypicalLatency = "No dedicated golden benchmark yet; covered by texture unit and integration tests",
    SpaceComplexity = "O(W*H)",
    SuitableUseCases = new[] { "Highlighting local texture energy for material, surface, or defect pre-screening.", "Comparing fixed Laws kernel responses such as E5E5, E5L5, S5S5, W5W5, and R5R5." },
    UnsuitableUseCases = new[] { "Semantic texture classification without downstream thresholds or model features.", "Images whose illumination drift cannot be corrected by local mean subtraction alone." },
    KnownLimitations = new[] { "Kernel combo must use the classic L/E/S/W/R 5-tap Laws codes.", "Output energy depends on the selected window size and is not normalized across unrelated acquisition setups." },
    Dependencies = new[] { "OpenCvSharp" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("FilteredImage", "Filtered Image", PortDataType.Image)]
[OutputPort("EnergyImage", "Energy Image", PortDataType.Image)]
[OutputPort("MeanEnergy", "Mean Energy", PortDataType.Float)]
[OperatorParam("KernelCombo", "Kernel Combo", "string", DefaultValue = "E5E5")]
[OperatorParam("SubtractLocalMean", "Subtract Local Mean", "bool", DefaultValue = true)]
[OperatorParam("LocalMeanWindowSize", "Local Mean Window Size", "int", DefaultValue = 15, Min = 3, Max = 101)]
[OperatorParam("EnergyWindowSize", "Energy Window Size", "int", DefaultValue = 15, Min = 3, Max = 101)]
[OperatorParam(
    "BorderType",
    "Border Type",
    "enum",
    DefaultValue = "1",
    Options = new[] { "1|Replicate", "2|Reflect", "4|Default" }
)]
public sealed class LawsTextureFilterOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.LawsTextureFilter;

    public LawsTextureFilterOperator(ILogger<LawsTextureFilterOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required"));
        }

        var src = imageWrapper.MatReadOnly;
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid"));
        }

        var kernelCombo = GetStringParam(@operator, "KernelCombo", "E5E5");
        var subtractLocalMean = GetBoolParam(@operator, "SubtractLocalMean", true);
        var localMeanWindow = GetIntParam(@operator, "LocalMeanWindowSize", 15, min: 3, max: 101);
        var energyWindow = GetIntParam(@operator, "EnergyWindowSize", 15, min: 3, max: 101);
        var borderType = GetIntParam(@operator, "BorderType", 1, min: 0, max: 7);

        var border = borderType switch
        {
            2 => BorderTypes.Reflect,
            4 => BorderTypes.Default,
            _ => BorderTypes.Replicate
        };

        Mat filtered;
        Mat energy;
        try
        {
            filtered = LawsTextureFilter.Apply(
                src,
                kernelCombo: kernelCombo,
                subtractLocalMean: subtractLocalMean,
                localMeanWindowSize: localMeanWindow,
                borderType: border);

            energy = LawsTextureFilter.ComputeEnergy(filtered, windowSize: energyWindow, borderType: border);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Laws texture filtering failed.");
            return Task.FromResult(OperatorExecutionOutput.Failure($"Laws texture filtering failed: {ex.Message}"));
        }

        var meanEnergy = Cv2.Mean(energy).Val0;

        var output = new Dictionary<string, object>
        {
            ["FilteredImage"] = new ImageWrapper(filtered),
            ["EnergyImage"] = new ImageWrapper(energy),
            ["MeanEnergy"] = meanEnergy
        };

        return Task.FromResult(OperatorExecutionOutput.Success(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelCombo = GetStringParam(@operator, "KernelCombo", "E5E5");
        if (!IsValidKernelCombo(kernelCombo))
        {
            return ValidationResult.Invalid("KernelCombo must look like E5L5 / R5R5 and only use {L,E,S,W,R}.");
        }

        var localMeanWindow = GetIntParam(@operator, "LocalMeanWindowSize", 15, min: 3, max: 101);
        var energyWindow = GetIntParam(@operator, "EnergyWindowSize", 15, min: 3, max: 101);
        if (localMeanWindow < 3 || energyWindow < 3)
        {
            return ValidationResult.Invalid("Window size must be >= 3.");
        }

        return ValidationResult.Valid();
    }

    private static bool IsValidKernelCombo(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;
        s = s.Trim().ToUpperInvariant();
        if (s.Length != 4 || s[1] != '5' || s[3] != '5')
            return false;
        return IsValidKernelCode(s[0]) && IsValidKernelCode(s[2]);
    }

    private static bool IsValidKernelCode(char c)
    {
        return c is 'L' or 'E' or 'S' or 'W' or 'R';
    }
}
