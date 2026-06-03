using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

public enum FrameChangeNormalizeMode
{
    None,
    MeanShift,
    PercentileClip
}

public enum FrameChangeReferenceUpdateMode
{
    PreviousFrame,
    StableBackground,
    ExponentialMovingAverage
}

public sealed record FrameChangeTriggerOptions(
    int PixelThreshold,
    double MinChangeRatio,
    int MinChangePixels,
    int CooldownMs,
    int BlurSize,
    int MorphOpenSize,
    FrameChangeNormalizeMode NormalizeMode,
    FrameChangeReferenceUpdateMode ReferenceUpdateMode,
    double ReferenceUpdateAlpha,
    bool AdaptivePixelThreshold,
    int MinConsecutiveChangedFrames,
    int ResetAfterNoChangeFrames,
    bool TriggerOnRisingEdgeOnly)
{
    public static FrameChangeTriggerOptions LineFastDefault { get; } = new(
        PixelThreshold: 30,
        MinChangeRatio: 0.02,
        MinChangePixels: 500,
        CooldownMs: 1200,
        BlurSize: 0,
        MorphOpenSize: 0,
        NormalizeMode: FrameChangeNormalizeMode.None,
        ReferenceUpdateMode: FrameChangeReferenceUpdateMode.PreviousFrame,
        ReferenceUpdateAlpha: 0.05,
        AdaptivePixelThreshold: false,
        MinConsecutiveChangedFrames: 1,
        ResetAfterNoChangeFrames: 1,
        TriggerOnRisingEdgeOnly: true);

    public static FrameChangeTriggerOptions LineNoiseGuard { get; } = LineFastDefault with
    {
        BlurSize = 3,
        MorphOpenSize = 3,
        NormalizeMode = FrameChangeNormalizeMode.MeanShift,
        ReferenceUpdateMode = FrameChangeReferenceUpdateMode.StableBackground,
        MinConsecutiveChangedFrames = 2,
        ResetAfterNoChangeFrames = 2
    };

    public static FrameChangeTriggerOptions LineLowContrast { get; } = LineNoiseGuard with
    {
        PixelThreshold = 12,
        MinChangeRatio = 0.012,
        AdaptivePixelThreshold = true,
        ReferenceUpdateMode = FrameChangeReferenceUpdateMode.ExponentialMovingAverage,
        ReferenceUpdateAlpha = 0.03
    };
}

public sealed record FrameChangeTriggerDecision(
    bool Triggered,
    double ChangeScore,
    int ChangedPixels,
    string Reason,
    bool BaselineReady,
    int TotalPixels,
    int CooldownRemainingMs,
    int EffectivePixelThreshold,
    double EffectiveMinChangeRatio,
    int ConsecutiveChangedFrames,
    int NoChangeFrames);

public sealed class FrameChangeTriggerKernelState : IDisposable
{
    public Mat? ReferenceGrayRoi { get; set; }
    public DateTime LastTriggeredUtc { get; set; } = DateTime.MinValue;
    public int ConsecutiveChangedFrames { get; set; }
    public int NoChangeFrames { get; set; }
    public bool WasChangedLastFrame { get; set; }

    public void ResetChangeState()
    {
        ConsecutiveChangedFrames = 0;
        NoChangeFrames = 0;
        WasChangedLastFrame = false;
    }

    public void Dispose()
    {
        ReferenceGrayRoi?.Dispose();
        ReferenceGrayRoi = null;
    }
}

