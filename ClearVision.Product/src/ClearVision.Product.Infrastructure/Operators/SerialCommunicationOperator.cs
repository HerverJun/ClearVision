// SerialCommunicationOperator.cs
// 串口通信算子 - RS-232// 功能实现485 PLC 通信
// 作者：蘅芜君

using System.IO.Ports;
using System.Text;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 串口通信算子 - RS-232/485 PLC 通信
/// SerialCommunication = 46
/// </summary>
[OperatorMeta(
    DisplayName = "串口通信",
    Description = "RS-232/485 串口数据收发",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "serial",
    Version = "1.1.0"
)]
[InputPort("Data", "发送数据", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "接收数据", PortDataType.Any)]
[OperatorParam("ProfileId", "串口Profile", "string", DefaultValue = "")]
[OperatorParam("PortName", "串口号", "string", DefaultValue = "COM1")]
[OperatorParam("BaudRate", "波特率", "enum", DefaultValue = "9600", Options = new[] { "9600|9600", "19200|19200", "38400|38400", "57600|57600", "115200|115200" })]
[OperatorParam("DataBits", "数据位", "int", DefaultValue = 8, Min = 5, Max = 8)]
[OperatorParam("StopBits", "停止位", "enum", DefaultValue = "One", Options = new[] { "One|1", "OnePointFive|1.5", "Two|2" })]
[OperatorParam("Parity", "校验位", "enum", DefaultValue = "None", Options = new[] { "None|无", "Odd|奇校验", "Even|偶校验" })]
[OperatorParam("SendData", "发送内容", "string", DefaultValue = "")]
[OperatorParam("Encoding", "编码", "enum", DefaultValue = "UTF8", Options = new[] { "UTF8|UTF-8", "ASCII|ASCII", "HEX|HEX" })]
[OperatorParam("TimeoutMs", "超时(毫秒)", "int", DefaultValue = 3000, Min = 100, Max = 30000)]
[OperatorParam("ResponseWaitMs", "响应等待(毫秒)", "int", DefaultValue = 100, Min = 0, Max = 30000)]
public class SerialCommunicationOperator : OperatorBase
{
    internal const string InvalidStopBitsCode = "SERIAL_STOP_BITS_INVALID";
    internal const string InvalidParityCode = "SERIAL_PARITY_INVALID";
    internal const string InvalidEncodingCode = "SERIAL_ENCODING_INVALID";
    internal const string InvalidHexCode = "SERIAL_HEX_PAYLOAD_INVALID";

    private const int DefaultResponseWaitMs = 100;
    private const int ResponsePollIntervalMs = 10;

    private readonly IExecutionResourceProfileResolver _resourceProfileResolver;
    private readonly Func<SerialPortConnectionSettings, ISerialPortConnection> _connectionFactory;

    public override OperatorType OperatorType => OperatorType.SerialCommunication;

    public SerialCommunicationOperator(ILogger<SerialCommunicationOperator> logger)
        : this(
            logger,
            DenyAllExecutionResourceProfileResolver.Instance,
            settings => new SerialPortConnection(settings))
    {
    }

    public SerialCommunicationOperator(
        ILogger<SerialCommunicationOperator> logger,
        IExecutionResourceProfileResolver resourceProfileResolver)
        : this(logger, resourceProfileResolver, settings => new SerialPortConnection(settings))
    {
    }

    internal SerialCommunicationOperator(
        ILogger<SerialCommunicationOperator> logger,
        IExecutionResourceProfileResolver resourceProfileResolver,
        Func<SerialPortConnectionSettings, ISerialPortConnection> connectionFactory)
        : base(logger)
    {
        _resourceProfileResolver = resourceProfileResolver ??
            throw new ArgumentNullException(nameof(resourceProfileResolver));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var resolution = _resourceProfileResolver.ResolveSerial(profileId);
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return OperatorExecutionOutput.Failure($"{resolution.Code}: {resolution.Message}");
        }

        var resource = resolution.Resource;
        var portName = resource.PortName;
        var baudRate = resource.BaudRate;
        var dataBits = resource.DataBits;
        var stopBitsStr = resource.StopBits;
        var parityStr = resource.Parity;
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", 3000);
        var responseWaitMs = Math.Clamp(GetIntParam(@operator, "ResponseWaitMs", DefaultResponseWaitMs), 0, timeoutMs);
        // 优先使用上游连线到 "发送数据" 端口的动态值，否则回退到参数面板中的静态内容。
        var sendData = ResolveSendData(@operator, inputs);
        var encoding = GetStringParam(@operator, "Encoding", "UTF8");

        if (!TryParseStopBits(stopBitsStr, out var stopBits))
        {
            return OperatorExecutionOutput.Failure(
                $"{InvalidStopBitsCode}: StopBits must be One, OnePointFive, or Two.");
        }

        if (!TryParseParity(parityStr, out var parity))
        {
            return OperatorExecutionOutput.Failure(
                $"{InvalidParityCode}: Parity must be None, Odd, or Even.");
        }

