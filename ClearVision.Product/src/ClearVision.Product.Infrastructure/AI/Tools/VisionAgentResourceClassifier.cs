namespace ClearVision.Product.Infrastructure.AI.Tools;

internal static class VisionAgentResourceClassifier
{
    public static string Classify(string operatorType, string parameterName, string? dataType = null)
    {
        if (operatorType.Equals("ImageAcquisition", StringComparison.OrdinalIgnoreCase) &&
            (parameterName.Equals("FilePath", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(dataType, "file", StringComparison.OrdinalIgnoreCase)))
        {
            return "image_file";
        }

        if (parameterName.Contains("camera", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parameterName, "CameraId", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataType, "cameraBinding", StringComparison.OrdinalIgnoreCase))
        {
            return "camera_binding";
        }

        if (parameterName.Contains("model", StringComparison.OrdinalIgnoreCase))
        {
            return "model_resource";
        }

        if (parameterName.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return "template_artifact";
        }

        if (parameterName.Contains("plc", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("address", StringComparison.OrdinalIgnoreCase))
        {
            return "plc_address";
        }

        if (parameterName.Contains("channel", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("output", StringComparison.OrdinalIgnoreCase))
        {
            return "output_channel";
        }

        if (operatorType.Contains("Measure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operatorType, "Measurement", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("calibration", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("scale", StringComparison.OrdinalIgnoreCase) ||
            parameterName.Contains("unit", StringComparison.OrdinalIgnoreCase))
        {
            return "measurement_parameter";
        }

        return string.Empty;
    }

    public static string DisplayName(string resourceType)
    {
        return resourceType switch
        {
            "image_file" => "图像文件",
            "model_resource" => "模型资源",
            "template_artifact" => "模板资源",
            "measurement_parameter" => "测量/标定参数",
            "camera_binding" => "相机绑定",
            "output_channel" => "输出通道",
            "plc_address" => "PLC 地址",
            _ => string.IsNullOrWhiteSpace(resourceType) ? "资源" : resourceType
        };
    }
}