public static class FrameChangeTriggerKernel
{
    public static Rect ResolveRoi(int x, int y, int width, int height, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageWidth), "Image dimensions must be positive.");
        }

        var clampedX = Math.Clamp(x, 0, imageWidth - 1);
        var clampedY = Math.Clamp(y, 0, imageHeight - 1);
        var effectiveWidth = width <= 0 ? imageWidth - clampedX : width;
        var effectiveHeight = height <= 0 ? imageHeight - clampedY : height;

        effectiveWidth = Math.Clamp(effectiveWidth, 1, imageWidth - clampedX);
        effectiveHeight = Math.Clamp(effectiveHeight, 1, imageHeight - clampedY);

        return new Rect(clampedX, clampedY, effectiveWidth, effectiveHeight);
    }

    public static Mat BuildGrayRoi(Mat src, Rect roi, FrameChangeTriggerOptions options)
    {
        using var cropped = new Mat(src, roi);
        using var gray = cropped.Channels() > 1
            ? cropped.CvtColor(ColorConversionCodes.BGR2GRAY)
            : cropped.Clone();

        var prepared = gray.Clone();
        if (options.BlurSize > 1)
        {
            using var blurred = new Mat();
            Cv2.GaussianBlur(prepared, blurred, new Size(options.BlurSize, options.BlurSize), 0);
            prepared.Dispose();
            prepared = blurred.Clone();
        }

        return prepared;
    }

    public static FrameChangeTriggerDecision Evaluate(
        FrameChangeTriggerKernelState state,
        Mat currentGrayRoi,
        FrameChangeTriggerOptions options,
        DateTime nowUtc)
    {
        var totalPixels = Math.Max(1, currentGrayRoi.Width * currentGrayRoi.Height);

        if (state.ReferenceGrayRoi == null ||
            state.ReferenceGrayRoi.Empty() ||
            state.ReferenceGrayRoi.Size() != currentGrayRoi.Size())
        {
            ReplaceReference(state, currentGrayRoi);
            state.ResetChangeState();
            return new FrameChangeTriggerDecision(
                Triggered: false,
                ChangeScore: 0.0,
                ChangedPixels: 0,
                Reason: "baseline",
                BaselineReady: true,
                TotalPixels: totalPixels,
                CooldownRemainingMs: 0,
                EffectivePixelThreshold: options.PixelThreshold,
                EffectiveMinChangeRatio: options.MinChangeRatio,
                ConsecutiveChangedFrames: 0,
                NoChangeFrames: 0);
        }

        using var reference = PrepareReferenceForDiff(state.ReferenceGrayRoi, currentGrayRoi, options.NormalizeMode);
        using var current = PrepareCurrentForDiff(state.ReferenceGrayRoi, currentGrayRoi, options.NormalizeMode);
        using var diff = new Mat();
        using var mask = new Mat();

        Cv2.Absdiff(reference, current, diff);
        var effectivePixelThreshold = ResolveEffectivePixelThreshold(diff, options);
        Cv2.Threshold(diff, mask, effectivePixelThreshold, 255, ThresholdTypes.Binary);

        if (options.MorphOpenSize > 1)
        {
            using var kernel = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(options.MorphOpenSize, options.MorphOpenSize));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        }

        var changedPixels = Cv2.CountNonZero(mask);
        var changeScore = changedPixels / (double)totalPixels;
        var changedEnough = changedPixels >= options.MinChangePixels && changeScore >= options.MinChangeRatio;

        if (changedEnough)
        {
            state.ConsecutiveChangedFrames++;
            state.NoChangeFrames = 0;
        }
        else
        {
            state.ConsecutiveChangedFrames = 0;
            state.NoChangeFrames++;
            if (options.ResetAfterNoChangeFrames > 0 &&
                state.NoChangeFrames >= options.ResetAfterNoChangeFrames)
            {
                state.WasChangedLastFrame = false;
            }
        }

        var cooldownRemainingMs = GetCooldownRemainingMs(state, options.CooldownMs, nowUtc);
        var reason = "below_threshold";
        var triggered = false;

        if (changedEnough)
        {
            if (state.ConsecutiveChangedFrames < options.MinConsecutiveChangedFrames)
            {
                reason = "consecutive_warmup";
            }
            else if (cooldownRemainingMs > 0)
            {
                reason = "cooldown";
            }
            else if (options.TriggerOnRisingEdgeOnly && state.WasChangedLastFrame)
            {
                reason = "rising_edge_suppressed";
            }
            else
            {
                triggered = true;
                reason = "change_detected";
                state.LastTriggeredUtc = nowUtc;
                cooldownRemainingMs = options.CooldownMs;
            }
        }

        if (changedEnough)
        {
            state.WasChangedLastFrame = true;
        }

        UpdateReference(state, currentGrayRoi, options, changedEnough);

        return new FrameChangeTriggerDecision(
            Triggered: triggered,
            ChangeScore: changeScore,
            ChangedPixels: changedPixels,
            Reason: reason,
            BaselineReady: true,
            TotalPixels: totalPixels,
            CooldownRemainingMs: cooldownRemainingMs,
            EffectivePixelThreshold: effectivePixelThreshold,
            EffectiveMinChangeRatio: options.MinChangeRatio,
            ConsecutiveChangedFrames: state.ConsecutiveChangedFrames,
            NoChangeFrames: state.NoChangeFrames);
    }

    private static Mat PrepareReferenceForDiff(Mat reference, Mat current, FrameChangeNormalizeMode mode)
    {
        return mode switch
        {
            FrameChangeNormalizeMode.PercentileClip => PercentileClip(reference),
            _ => reference.Clone()
        };
    }

    private static Mat PrepareCurrentForDiff(Mat reference, Mat current, FrameChangeNormalizeMode mode)
    {
        return mode switch
        {
            FrameChangeNormalizeMode.MeanShift => MeanShift(reference, current),
            FrameChangeNormalizeMode.PercentileClip => PercentileClip(current),
            _ => current.Clone()
        };
    }

    private static Mat MeanShift(Mat reference, Mat current)
    {
        var referenceMean = Cv2.Mean(reference).Val0;
        var currentMean = Cv2.Mean(current).Val0;
        var shifted = new Mat();
        current.ConvertTo(shifted, current.Type(), 1.0, referenceMean - currentMean);
        return shifted;
    }

    private static Mat PercentileClip(Mat src)
    {
        using var mean = new Mat();
        using var stddev = new Mat();
        Cv2.MeanStdDev(src, mean, stddev);

        var center = mean.At<double>(0);
        var spread = Math.Max(1.0, stddev.At<double>(0));
        var low = Math.Clamp(center - (2.5 * spread), 0.0, 255.0);
        var high = Math.Clamp(center + (2.5 * spread), low + 1.0, 255.0);
        var scale = 255.0 / (high - low);
        var shifted = new Mat();
        src.ConvertTo(shifted, MatType.CV_8UC1, scale, -low * scale);
        return shifted;
    }

    private static int ResolveEffectivePixelThreshold(Mat diff, FrameChangeTriggerOptions options)
    {
        if (!options.AdaptivePixelThreshold)
        {
            return options.PixelThreshold;
        }

        using var mean = new Mat();
        using var stddev = new Mat();
        Cv2.MeanStdDev(diff, mean, stddev);

        var adaptive = (int)Math.Round(mean.At<double>(0) + (2.0 * stddev.At<double>(0)));
        return Math.Clamp(Math.Min(options.PixelThreshold, Math.Max(1, adaptive)), 1, 255);
    }

    private static int GetCooldownRemainingMs(
        FrameChangeTriggerKernelState state,
        int cooldownMs,
        DateTime nowUtc)
    {
        if (cooldownMs <= 0 || state.LastTriggeredUtc == DateTime.MinValue)
        {
            return 0;
        }

        var elapsed = (nowUtc - state.LastTriggeredUtc).TotalMilliseconds;
        return Math.Max(0, (int)Math.Ceiling(cooldownMs - elapsed));
    }

    private static void UpdateReference(
        FrameChangeTriggerKernelState state,
        Mat currentGrayRoi,
        FrameChangeTriggerOptions options,
        bool changedEnough)
    {
        switch (options.ReferenceUpdateMode)
        {
            case FrameChangeReferenceUpdateMode.PreviousFrame:
                ReplaceReference(state, currentGrayRoi);
                break;
            case FrameChangeReferenceUpdateMode.StableBackground:
                if (!changedEnough)
                {
                    ReplaceReference(state, currentGrayRoi);
                }
                break;
            case FrameChangeReferenceUpdateMode.ExponentialMovingAverage:
                if (state.ReferenceGrayRoi == null || state.ReferenceGrayRoi.Empty())
                {
                    ReplaceReference(state, currentGrayRoi);
                    break;
                }

                using (var blended = new Mat())
                {
                    Cv2.AddWeighted(
                        currentGrayRoi,
                        options.ReferenceUpdateAlpha,
                        state.ReferenceGrayRoi,
                        1.0 - options.ReferenceUpdateAlpha,
                        0,
                        blended);
                    ReplaceReference(state, blended);
                }

                break;
            default:
                ReplaceReference(state, currentGrayRoi);
                break;
        }
    }

    private static void ReplaceReference(FrameChangeTriggerKernelState state, Mat currentGrayRoi)
    {
        var clone = currentGrayRoi.Clone();
        state.ReferenceGrayRoi?.Dispose();
        state.ReferenceGrayRoi = clone;
    }
}
