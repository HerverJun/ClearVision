// MorphologyOperator.cs
// 形态学算子
// 提供标准形态学处理入口与参数映射
// 作者：蘅芜君
using System.Threading;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 旧版图像形态学算子，仅用于兼容既有流程。
/// 新流程应优先使用 <see cref="MorphologicalOperationOperator"/>。
/// </summary>
[OperatorMeta(
    DisplayName = "形态学（旧版）",
    Description = "旧版图像形态学节点；新建图像流程请使用“形态学操作”，区域流程请使用 Region* 系列算子。",
    Category = "预处理",
    IconName = "morphology",
    Keywords = new[] { "形态学", "腐蚀", "膨胀", "开运算", "闭运算", "旧版", "Morphology", "Legacy" },
    Tags = new[] { "legacy", "deprecated", "compatibility", "image-only" }
)]
[InputPort("Image", "Image", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "Image", PortDataType.Image)]
[OperatorParam("Operation", "Operation", "string", DefaultValue = "Erode")]
[OperatorParam("KernelSize", "Kernel Size", "int", DefaultValue = 3, Min = 1, Max = 51)]
[OperatorParam("KernelShape", "Kernel Shape", "string", DefaultValue = "Rect")]
[OperatorParam("Iterations", "Iterations", "int", DefaultValue = 1, Min = 1, Max = 10)]
[OperatorParam("AnchorX", "Anchor X", "int", DefaultValue = -1)]
[OperatorParam("AnchorY", "Anchor Y", "int", DefaultValue = -1)]
public class MorphologyOperator : OperatorBase
{
    private static int _legacyWarningLogged;

    public override OperatorType OperatorType => OperatorType.Morphology;

    public MorphologyOperator(ILogger<MorphologyOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (!TryGetInputImage(inputs, "Image", out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is required."));
        }

        LogLegacyUsageOnce();

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Input image is invalid."));
        }

        var operation = GetStringParam(@operator, "Operation", "Erode");
        var kernelSize = GetIntParam(@operator, "KernelSize", 3, min: 1, max: 51);
        var kernelShape = GetStringParam(@operator, "KernelShape", "Rect");
        var iterations = GetIntParam(@operator, "Iterations", 1, min: 1, max: 10);
        var anchorX = GetIntParam(@operator, "AnchorX", -1);
        var anchorY = GetIntParam(@operator, "AnchorY", -1);

        var dst = MorphologyExecutionHelper.Execute(
            src,
            operation,
            kernelShape,
            kernelSize,
            kernelSize,
            iterations,
            anchorX,
            anchorY);

        return Task.FromResult(OperatorExecutionOutput.Success(CreateImageOutput(dst, new Dictionary<string, object>
        {
            { "Operation", operation },
            { "KernelShape", kernelShape },
            { "KernelSize", $"{kernelSize}x{kernelSize}" },
            { "Iterations", iterations },
            { "LegacyCompatible", true }
        })));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var kernelSize = GetIntParam(@operator, "KernelSize", 3);
        if (kernelSize < 1 || kernelSize > 51)
        {
            return ValidationResult.Invalid("KernelSize must be between 1 and 51.");
        }

        var iterations = GetIntParam(@operator, "Iterations", 1);
        if (iterations < 1 || iterations > 10)
        {
            return ValidationResult.Invalid("Iterations must be between 1 and 10.");
        }

        var operation = GetStringParam(@operator, "Operation", "Erode");
        if (!MorphologyExecutionHelper.IsValidOperation(operation))
        {
            return ValidationResult.Invalid($"Unsupported operation: {operation}");
        }

        var kernelShape = GetStringParam(@operator, "KernelShape", "Rect");
        if (!MorphologyExecutionHelper.IsValidShape(kernelShape))
        {
            return ValidationResult.Invalid($"Unsupported kernel shape: {kernelShape}");
        }

        return ValidationResult.Valid();
    }

    private void LogLegacyUsageOnce()
    {
        if (Interlocked.Exchange(ref _legacyWarningLogged, 1) == 0)
        {
            Logger.LogWarning(
                "[MorphologyOperator] Legacy node is kept for compatibility. Prefer MorphologicalOperationOperator for new workflows.");
        }
    }
}
