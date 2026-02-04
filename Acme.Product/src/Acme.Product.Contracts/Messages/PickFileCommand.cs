namespace Acme.Product.Contracts.Messages;

/// <summary>
/// 请求选择文件命令
/// </summary>
public class PickFileCommand : MessageBase
{
    public PickFileCommand()
    {
        MessageType = nameof(PickFileCommand);
    }

    /// <summary>
    /// 参数名称（用于标识是哪个参数触发的选择）
    /// </summary>
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    /// 文件过滤器（例如 "Image Files|*.bmp;*.jpg;*.png"）
    /// </summary>
    public string Filter { get; set; } = "All Files|*.*";
}
