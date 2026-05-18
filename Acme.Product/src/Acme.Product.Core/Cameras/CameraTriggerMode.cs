using Acme.Product.Core.Entities;

namespace Acme.Product.Core.Cameras;

public enum CameraTriggerMode
{
    Software = 0,
    External = 1,
    Continuous = 2
}

public enum CameraSoftwareTriggerSource
{
    Manual = 0,
    EnterPhotoelectric = 1,
    SerialPhotoelectric = 2
}

public static class CameraTriggerModeExtensions
{
    public const int DefaultTargetFrameRateFps = 10;
    public const int MinTargetFrameRateFps = 1;
    public const int MaxTargetFrameRateFps = 120;

    public static CameraTriggerMode Normalize(string? rawMode)
    {
        if (string.IsNullOrWhiteSpace(rawMode))
        {
            return CameraTriggerMode.Software;
        }

        return rawMode.Trim().ToLowerInvariant() switch
        {
            "software" => CameraTriggerMode.Software,
            "hardware" => CameraTriggerMode.External,
            "external" => CameraTriggerMode.External,
            "externalsignal" => CameraTriggerMode.External,
            "continuous" => CameraTriggerMode.Continuous,
            _ => CameraTriggerMode.Software
        };
    }

    public static string ToConfigValue(this CameraTriggerMode mode) => mode switch
    {
        CameraTriggerMode.External => nameof(CameraTriggerMode.External),
        CameraTriggerMode.Continuous => nameof(CameraTriggerMode.Continuous),
        _ => nameof(CameraTriggerMode.Software)
    };

    public static bool IsFrameDriven(this CameraTriggerMode mode) =>
        mode is CameraTriggerMode.External or CameraTriggerMode.Continuous;

    public static int NormalizeTargetFrameRate(int targetFrameRateFps)
    {
        if (targetFrameRateFps <= 0)
        {
            return DefaultTargetFrameRateFps;
        }

        return Math.Clamp(targetFrameRateFps, MinTargetFrameRateFps, MaxTargetFrameRateFps);
    }

    public static CameraBindingConfig? FindBinding(this ICameraManager cameraManager, string? bindingIdOrCameraId)
    {
        if (string.IsNullOrWhiteSpace(bindingIdOrCameraId))
        {
            return null;
        }

        var normalized = bindingIdOrCameraId.Trim();
        return (cameraManager.GetBindings() ?? new List<CameraBindingConfig>()).FirstOrDefault(binding =>
            binding.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(binding.SerialNumber)
                && binding.SerialNumber.Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }
}

public static class CameraHardwareTriggerSourceExtensions
{
    public const string DefaultHardwareTriggerSource = "Line0";

    public static string Normalize(string? rawSource)
    {
        var source = rawSource?.Trim();
        return string.IsNullOrWhiteSpace(source)
            ? DefaultHardwareTriggerSource
            : source;
    }
}

public static class CameraSoftwareTriggerSourceExtensions
{
    public const int DefaultEnterPhotoelectricDebounceMs = 200;
    public const int MinEnterPhotoelectricDebounceMs = 0;
    public const int MaxEnterPhotoelectricDebounceMs = 5000;
    public const int DefaultEnterPhotoelectricTimeoutMs = 30000;
    public const int MinEnterPhotoelectricTimeoutMs = 100;
    public const int MaxEnterPhotoelectricTimeoutMs = 600000;
    public const int DefaultSerialPhotoelectricBaudRate = 9600;

    public static CameraSoftwareTriggerSource Normalize(string? rawSource)
    {
        if (string.IsNullOrWhiteSpace(rawSource))
        {
            return CameraSoftwareTriggerSource.Manual;
        }

        return rawSource.Trim().ToLowerInvariant() switch
        {
            "enterphotoelectric" => CameraSoftwareTriggerSource.EnterPhotoelectric,
            "keyboardenter" => CameraSoftwareTriggerSource.EnterPhotoelectric,
            "usbenter" => CameraSoftwareTriggerSource.EnterPhotoelectric,
            "enter" => CameraSoftwareTriggerSource.EnterPhotoelectric,
            "photoelectricenter" => CameraSoftwareTriggerSource.EnterPhotoelectric,
            "serialphotoelectric" => CameraSoftwareTriggerSource.SerialPhotoelectric,
            "comphotoelectric" => CameraSoftwareTriggerSource.SerialPhotoelectric,
            "serial" => CameraSoftwareTriggerSource.SerialPhotoelectric,
            "com" => CameraSoftwareTriggerSource.SerialPhotoelectric,
            _ => CameraSoftwareTriggerSource.Manual
        };
    }

