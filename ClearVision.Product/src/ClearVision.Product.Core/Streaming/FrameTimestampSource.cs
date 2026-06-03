namespace ClearVision.Product.Core.Streaming;

public enum FrameTimestampSource
{
    Unknown = 0,
    CameraPreferred = 1,
    HostFallback = 2
}

public enum FramePayloadKind
{
    Raw = 0,
    EncodedImage = 1
}