        if (!TryResolveEncoding(encoding, out var textEncoding))
        {
            return OperatorExecutionOutput.Failure(
                $"{InvalidEncodingCode}: Encoding must be UTF8, ASCII, or HEX.");
        }

        // 验证数据位范围
        if (dataBits < 5 || dataBits > 8)
        {
            return OperatorExecutionOutput.Failure("数据位必须在 5-8 之间");
        }

        byte[]? bytesToSend = null;
        if (!string.IsNullOrEmpty(sendData))
        {
            if (encoding == "HEX")
            {
                if (!TryParseHexPayload(sendData, out bytesToSend, out var hexError))
                {
                    return OperatorExecutionOutput.Failure(hexError);
                }
            }
            else
            {
                bytesToSend = textEncoding!.GetBytes(sendData);
            }
        }

        using var port = _connectionFactory(new SerialPortConnectionSettings(
            portName,
            baudRate,
            parity,
            dataBits,
            stopBits,
            timeoutMs));

        try
        {
            await port.OpenAsync(cancellationToken);

            // 发送数据
            if (bytesToSend is not null)
            {
                await port.WriteAsync(bytesToSend, cancellationToken);
                Logger.LogInformation("[SerialCommunication] 已发送 {Bytes} 字节到 {Port}", bytesToSend.Length, portName);
            }

            // 接收响应
            string response = "";
            var bytesAvailable = await WaitForAvailableBytesAsync(port, responseWaitMs, cancellationToken);
            var bytesReceived = 0;
            if (bytesAvailable > 0)
            {
                byte[] buffer = new byte[bytesAvailable];
                bytesReceived = await port.ReadAsync(buffer, cancellationToken);

                if (encoding == "HEX")
                {
                    response = BitConverter.ToString(buffer, 0, bytesReceived).Replace("-", " ");
                }
                else
                {
                    response = textEncoding!.GetString(buffer, 0, bytesReceived);
                }

                Logger.LogInformation("[SerialCommunication] 从 {Port} 接收 {Bytes} 字节", portName, bytesReceived);
            }

            var output = new Dictionary<string, object>
            {
                { "Response", response },
                { "BytesReceived", bytesReceived },
                { "Port", portName },
                { "BaudRate", baudRate },
                { "Success", true }
            };

            return OperatorExecutionOutput.Success(output);
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex, "[SerialCommunication] 串口 {Port} 访问被拒绝", portName);
            return OperatorExecutionOutput.Failure($"串口 {portName} 访问被拒绝，请检查串口是否被其他程序占用");
        }
        catch (IOException ex)
        {
            Logger.LogError(ex, "[SerialCommunication] 串口 {Port} IO 错误", portName);
            return OperatorExecutionOutput.Failure($"串口 {portName} IO 错误: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            Logger.LogError(ex, "[SerialCommunication] 串口 {Port} 操作超时", portName);
            return OperatorExecutionOutput.Failure($"串口 {portName} 操作超时");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[SerialCommunication] 串口通信失败: {Port}", portName);
            return OperatorExecutionOutput.Failure($"串口通信失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析实际要发送的数据：优先读取上游连线（"Data" 端口，以及展平合并进 inputs 的常见结果键），
    /// 无连线时回退到参数面板中的静态 <c>SendData</c>。这样视觉结果才能真正通过画布连线发往串口设备。
    /// </summary>
    private string ResolveSendData(Operator @operator, Dictionary<string, object>? inputs)
    {
        var staticValue = GetStringParam(@operator, "SendData", "");

        if (inputs == null || inputs.Count == 0)
        {
            return staticValue;
        }

        // 优先级：显式 "Data" 端口 > 判断结果 > 通用 Value。与 PLC 算子的 ResolveWriteValue 保持一致。
        foreach (var key in new[] { "Data", "JudgmentValue", "Value" })
        {
            if (inputs.TryGetValue(key, out var value) && value != null)
            {
                var stringValue = value.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(stringValue))
                {
                    Logger.LogDebug("[SerialCommunication] 使用上游动态发送数据: Key={Key}", key);
                    return stringValue;
                }
            }
        }

        return staticValue;
    }

    private static async Task<int> WaitForAvailableBytesAsync(
        ISerialPortConnection port,
        int responseWaitMs,
        CancellationToken cancellationToken)
    {
        var available = port.BytesToRead;
        if (available > 0 || responseWaitMs <= 0)
        {
            return available;
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(responseWaitMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var remainingMs = (int)Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
            if (remainingMs == 0)
            {
                break;
            }

            await Task.Delay(Math.Min(ResponsePollIntervalMs, remainingMs), cancellationToken);
            available = port.BytesToRead;
            if (available > 0)
            {
                return available;
            }
        }

        return port.BytesToRead;
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var profileId = GetStringParam(@operator, "ProfileId", string.Empty);
        var resolution = _resourceProfileResolver.ResolveSerial(profileId);
        if (!resolution.Resolved || resolution.Resource == null)
        {
            return ValidationResult.Invalid($"{resolution.Code}: {resolution.Message}");
        }

        var resource = resolution.Resource;
        var portName = resource.PortName;
        var baudRate = resource.BaudRate;
        var dataBits = resource.DataBits;
        var stopBits = resource.StopBits;
        var parity = resource.Parity;
        var encoding = GetStringParam(@operator, "Encoding", "UTF8");
        var sendData = GetStringParam(@operator, "SendData", string.Empty);

        if (string.IsNullOrWhiteSpace(portName))
        {
            return ValidationResult.Invalid("串口号不能为空");
        }

        if (baudRate <= 0)
        {
            return ValidationResult.Invalid("波特率必须大于 0");
        }

        if (dataBits < 5 || dataBits > 8)
        {
            return ValidationResult.Invalid("数据位必须在 5-8 之间");
        }

        if (!TryParseStopBits(stopBits, out _))
        {
            return ValidationResult.Invalid(
                $"{InvalidStopBitsCode}: StopBits must be One, OnePointFive, or Two.");
        }

        if (!TryParseParity(parity, out _))
        {
            return ValidationResult.Invalid(
                $"{InvalidParityCode}: Parity must be None, Odd, or Even.");
        }

        if (!TryResolveEncoding(encoding, out _))
        {
            return ValidationResult.Invalid(
                $"{InvalidEncodingCode}: Encoding must be UTF8, ASCII, or HEX.");
        }

        if (encoding == "HEX" &&
            !string.IsNullOrEmpty(sendData) &&
            !TryParseHexPayload(sendData, out _, out var hexError))
        {
            return ValidationResult.Invalid(hexError);
        }

        return ValidationResult.Valid();
    }

    private static bool TryParseStopBits(string value, out StopBits stopBits)
    {
        stopBits = value switch
        {
            "One" => StopBits.One,
            "OnePointFive" => StopBits.OnePointFive,
            "Two" => StopBits.Two,
            _ => default
        };
        return value is "One" or "OnePointFive" or "Two";
    }

    private static bool TryParseParity(string value, out Parity parity)
    {
        parity = value switch
        {
            "None" => Parity.None,
            "Odd" => Parity.Odd,
            "Even" => Parity.Even,
            _ => default
        };
        return value is "None" or "Odd" or "Even";
    }

    private static bool TryResolveEncoding(string value, out Encoding? encoding)
    {
        encoding = value switch
        {
            "UTF8" => Encoding.UTF8,
            "ASCII" => Encoding.ASCII,
            "HEX" => null,
            _ => null
        };
        return value is "UTF8" or "ASCII" or "HEX";
    }

    internal static bool TryParseHexPayload(
        string payload,
        out byte[] bytes,
        out string error)
    {
        var normalized = payload.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        bytes = [];

        if (normalized.Length % 2 != 0)
        {
            error = $"{InvalidHexCode}: HEX payload must contain an even number of hexadecimal characters.";
            return false;
        }

        if (normalized.Any(character =>
                character is not (>= '0' and <= '9') and
                not (>= 'A' and <= 'F') and
                not (>= 'a' and <= 'f')))
        {
            error = $"{InvalidHexCode}: HEX payload contains an invalid byte token.";
            return false;
        }

        bytes = new byte[normalized.Length / 2];
        for (var index = 0; index < normalized.Length; index += 2)
        {
            bytes[index / 2] = Convert.ToByte(normalized.Substring(index, 2), 16);
        }

        error = string.Empty;
        return true;
    }

    internal readonly record struct SerialPortConnectionSettings(
        string PortName,
        int BaudRate,
        Parity Parity,
        int DataBits,
        StopBits StopBits,
        int TimeoutMs);

    internal interface ISerialPortConnection : IDisposable
    {
        int BytesToRead { get; }

        Task OpenAsync(CancellationToken cancellationToken);

        Task WriteAsync(byte[] bytes, CancellationToken cancellationToken);

        Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken);
    }

    private sealed class SerialPortConnection : ISerialPortConnection
    {
        private readonly SerialPort _port;

        public SerialPortConnection(SerialPortConnectionSettings settings)
        {
            _port = new SerialPort(
                settings.PortName,
                settings.BaudRate,
                settings.Parity,
                settings.DataBits,
                settings.StopBits)
            {
                ReadTimeout = settings.TimeoutMs,
                WriteTimeout = settings.TimeoutMs
            };
        }

        public int BytesToRead => _port.BytesToRead;

        public Task OpenAsync(CancellationToken cancellationToken)
        {
            return Task.Run(_port.Open, cancellationToken);
        }

        public Task WriteAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            return _port.BaseStream.WriteAsync(bytes.AsMemory(0, bytes.Length), cancellationToken).AsTask();
        }

        public async Task<int> ReadAsync(byte[] buffer, CancellationToken cancellationToken)
        {
            return await _port.BaseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
        }

        public void Dispose()
        {
            _port.Dispose();
        }
    }
}