    public static string ToConfigValue(this CameraSoftwareTriggerSource source) => source switch
    {
        CameraSoftwareTriggerSource.EnterPhotoelectric => nameof(CameraSoftwareTriggerSource.EnterPhotoelectric),
        CameraSoftwareTriggerSource.SerialPhotoelectric => nameof(CameraSoftwareTriggerSource.SerialPhotoelectric),
        _ => nameof(CameraSoftwareTriggerSource.Manual)
    };

    public static int NormalizeEnterPhotoelectricDebounceMs(int debounceMs) =>
        Math.Clamp(debounceMs, MinEnterPhotoelectricDebounceMs, MaxEnterPhotoelectricDebounceMs);

    public static int NormalizeEnterPhotoelectricTimeoutMs(int timeoutMs)
    {
        if (timeoutMs <= 0)
        {
            return DefaultEnterPhotoelectricTimeoutMs;
        }

        return Math.Clamp(timeoutMs, MinEnterPhotoelectricTimeoutMs, MaxEnterPhotoelectricTimeoutMs);
    }

    public static int NormalizeSerialPhotoelectricDebounceMs(int debounceMs) =>
        NormalizeEnterPhotoelectricDebounceMs(debounceMs);

    public static int NormalizeSerialPhotoelectricTimeoutMs(int timeoutMs) =>
        NormalizeEnterPhotoelectricTimeoutMs(timeoutMs);

    public static int NormalizeSerialPhotoelectricBaudRate(int baudRate) =>
        baudRate > 0 ? baudRate : DefaultSerialPhotoelectricBaudRate;

    public static bool UsesEnterPhotoelectricTrigger(this CameraBindingConfig? binding)
    {
        if (binding == null)
        {
            return false;
        }

        return CameraTriggerModeExtensions.Normalize(binding.TriggerMode) == CameraTriggerMode.Software &&
               Normalize(binding.SoftwareTriggerSource) == CameraSoftwareTriggerSource.EnterPhotoelectric;
    }

    public static bool UsesSerialPhotoelectricTrigger(this CameraBindingConfig? binding)
    {
        if (binding == null)
        {
            return false;
        }

        return CameraTriggerModeExtensions.Normalize(binding.TriggerMode) == CameraTriggerMode.Software &&
               Normalize(binding.SoftwareTriggerSource) == CameraSoftwareTriggerSource.SerialPhotoelectric;
    }

    public static EnterPhotoelectricTriggerOptions ToEnterPhotoelectricTriggerOptions(this CameraBindingConfig binding) =>
        new(
            binding.Id,
            binding.DisplayName,
            binding.EnterPhotoelectricDeviceId,
            NormalizeEnterPhotoelectricDebounceMs(binding.EnterPhotoelectricDebounceMs),
            NormalizeEnterPhotoelectricTimeoutMs(binding.EnterPhotoelectricTimeoutMs),
            binding.IgnoreEnterTriggerWhileBusy);

    public static SerialPhotoelectricTriggerOptions ToSerialPhotoelectricTriggerOptions(this CameraBindingConfig binding) =>
        new(
            binding.Id,
            binding.DisplayName,
            binding.SerialPhotoelectricPortName,
            NormalizeSerialPhotoelectricBaudRate(binding.SerialPhotoelectricBaudRate),
            NormalizeSerialPhotoelectricDebounceMs(binding.SerialPhotoelectricDebounceMs),
            NormalizeSerialPhotoelectricTimeoutMs(binding.SerialPhotoelectricTimeoutMs),
            binding.IgnoreSerialPhotoelectricTriggerWhileBusy);
}
