using Acme.Product.Core.Streaming;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Continuous;

public sealed record ArrivalDetectorOptions(
    int PixelThreshold = 30,
    double MinChangeRatio = 0.02,
    int MinChangePixels = 500,
    int CooldownMs = 1200,
    Rect? Roi = null);

public sealed record ArrivalSignal(
    string CameraId,
    long Sequence,
    DateTimeOffset EventTimeUtc,
    string TriggerType,
    double Score,
    Rect DecisionRoi,
    string CorrelationId,
    int ChangedPixels);

public interface IArrivalDetector
{
    ArrivalSignal? Update(FrameEnvelope frame);
}

public sealed class FrameDifferenceArrivalDetector : IArrivalDetector, IDisposable
{
    private readonly ArrivalDetectorOptions _options;
    private readonly object _gate = new();
    private Mat? _previousGrayRoi;
    private DateTimeOffset _lastTriggeredUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public FrameDifferenceArrivalDetector(ArrivalDetectorOptions? options = null)
    {
        _options = options ?? new ArrivalDetectorOptions();
    }

    public ArrivalSignal? Update(FrameEnvelope frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        using var mat = DecodeFrame(frame);
        if (mat.Empty())
        {
            return null;
        }

        var roi = ResolveRoi(mat, _options.Roi);
        using var grayRoi = BuildGrayRoi(mat, roi);

        lock (_gate)
        {
            if (_previousGrayRoi == null ||
                _previousGrayRoi.Empty() ||
                _previousGrayRoi.Size() != grayRoi.Size())
            {
                ReplaceBaseline(grayRoi);
                return null;
            }

            using var diff = new Mat();
            using var mask = new Mat();
            Cv2.Absdiff(_previousGrayRoi, grayRoi, diff);
            Cv2.Threshold(diff, mask, _options.PixelThreshold, 255, ThresholdTypes.Binary);

            var changedPixels = Cv2.CountNonZero(mask);
            var totalPixels = Math.Max(1, grayRoi.Width * grayRoi.Height);
            var score = changedPixels / (double)totalPixels;

            ReplaceBaseline(grayRoi);

            if (changedPixels < _options.MinChangePixels || score < _options.MinChangeRatio)
            {
                return null;
            }

            var now = frame.HostReceiveTimestampUtc;
            if (_options.CooldownMs > 0 &&
                _lastTriggeredUtc != DateTimeOffset.MinValue &&
                (now - _lastTriggeredUtc).TotalMilliseconds < _options.CooldownMs)
            {
                return null;
            }

            _lastTriggeredUtc = now;
            return new ArrivalSignal(
                frame.CameraId,
                frame.Sequence,
                now,
                "frame_change",
                score,
                roi,
                frame.EffectiveCorrelationId,
                changedPixels);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _previousGrayRoi?.Dispose();
        _previousGrayRoi = null;
        _disposed = true;
    }

    private void ReplaceBaseline(Mat grayRoi)
    {
        _previousGrayRoi?.Dispose();
        _previousGrayRoi = grayRoi.Clone();
    }

    private static Mat DecodeFrame(FrameEnvelope frame)
    {
        if (frame.PayloadKind == FramePayloadKind.EncodedImage)
        {
            return Cv2.ImDecode(frame.Payload.ToArray(), ImreadModes.Color);
        }

        var matType = frame.PixelFormat.Equals("Mono8", StringComparison.OrdinalIgnoreCase)
            ? MatType.CV_8UC1
            : MatType.CV_8UC3;
        using var raw = new Mat(frame.Height, frame.Width, matType, frame.Payload.ToArray());
        var decoded = raw.Clone();
        if (frame.PixelFormat.Equals("RGB8", StringComparison.OrdinalIgnoreCase))
        {
            Cv2.CvtColor(decoded, decoded, ColorConversionCodes.RGB2BGR);
        }

        return decoded;
    }

    private static Rect ResolveRoi(Mat mat, Rect? configuredRoi)
    {
        if (configuredRoi == null)
        {
            return new Rect(0, 0, mat.Width, mat.Height);
        }

        var roi = configuredRoi.Value;
        var x = Math.Clamp(roi.X, 0, Math.Max(0, mat.Width - 1));
        var y = Math.Clamp(roi.Y, 0, Math.Max(0, mat.Height - 1));
        var width = roi.Width <= 0 ? mat.Width - x : Math.Clamp(roi.Width, 1, mat.Width - x);
        var height = roi.Height <= 0 ? mat.Height - y : Math.Clamp(roi.Height, 1, mat.Height - y);
        return new Rect(x, y, width, height);
    }

    private static Mat BuildGrayRoi(Mat src, Rect roi)
    {
        using var cropped = new Mat(src, roi);
        using var gray = cropped.Channels() > 1
            ? cropped.CvtColor(ColorConversionCodes.BGR2GRAY)
            : cropped.Clone();

        return gray.Clone();
    }
}
