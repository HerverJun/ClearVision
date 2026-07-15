using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "帧变化触发",
    Description = "通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空帧进入深度学习。",
    CategoryId = OperatorCategoryId.FlowControl,
    IconName = "activity",
    Keywords = new[] { "触发", "到料", "帧差", "视频流", "连续采集", "软触发", "去重", "冷却", "trigger", "arrival", "frame change", "continuous", "video" }
)]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "输出图像", PortDataType.Image)]
[OutputPort("Triggered", "是否触发", PortDataType.Boolean)]
[OutputPort("ChangeScore", "变化比例", PortDataType.Float)]
[OutputPort("ChangedPixels", "变化像素数", PortDataType.Integer)]
[OutputPort("Reason", "判定原因", PortDataType.String)]
[OutputPort("BaselineReady", "基线已建立", PortDataType.Boolean)]
[OutputPort("TotalPixels", "有效像素数", PortDataType.Integer)]
[OutputPort("CooldownRemainingMs", "剩余冷却时间(ms)", PortDataType.Integer)]
[OutputPort("EffectivePixelThreshold", "有效像素差阈值", PortDataType.Integer)]
[OutputPort("EffectiveMinChangeRatio", "有效最小变化比例", PortDataType.Float)]
[OperatorParam("Enabled", "启用检测", "bool", Description = "关闭后图像直接放行，不做帧差判断。", DefaultValue = true)]
[OperatorParam("ShortCircuitWhenNotTriggered", "未触发时跳过本轮", "bool", Description = "开启后，未检测到到料变化时短路当前流程，不执行后续 YOLO 和结果输出。", DefaultValue = true)]
[OperatorParam("Profile", "参数配置档", "enum", Description = "默认 line_fast_default；line_noise_guard 和 line_low_contrast 必须作为证据 profile 显式启用。", DefaultValue = "line_fast_default", Options = new[] { "line_fast_default", "line_noise_guard", "line_low_contrast" })]
[OperatorParam("PixelThreshold", "像素差阈值", "int", Description = "单个像素灰度差超过该值才计入变化区域。现场反光/抖动多时可适当调高。", DefaultValue = 30, Min = 1, Max = 255)]
[OperatorParam("MinChangeRatio", "最小变化比例", "double", Description = "ROI 内变化像素占比达到该值才认为到料。误触发多时调高，漏检时调低。", DefaultValue = 0.02, Min = 0.0, Max = 1.0)]
[OperatorParam("MinChangePixels", "最小变化像素数", "int", Description = "ROI 内变化像素数量下限，用于过滤小面积噪声。", DefaultValue = 500, Min = 0)]
[OperatorParam("CooldownMs", "冷却时间(ms)", "int", Description = "触发后在该时间内抑制重复触发，防止同一端子停留期间重复判定。", DefaultValue = 1200, Min = 0, Max = 60000)]
[OperatorParam("RoiX", "检测区域X", "int", Description = "到料检测 ROI 左上角 X。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiY", "检测区域Y", "int", Description = "到料检测 ROI 左上角 Y。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiW", "检测区域宽度", "int", Description = "到料检测 ROI 宽度；0 表示从 X 到图像右边界。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiH", "检测区域高度", "int", Description = "到料检测 ROI 高度；0 表示从 Y 到图像下边界。", DefaultValue = 0, Min = 0)]
[OperatorParam("BlurSize", "降噪模糊核", "int", Description = "0 表示关闭；开启时必须为 3 到 15 的奇数。", DefaultValue = 0, Min = 0, Max = 15)]
[OperatorParam("MorphOpenSize", "开运算核", "int", Description = "0 表示关闭；开启时必须为 3 到 15 的奇数，用于去除孤立噪声。", DefaultValue = 0, Min = 0, Max = 15)]
[OperatorParam("NormalizeMode", "亮度归一化", "enum", Description = "None、MeanShift 或 PercentileClip。", DefaultValue = "None", Options = new[] { "None", "MeanShift", "PercentileClip" })]
[OperatorParam("ReferenceUpdateMode", "参考帧更新", "enum", Description = "PreviousFrame、StableBackground 或 ExponentialMovingAverage。", DefaultValue = "PreviousFrame", Options = new[] { "PreviousFrame", "StableBackground", "ExponentialMovingAverage" })]
[OperatorParam("ReferenceUpdateAlpha", "参考更新系数", "double", Description = "仅 ExponentialMovingAverage 使用，范围 0 到 1。", DefaultValue = 0.05, Min = 0.0, Max = 1.0)]
[OperatorParam("AdaptivePixelThreshold", "自适应像素阈值", "bool", Description = "默认关闭；低对比 evidence profile 可显式启用。", DefaultValue = false)]
[OperatorParam("MinConsecutiveChangedFrames", "连续变化帧数", "int", Description = "至少连续多少帧达到变化阈值才触发，用于抑制单帧闪烁。", DefaultValue = 1, Min = 1)]
[OperatorParam("ResetAfterNoChangeFrames", "无变化复位帧数", "int", Description = "连续无变化达到该帧数后复位边沿状态；0 表示关闭。", DefaultValue = 1, Min = 0)]
[OperatorParam("TriggerOnRisingEdgeOnly", "仅上升沿触发", "bool", Description = "开启后持续变化只在进入变化状态的边沿触发一次。", DefaultValue = true)]
public sealed class FrameChangeTriggerOperator : OperatorBase, IDisposable
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, FrameChangeState> _states = new();
    private readonly object _cleanupSync = new();
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    public override OperatorType OperatorType => OperatorType.FrameChangeTrigger;

    public FrameChangeTriggerOperator(ILogger<FrameChangeTriggerOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var validation = ValidateParameters(@operator);
        if (!validation.IsValid)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(
                "FrameChangeTrigger parameter invalid: " + string.Join("; ", validation.Errors)));
        }

        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("必须提供输入图像。"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("输入图像为空。"));
        }

        var enabled = ReadBoolParam(@operator, "Enabled", true);
        var options = CreateOptions(@operator);
        var roi = ResolveRoi(@operator, src);
        if (!enabled)
        {
            var totalPixels = Math.Max(1, roi.Width * roi.Height);
            var disabledDecision = new FrameChangeTriggerDecision(
                Triggered: true,
                ChangeScore: 0.0,
                ChangedPixels: 0,
                Reason: "disabled",
                BaselineReady: false,
                TotalPixels: totalPixels,
                CooldownRemainingMs: 0,
                EffectivePixelThreshold: options.PixelThreshold,
                EffectiveMinChangeRatio: options.MinChangeRatio,
                ConsecutiveChangedFrames: 0,
                NoChangeFrames: 0);
            return Task.FromResult(OperatorExecutionOutput.Success(
                CreatePassThroughOutput(src, disabledDecision, roi, @operator.Id)));
        }

        var shortCircuitWhenNotTriggered = ReadBoolParam(@operator, "ShortCircuitWhenNotTriggered", true);
        var nowUtc = DateTime.UtcNow;
        var state = _states.GetOrAdd(@operator.Id, static _ => new FrameChangeState());
        FrameChangeTriggerDecision decision;

        using var grayRoi = FrameChangeTriggerKernel.BuildGrayRoi(src, roi, options);
        lock (state.SyncRoot)
        {
            decision = FrameChangeTriggerKernel.Evaluate(state.KernelState, grayRoi, options, nowUtc);
            state.LastTouchedUtc = nowUtc;
        }

        TryCleanupStaleStates(nowUtc);

        var output = CreatePassThroughOutput(src, decision, roi, @operator.Id);

        return Task.FromResult(
            decision.Triggered || !shortCircuitWhenNotTriggered
                ? OperatorExecutionOutput.Success(output)
                : OperatorExecutionOutput.ShortCircuit(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var errors = new List<string>();

        ValidateBool(@operator, "Enabled", errors);
        ValidateBool(@operator, "ShortCircuitWhenNotTriggered", errors);
        ValidateBool(@operator, "AdaptivePixelThreshold", errors);
        ValidateBool(@operator, "TriggerOnRisingEdgeOnly", errors);
        ValidateProfile(@operator, errors);
        ValidateEnum<FrameChangeNormalizeMode>(@operator, "NormalizeMode", errors);
        ValidateEnum<FrameChangeReferenceUpdateMode>(@operator, "ReferenceUpdateMode", errors);

        ValidateIntRange(@operator, "PixelThreshold", 30, 1, 255, errors);
        ValidateDoubleRange(@operator, "MinChangeRatio", 0.02, 0.0, 1.0, errors);
        ValidateIntRange(@operator, "MinChangePixels", 500, 0, null, errors);
        ValidateIntRange(@operator, "CooldownMs", 1200, 0, 60_000, errors);
        ValidateIntRange(@operator, "RoiX", 0, 0, null, errors);
        ValidateIntRange(@operator, "RoiY", 0, 0, null, errors);
        ValidateIntRange(@operator, "RoiW", 0, 0, null, errors);
        ValidateIntRange(@operator, "RoiH", 0, 0, null, errors);
        ValidateOddKernel(@operator, "BlurSize", errors);
        ValidateOddKernel(@operator, "MorphOpenSize", errors);
        ValidateDoubleRange(@operator, "ReferenceUpdateAlpha", 0.05, 0.0, 1.0, errors);
        ValidateIntRange(@operator, "MinConsecutiveChangedFrames", 1, 1, null, errors);
        ValidateIntRange(@operator, "ResetAfterNoChangeFrames", 1, 0, null, errors);

        if (errors.Count == 0)
        {
            return ValidationResult.Valid();
        }

        return new ValidationResult
        {
            IsValid = false,
            Errors = errors
        };
    }

    public void Dispose()
    {
        foreach (var state in _states.Values)
        {
            lock (state.SyncRoot)
            {
                state.Clear();
            }
        }

        _states.Clear();
    }

    private static FrameChangeTriggerOptions CreateOptions(Operator @operator)
    {
        var profile = ReadStringParam(@operator, "Profile", "line_fast_default").Trim();
        var defaults = profile.ToLowerInvariant() switch
        {
            "line_noise_guard" => FrameChangeTriggerOptions.LineNoiseGuard,
            "line_low_contrast" => FrameChangeTriggerOptions.LineLowContrast,
            _ => FrameChangeTriggerOptions.LineFastDefault
        };

        return defaults with
        {
            PixelThreshold = ReadIntParam(@operator, "PixelThreshold", defaults.PixelThreshold),
            MinChangeRatio = ReadDoubleParam(@operator, "MinChangeRatio", defaults.MinChangeRatio),
            MinChangePixels = ReadIntParam(@operator, "MinChangePixels", defaults.MinChangePixels),
            CooldownMs = ReadIntParam(@operator, "CooldownMs", defaults.CooldownMs),
            BlurSize = ReadIntParam(@operator, "BlurSize", defaults.BlurSize),
            MorphOpenSize = ReadIntParam(@operator, "MorphOpenSize", defaults.MorphOpenSize),
            NormalizeMode = ReadEnumParam(@operator, "NormalizeMode", defaults.NormalizeMode),
            ReferenceUpdateMode = ReadEnumParam(@operator, "ReferenceUpdateMode", defaults.ReferenceUpdateMode),
            ReferenceUpdateAlpha = ReadDoubleParam(@operator, "ReferenceUpdateAlpha", defaults.ReferenceUpdateAlpha),
            AdaptivePixelThreshold = ReadBoolParam(@operator, "AdaptivePixelThreshold", defaults.AdaptivePixelThreshold),
            MinConsecutiveChangedFrames = ReadIntParam(@operator, "MinConsecutiveChangedFrames", defaults.MinConsecutiveChangedFrames),
            ResetAfterNoChangeFrames = ReadIntParam(@operator, "ResetAfterNoChangeFrames", defaults.ResetAfterNoChangeFrames),
            TriggerOnRisingEdgeOnly = ReadBoolParam(@operator, "TriggerOnRisingEdgeOnly", defaults.TriggerOnRisingEdgeOnly)
        };
    }

    private Dictionary<string, object> CreatePassThroughOutput(
        Mat src,
        FrameChangeTriggerDecision decision,
        Rect roi,
        Guid operatorId)
    {
        var output = CreateImageOutput(src.Clone(), new Dictionary<string, object>
        {
            ["Triggered"] = decision.Triggered,
            ["ChangeScore"] = decision.ChangeScore,
            ["ChangedPixels"] = decision.ChangedPixels,
            ["Reason"] = decision.Reason,
            ["BaselineReady"] = decision.BaselineReady,
            ["TotalPixels"] = decision.TotalPixels,
            ["CooldownRemainingMs"] = decision.CooldownRemainingMs,
            ["EffectivePixelThreshold"] = decision.EffectivePixelThreshold,
            ["EffectiveMinChangeRatio"] = decision.EffectiveMinChangeRatio,
            ["ConsecutiveChangedFrames"] = decision.ConsecutiveChangedFrames,
            ["NoChangeFrames"] = decision.NoChangeFrames,
            ["NoMaterialFrame"] = !decision.Triggered
        });

        output["RoiX"] = roi.X;
        output["RoiY"] = roi.Y;
        output["RoiW"] = roi.Width;
        output["RoiH"] = roi.Height;
        output["StateScope"] = "OperatorInstance";
        output["StateKey"] = operatorId;
        return output;
    }

    private Rect ResolveRoi(Operator @operator, Mat src)
    {
        var x = ReadIntParam(@operator, "RoiX", 0);
        var y = ReadIntParam(@operator, "RoiY", 0);
        var configuredWidth = ReadIntParam(@operator, "RoiW", 0);
        var configuredHeight = ReadIntParam(@operator, "RoiH", 0);

        return FrameChangeTriggerKernel.ResolveRoi(
            x,
            y,
            configuredWidth,
            configuredHeight,
            src.Width,
            src.Height);
    }

    private void TryCleanupStaleStates(DateTime nowUtc)
    {
        if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
        {
            return;
        }

        lock (_cleanupSync)
        {
            if ((nowUtc - _lastCleanupUtc) < CleanupInterval)
            {
                return;
            }

            var staleBefore = nowUtc - StateTtl;
            foreach (var entry in _states)
            {
                var state = entry.Value;
                var shouldRemove = false;
                lock (state.SyncRoot)
                {
                    shouldRemove = state.LastTouchedUtc < staleBefore;
                }

                if (!shouldRemove || !_states.TryRemove(entry.Key, out var removedState))
                {
                    continue;
                }

                lock (removedState.SyncRoot)
                {
                    removedState.Clear();
                }
            }

            _lastCleanupUtc = nowUtc;
        }
    }

    private static void ValidateProfile(Operator @operator, List<string> errors)
    {
        var profile = ReadStringParam(@operator, "Profile", "line_fast_default").Trim();
        if (profile.Equals("line_fast_default", StringComparison.OrdinalIgnoreCase) ||
            profile.Equals("line_noise_guard", StringComparison.OrdinalIgnoreCase) ||
            profile.Equals("line_low_contrast", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        errors.Add("Profile must be line_fast_default, line_noise_guard, or line_low_contrast.");
    }

    private static void ValidateBool(Operator @operator, string name, List<string> errors)
    {
        if (!TryReadBoolParam(@operator, name, false, out _, out var error))
        {
            errors.Add(error);
        }
    }

    private static void ValidateEnum<TEnum>(Operator @operator, string name, List<string> errors)
        where TEnum : struct, Enum
    {
        if (!TryReadEnumParam<TEnum>(@operator, name, default, out _, out var error))
        {
            errors.Add(error);
        }
    }

    private static void ValidateOddKernel(Operator @operator, string name, List<string> errors)
    {
        if (!TryReadIntParam(@operator, name, 0, out var value, out var error))
        {
            errors.Add(error);
            return;
        }

        if (value == 0)
        {
            return;
        }

        if (value < 3 || value > 15 || value % 2 == 0)
        {
            errors.Add($"{name} must be 0 or an odd integer from 3 to 15.");
        }
    }

    private static void ValidateIntRange(
        Operator @operator,
        string name,
        int defaultValue,
        int? min,
        int? max,
        List<string> errors)
    {
        if (!TryReadIntParam(@operator, name, defaultValue, out var value, out var error))
        {
            errors.Add(error);
            return;
        }

        if (min.HasValue && value < min.Value)
        {
            errors.Add($"{name} must be >= {min.Value}.");
        }

        if (max.HasValue && value > max.Value)
        {
            errors.Add($"{name} must be <= {max.Value}.");
        }
    }

    private static void ValidateDoubleRange(
        Operator @operator,
        string name,
        double defaultValue,
        double min,
        double max,
        List<string> errors)
    {
        if (!TryReadDoubleParam(@operator, name, defaultValue, out var value, out var error))
        {
            errors.Add(error);
            return;
        }

        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
        {
            errors.Add($"{name} must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static bool TryReadIntParam(
        Operator @operator,
        string name,
        int defaultValue,
        out int value,
        out string error)
    {
        if (!TryGetRawParameterValue(@operator, name, out var raw) || raw == null)
        {
            value = defaultValue;
            error = string.Empty;
            return true;
        }

        try
        {
            value = raw is JsonElement element
                ? element.GetInt32()
                : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException)
        {
            value = defaultValue;
            error = $"{name} must be an integer.";
            return false;
        }
    }

    private static bool TryReadDoubleParam(
        Operator @operator,
        string name,
        double defaultValue,
        out double value,
        out string error)
    {
        if (!TryGetRawParameterValue(@operator, name, out var raw) || raw == null)
        {
            value = defaultValue;
            error = string.Empty;
            return true;
        }

        try
        {
            value = raw is JsonElement element
                ? element.GetDouble()
                : Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or JsonException)
        {
            value = defaultValue;
            error = $"{name} must be a number.";
            return false;
        }
    }

    private static bool TryReadBoolParam(
        Operator @operator,
        string name,
        bool defaultValue,
        out bool value,
        out string error)
    {
        if (!TryGetRawParameterValue(@operator, name, out var raw) || raw == null)
        {
            value = defaultValue;
            error = string.Empty;
            return true;
        }

        try
        {
            value = raw is JsonElement element
                ? element.GetBoolean()
                : Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or JsonException)
        {
            value = defaultValue;
            error = $"{name} must be a boolean.";
            return false;
        }
    }

    private static bool TryReadEnumParam<TEnum>(
        Operator @operator,
        string name,
        TEnum defaultValue,
        out TEnum value,
        out string error)
        where TEnum : struct, Enum
    {
        if (!TryGetRawParameterValue(@operator, name, out var raw) || raw == null)
        {
            value = defaultValue;
            error = string.Empty;
            return true;
        }

        var text = raw is JsonElement element ? element.ToString() : Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse<TEnum>(text.Trim(), ignoreCase: true, out value))
        {
            error = string.Empty;
            return true;
        }

        value = defaultValue;
        error = $"{name} must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
        return false;
    }

    private static int ReadIntParam(Operator @operator, string name, int defaultValue)
    {
        return TryReadIntParam(@operator, name, defaultValue, out var value, out _) ? value : defaultValue;
    }

    private static double ReadDoubleParam(Operator @operator, string name, double defaultValue)
    {
        return TryReadDoubleParam(@operator, name, defaultValue, out var value, out _) ? value : defaultValue;
    }

    private static bool ReadBoolParam(Operator @operator, string name, bool defaultValue)
    {
        return TryReadBoolParam(@operator, name, defaultValue, out var value, out _) ? value : defaultValue;
    }

    private static string ReadStringParam(Operator @operator, string name, string defaultValue)
    {
        if (!TryGetRawParameterValue(@operator, name, out var raw) || raw == null)
        {
            return defaultValue;
        }

        return raw is JsonElement element
            ? element.ToString()
            : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? defaultValue;
    }

    private static TEnum ReadEnumParam<TEnum>(Operator @operator, string name, TEnum defaultValue)
        where TEnum : struct, Enum
    {
        return TryReadEnumParam(@operator, name, defaultValue, out var value, out _) ? value : defaultValue;
    }

    private static bool TryGetRawParameterValue(Operator @operator, string name, out object? value)
    {
        var parameter = @operator.Parameters.FirstOrDefault(item => item.Name == name);
        value = parameter?.Value;
        return parameter != null;
    }

    private sealed class FrameChangeState
    {
        public object SyncRoot { get; } = new();
        public FrameChangeTriggerKernelState KernelState { get; } = new();
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        public void Clear()
        {
            KernelState.Dispose();
        }
    }
}
