using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Concurrent;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "帧变化触发",
    Description = "通过连续帧 ROI 变化判断端子是否到达；未到料时短路当前检测周期，避免空帧进入深度学习。",
    Category = "逻辑工具",
    IconName = "activity",
    Keywords = new[] { "触发", "到料", "帧差", "视频流", "连续采集", "软触发", "去重", "冷却", "trigger", "arrival", "frame change", "continuous", "video" }
)]
[InputPort("Image", "输入图像", PortDataType.Image, IsRequired = true)]
[OutputPort("Image", "输出图像", PortDataType.Image)]
[OutputPort("Triggered", "是否触发", PortDataType.Boolean)]
[OutputPort("ChangeScore", "变化比例", PortDataType.Float)]
[OutputPort("ChangedPixels", "变化像素数", PortDataType.Integer)]
[OutputPort("Reason", "判定原因", PortDataType.String)]
[OperatorParam("Enabled", "启用检测", "bool", Description = "关闭后图像直接放行，不做帧差判断。", DefaultValue = true)]
[OperatorParam("ShortCircuitWhenNotTriggered", "未触发时跳过本轮", "bool", Description = "开启后，未检测到到料变化时短路当前流程，不执行后续 YOLO 和结果输出。", DefaultValue = true)]
[OperatorParam("PixelThreshold", "像素差阈值", "int", Description = "单个像素灰度差超过该值才计入变化区域。现场反光/抖动多时可适当调高。", DefaultValue = 30, Min = 1, Max = 255)]
[OperatorParam("MinChangeRatio", "最小变化比例", "double", Description = "ROI 内变化像素占比达到该值才认为到料。误触发多时调高，漏检时调低。", DefaultValue = 0.02, Min = 0.0, Max = 1.0)]
[OperatorParam("MinChangePixels", "最小变化像素数", "int", Description = "ROI 内变化像素数量下限，用于过滤小面积噪声。", DefaultValue = 500, Min = 0)]
[OperatorParam("CooldownMs", "冷却时间(ms)", "int", Description = "触发后在该时间内抑制重复触发，防止同一端子停留期间重复判定。", DefaultValue = 1200, Min = 0, Max = 60000)]
[OperatorParam("RoiX", "检测区域X", "int", Description = "到料检测 ROI 左上角 X。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiY", "检测区域Y", "int", Description = "到料检测 ROI 左上角 Y。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiW", "检测区域宽度", "int", Description = "到料检测 ROI 宽度；0 表示从 X 到图像右边界。", DefaultValue = 0, Min = 0)]
[OperatorParam("RoiH", "检测区域高度", "int", Description = "到料检测 ROI 高度；0 表示从 Y 到图像下边界。", DefaultValue = 0, Min = 0)]
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
        if (!TryGetInputImage(inputs, out var imageWrapper) || imageWrapper == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("必须提供输入图像。"));
        }

        var src = imageWrapper.GetMat();
        if (src.Empty())
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("输入图像为空。"));
        }

        var enabled = GetBoolParam(@operator, "Enabled", true);
        if (!enabled)
        {
            return Task.FromResult(OperatorExecutionOutput.Success(CreatePassThroughOutput(src, true, 1.0, 0, "disabled")));
        }

        var pixelThreshold = GetIntParam(@operator, "PixelThreshold", 30, 1, 255);
        var minChangeRatio = GetDoubleParam(@operator, "MinChangeRatio", 0.02, 0.0, 1.0);
        var minChangePixels = GetIntParam(@operator, "MinChangePixels", 500, 0);
        var cooldownMs = GetIntParam(@operator, "CooldownMs", 1200, 0, 60_000);
        var shortCircuitWhenNotTriggered = GetBoolParam(@operator, "ShortCircuitWhenNotTriggered", true);
        var roi = ResolveRoi(@operator, src);
        var nowUtc = DateTime.UtcNow;
        var state = _states.GetOrAdd(@operator.Id, static _ => new FrameChangeState());

        using var grayRoi = BuildGrayRoi(src, roi);
        FrameChangeDecision decision;

        lock (state.SyncRoot)
        {
            decision = Evaluate(state, grayRoi, pixelThreshold, minChangeRatio, minChangePixels, cooldownMs, nowUtc);
            state.PreviousGrayRoi?.Dispose();
            state.PreviousGrayRoi = grayRoi.Clone();
            state.LastTouchedUtc = nowUtc;
        }

        TryCleanupStaleStates(nowUtc);

        var output = CreatePassThroughOutput(src, decision.Triggered, decision.ChangeScore, decision.ChangedPixels, decision.Reason);
        output["RoiX"] = roi.X;
        output["RoiY"] = roi.Y;
        output["RoiW"] = roi.Width;
        output["RoiH"] = roi.Height;
        output["StateScope"] = "OperatorInstance";
        output["StateKey"] = @operator.Id;

        return Task.FromResult(
            decision.Triggered || !shortCircuitWhenNotTriggered
                ? OperatorExecutionOutput.Success(output)
                : OperatorExecutionOutput.ShortCircuit(output));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var pixelThreshold = GetIntParam(@operator, "PixelThreshold", 30);
        if (pixelThreshold is < 1 or > 255)
        {
            return ValidationResult.Invalid("像素差阈值必须在 1 到 255 之间。");
        }

        var minChangeRatio = GetDoubleParam(@operator, "MinChangeRatio", 0.02);
        if (minChangeRatio is < 0.0 or > 1.0)
        {
            return ValidationResult.Invalid("最小变化比例必须在 0 到 1 之间。");
        }

        return ValidationResult.Valid();
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

    private FrameChangeDecision Evaluate(
        FrameChangeState state,
        Mat grayRoi,
        int pixelThreshold,
        double minChangeRatio,
        int minChangePixels,
        int cooldownMs,
        DateTime nowUtc)
    {
        if (state.PreviousGrayRoi == null || state.PreviousGrayRoi.Empty() || state.PreviousGrayRoi.Size() != grayRoi.Size())
        {
            return new FrameChangeDecision(false, 0.0, 0, "baseline");
        }

        using var diff = new Mat();
        using var mask = new Mat();
        Cv2.Absdiff(state.PreviousGrayRoi, grayRoi, diff);
        Cv2.Threshold(diff, mask, pixelThreshold, 255, ThresholdTypes.Binary);

        var changedPixels = Cv2.CountNonZero(mask);
        var totalPixels = Math.Max(1, grayRoi.Width * grayRoi.Height);
        var changeScore = changedPixels / (double)totalPixels;
        var changedEnough = changedPixels >= minChangePixels && changeScore >= minChangeRatio;

        if (!changedEnough)
        {
            return new FrameChangeDecision(false, changeScore, changedPixels, "below_threshold");
        }

        if (cooldownMs > 0 && state.LastTriggeredUtc != DateTime.MinValue &&
            (nowUtc - state.LastTriggeredUtc).TotalMilliseconds < cooldownMs)
        {
            return new FrameChangeDecision(false, changeScore, changedPixels, "cooldown");
        }

        state.LastTriggeredUtc = nowUtc;
        return new FrameChangeDecision(true, changeScore, changedPixels, "change_detected");
    }

    private Dictionary<string, object> CreatePassThroughOutput(
        Mat src,
        bool triggered,
        double changeScore,
        int changedPixels,
        string reason)
    {
        return CreateImageOutput(src.Clone(), new Dictionary<string, object>
        {
            ["Triggered"] = triggered,
            ["ChangeScore"] = changeScore,
            ["ChangedPixels"] = changedPixels,
            ["Reason"] = reason,
            ["NoMaterialFrame"] = !triggered
        });
    }

    private static Mat BuildGrayRoi(Mat src, Rect roi)
    {
        using var cropped = new Mat(src, roi);
        using var gray = cropped.Channels() > 1
            ? cropped.CvtColor(ColorConversionCodes.BGR2GRAY)
            : cropped.Clone();

        return gray.Clone();
    }

    private Rect ResolveRoi(Operator @operator, Mat src)
    {
        var x = GetIntParam(@operator, "RoiX", 0, 0, Math.Max(0, src.Width - 1));
        var y = GetIntParam(@operator, "RoiY", 0, 0, Math.Max(0, src.Height - 1));
        var configuredWidth = GetIntParam(@operator, "RoiW", 0, 0, src.Width);
        var configuredHeight = GetIntParam(@operator, "RoiH", 0, 0, src.Height);

        var width = configuredWidth <= 0 ? src.Width - x : configuredWidth;
        var height = configuredHeight <= 0 ? src.Height - y : configuredHeight;
        width = Math.Clamp(width, 1, src.Width - x);
        height = Math.Clamp(height, 1, src.Height - y);

        return new Rect(x, y, width, height);
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

    private sealed class FrameChangeState
    {
        public object SyncRoot { get; } = new();
        public Mat? PreviousGrayRoi { get; set; }
        public DateTime LastTriggeredUtc { get; set; } = DateTime.MinValue;
        public DateTime LastTouchedUtc { get; set; } = DateTime.UtcNow;

        public void Clear()
        {
            PreviousGrayRoi?.Dispose();
            PreviousGrayRoi = null;
        }
    }

    private readonly record struct FrameChangeDecision(bool Triggered, double ChangeScore, int ChangedPixels, string Reason);
}
