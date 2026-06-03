// IIndustrialCamera.cs
// 相机像素格式
// 作者：蘅芜君

namespace ClearVision.Product.Core.Cameras;

/// <summary>
/// 工业相机扩展接口 - 支持硬件触发和软件触发
/// </summary>
public interface IIndustrialCamera : ICamera
{
    /// <summary>
    /// 设置像素格式
    /// </summary>
    Task SetPixelFormatAsync(CameraPixelFormat pixelFormat);

    /// <summary>
    /// 设置触发模式
    /// </summary>
    Task SetTriggerModeAsync(CameraTriggerMode mode, string? hardwareTriggerSource = null);

    /// <summary>
    /// 执行软件触发（仅在软件触发模式下有效）
    /// </summary>
    Task ExecuteSoftwareTriggerAsync();

    /// <summary>
    /// 帧接收事件
    /// </summary>
    event EventHandler<CameraFrameReceivedEventArgs>? FrameReceived;
}

/// <summary>
/// 相机帧接收事件参数
/// </summary>
public class CameraFrameReceivedEventArgs : EventArgs
{
    /// <summary>
    /// 图像数据
    /// </summary>
    public byte[] ImageData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// 图像宽度
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 图像高度
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public ulong? DeviceFrameCounter { get; set; }

    public ulong? CameraTimestampNs { get; set; }

    public int? Stride { get; set; }

    public CameraPixelFormat PixelFormat { get; set; } = CameraPixelFormat.Unknown;
}

/// <summary>
/// 相机提供器接口 - 用于发现创建设备
/// </summary>
public interface ICameraProvider : IDisposable
{
    /// <summary>
    /// 提供器名称
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// 是否已连接
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 是否正在采集
    /// </summary>
    bool IsGrabbing { get; }

    /// <summary>
    /// 当前设备信息
    /// </summary>
    CameraDeviceInfo? CurrentDevice { get; }

    /// <summary>
    /// 枚举设备
    /// </summary>
    List<CameraDeviceInfo> EnumerateDevices();

    /// <summary>
    /// 打开设备
    /// </summary>
    bool Open(string serialNumber);

    /// <summary>
    /// 关闭设备
    /// </summary>
    bool Close();

    /// <summary>
    /// 开始采集
    /// </summary>
    bool StartGrabbing();

    /// <summary>
    /// 停止采集
    /// </summary>
    bool StopGrabbing();

    /// <summary>
    /// 获取帧
    /// </summary>
    CameraFrame? GetFrame(int timeoutMs = 1000);

    /// <summary>
    /// 设置曝光时间
    /// </summary>
    bool SetExposure(double microseconds);

    /// <summary>
    /// 设置增益
    /// </summary>
    bool SetGain(double value);

    /// <summary>
    /// 设置像素格式
    /// </summary>
    bool SetPixelFormat(CameraPixelFormat pixelFormat);

    /// <summary>
    /// 设置触发模式
    /// </summary>
    bool SetTriggerMode(CameraTriggerMode mode, string? hardwareTriggerSource = null);

    /// <summary>
    /// 执行软件触发
    /// </summary>
    bool ExecuteSoftwareTrigger();
}

/// <summary>
/// 相机设备信息
/// </summary>
public class CameraDeviceInfo
{
    /// <summary>
    /// 序列号
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>
    /// 制造商
    /// </summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>
    /// 型号
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 用户定义名称
    /// </summary>
    public string UserDefinedName { get; set; } = string.Empty;

    /// <summary>
    /// 设备 IP 地址（若 SDK 可提供）
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 接口类型（USB3/GigE等）
    /// </summary>
    public string InterfaceType { get; set; } = string.Empty;
}

/// <summary>
/// 相机帧数据
/// </summary>
public class CameraFrame
{
    /// <summary>
    /// 数据指针
    /// </summary>
    public IntPtr DataPtr { get; set; }

    /// <summary>
    /// 图像宽度
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 图像高度
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// 数据大小
    /// </summary>
    public int Size { get; set; }

    /// <summary>
    /// 像素格式
    /// </summary>
    public CameraPixelFormat PixelFormat { get; set; }

    /// <summary>
    /// 帧号
    /// </summary>
    public ulong FrameNumber { get; set; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public ulong Timestamp { get; set; }

    /// <summary>
    /// 是否需要原生释放
    /// </summary>
    public bool NeedsNativeRelease { get; set; }
}

/// <summary>
/// 相机像素格式
/// </summary>
public enum CameraPixelFormat
{
    Unknown,
    Mono8,
    RGB8,
    BGR8,
    BayerRG8,
    BayerGB8,
    BayerGR8,
    BayerBG8
}

public static class CameraPixelFormatExtensions
{
    public const string DefaultPixelFormat = nameof(CameraPixelFormat.Mono8);

    public static CameraPixelFormat Normalize(string? rawPixelFormat)
    {
        var normalized = (rawPixelFormat ?? string.Empty)
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized switch
        {
            "mono" or "mono8" or "monochrome" or "gray" or "gray8" or "grey" or "grey8" or "moon" => CameraPixelFormat.Mono8,
            "rgb" or "rgb8" => CameraPixelFormat.RGB8,
            "bgr" or "bgr8" => CameraPixelFormat.BGR8,
            "bayerrg" or "bayerrg8" => CameraPixelFormat.BayerRG8,
            "bayergb" or "bayergb8" => CameraPixelFormat.BayerGB8,
            "bayergr" or "bayergr8" => CameraPixelFormat.BayerGR8,
            "bayerbg" or "bayerbg8" => CameraPixelFormat.BayerBG8,
            _ => CameraPixelFormat.Mono8
        };
    }

    public static string ToConfigValue(this CameraPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            CameraPixelFormat.RGB8 => nameof(CameraPixelFormat.RGB8),
            CameraPixelFormat.BGR8 => nameof(CameraPixelFormat.BGR8),
            CameraPixelFormat.BayerRG8 => nameof(CameraPixelFormat.BayerRG8),
            CameraPixelFormat.BayerGB8 => nameof(CameraPixelFormat.BayerGB8),
            CameraPixelFormat.BayerGR8 => nameof(CameraPixelFormat.BayerGR8),
            CameraPixelFormat.BayerBG8 => nameof(CameraPixelFormat.BayerBG8),
            _ => nameof(CameraPixelFormat.Mono8)
        };
    }
}
