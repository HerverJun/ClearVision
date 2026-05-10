namespace Acme.Product.Core.Streaming;

public sealed record FrameEnvelope(
    string CameraId,
    long Sequence,
    DateTimeOffset HostReceiveTimestampUtc,
    int Width,
    int Height,
    string PixelFormat,
    FramePayloadKind PayloadKind,
    ReadOnlyMemory<byte> Payload,
    long? CameraTimestampNs = null,
    long? DeviceFrameCounter = null,
    int? Stride = null,
    FrameTimestampSource TimestampSource = FrameTimestampSource.Unknown,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? Tags = null)
{
    public string EffectiveCorrelationId => string.IsNullOrWhiteSpace(CorrelationId)
        ? $"{CameraId}:{Sequence}"
        : CorrelationId;
}
