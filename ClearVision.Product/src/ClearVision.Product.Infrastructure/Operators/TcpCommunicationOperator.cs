// TcpCommunicationOperator.cs
// TCP client operator backed by the global TCP device manager.
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// TCP/IP通信算子 - 复用全局 TCP / 机器人通讯 Profile，兼容旧客户端参数。
/// </summary>
[OperatorMeta(
    DisplayName = "TCP通信",
    Description = "TCP/IP网络通信",
    Category = "通信",
    IconName = "tcp",
    Keywords = new[] { "TCP", "网络", "Socket", "通信", "发送", "接收", "IP", "Communication" }
)]
[InputPort("Data", "数据", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "响应", PortDataType.String)]
[OutputPort("Status", "状态", PortDataType.Boolean)]
[OperatorParam("ProfileId", "全局Profile", "string", DefaultValue = "")]
[OperatorParam("UseGlobalProfile", "使用全局Profile", "bool", DefaultValue = false)]
[OperatorParam("Mode", "模式", "enum", DefaultValue = "Client", Options = new[] { "Client|客户端", "Server|服务器" })]
[OperatorParam("IpAddress", "IP地址", "string", DefaultValue = "127.0.0.1")]
[OperatorParam("Port", "端口", "int", DefaultValue = 8080, Min = 1, Max = 65535)]
[OperatorParam("SendData", "发送数据", "string", DefaultValue = "")]
[OperatorParam("UseFixedSendData", "固定发送数据", "bool", DefaultValue = false)]
[OperatorParam("PayloadTemplate", "报文模板", "string", DefaultValue = "")]
[OperatorParam("WaitResponse", "等待响应", "bool", DefaultValue = true)]
[OperatorParam("ResponseTimeoutMs", "响应超时(ms)", "int", DefaultValue = 5000, Min = 100, Max = 600000)]
[OperatorParam("Timeout", "超时(ms)", "int", DefaultValue = 5000, Min = 100, Max = 600000)]
[OperatorParam("Encoding", "编码", "enum", DefaultValue = "UTF8", Options = new[] { "UTF8|UTF-8", "ASCII|ASCII", "GBK|GBK", "HEX|HEX" })]
public class TcpCommunicationOperator : OperatorBase
{
    private readonly ITcpDeviceManager _tcpDeviceManager;

    public override OperatorType OperatorType => OperatorType.TcpCommunication;

    public TcpCommunicationOperator(ILogger<TcpCommunicationOperator> logger)
        : this(logger, new TcpDeviceManager(null, NullLogger<TcpDeviceManager>.Instance))
    {
    }

    public TcpCommunicationOperator(
        ILogger<TcpCommunicationOperator> logger,
        ITcpDeviceManager tcpDeviceManager)
        : base(logger)
    {
        _tcpDeviceManager = tcpDeviceManager;
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        inputs ??= new Dictionary<string, object>();
        inputs.TryGetValue("Data", out var inputData);

        var mode = GetStringParam(@operator, "Mode", "Client");
        var profileId = GetStringParam(@operator, "ProfileId", string.Empty).Trim();
        var useGlobalProfile = GetBoolParam(@operator, "UseGlobalProfile", false);
        var ipAddress = GetStringParam(@operator, "IpAddress", "127.0.0.1");
        var port = GetIntParam(@operator, "Port", 8080, 1, 65535);
        var sendData = GetStringParam(@operator, "SendData", string.Empty);
        var payloadTemplate = GetStringParam(@operator, "PayloadTemplate", string.Empty);
        var useFixedSendData = GetBoolParam(@operator, "UseFixedSendData", false);
        var waitResponse = GetBoolParam(@operator, "WaitResponse", true);
        var responseTimeoutMs = GetIntParam(@operator, "ResponseTimeoutMs", 5000, 100, 600000);
        var timeout = GetIntParam(@operator, "Timeout", 5000, 100, 600000);
        var encoding = TcpCommunicationProfile.NormalizeEncoding(GetStringParam(@operator, "Encoding", "UTF8"));
        var payload = ResolvePayload(inputData, sendData, payloadTemplate, useFixedSendData);

        TcpDeviceSendResult result;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            result = await _tcpDeviceManager.SendAsync(
                profileId,
                new TcpDeviceSendRequest(
                    payload,
                    IsHexEncoding(encoding),
                    waitResponse,
                    responseTimeoutMs),
                cancellationToken);
        }
        else
        {
            if (useGlobalProfile)
            {
                return OperatorExecutionOutput.Failure("启用全局 Profile 时必须配置 ProfileId。");
            }

            if (string.Equals(mode, TcpCommunicationProfile.ModeServer, StringComparison.OrdinalIgnoreCase))
            {
                return OperatorExecutionOutput.Failure("Server 监听请在全局 TCP 通讯页启动，算子只负责通过已配置 Profile 发送/等待响应。");
            }

            var legacyProfile = new TcpCommunicationProfile
            {
                Id = BuildLegacyProfileId(ipAddress, port),
                Name = $"Legacy {ipAddress}:{port}",
                Enabled = true,
                Mode = TcpCommunicationProfile.ModeClient,
                RemoteHost = ipAddress,
                RemotePort = port,
                Encoding = encoding,
                FrameMode = IsHexEncoding(encoding)
                    ? TcpCommunicationProfile.FrameModeHex
                    : TcpCommunicationProfile.FrameModeRaw,
                TimeoutMs = timeout,
                Reconnect = true
            };

            result = await _tcpDeviceManager.SendTransientAsync(
                legacyProfile,
                new TcpDeviceSendRequest(
                    payload,
                    IsHexEncoding(encoding),
                    waitResponse,
                    responseTimeoutMs),
                cancellationToken);
        }

        if (!result.Success)
        {
            return OperatorExecutionOutput.Failure(result.Message);
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            { "Response", result.Response },
            { "Status", result.Success },
            { "Mode", string.IsNullOrWhiteSpace(profileId) ? mode : "Profile" },
            { "ProfileId", profileId },
            { "IpAddress", ipAddress },
            { "Port", port }
        });
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var profileId = GetStringParam(@operator, "ProfileId", string.Empty).Trim();
        var useGlobalProfile = GetBoolParam(@operator, "UseGlobalProfile", false);
        var host = GetStringParam(@operator, "IpAddress", "127.0.0.1");
        var port = GetIntParam(@operator, "Port", 8080);
        var timeout = GetIntParam(@operator, "Timeout", 5000);
        var responseTimeoutMs = GetIntParam(@operator, "ResponseTimeoutMs", 5000);
        var mode = GetStringParam(@operator, "Mode", "Client");
        var encoding = TcpCommunicationProfile.NormalizeEncoding(GetStringParam(@operator, "Encoding", "UTF8"));

        if (useGlobalProfile && string.IsNullOrWhiteSpace(profileId))
        {
            return ValidationResult.Invalid("启用全局 Profile 时必须配置 ProfileId。");
        }

        if (mode != TcpCommunicationProfile.ModeClient && mode != TcpCommunicationProfile.ModeServer)
        {
            return ValidationResult.Invalid("模式必须是 Client 或 Server");
        }

        if (string.IsNullOrWhiteSpace(profileId) && mode == TcpCommunicationProfile.ModeServer)
        {
            return ValidationResult.Invalid("Server 监听请在全局 TCP 通讯页启动。");
        }

        if (string.IsNullOrWhiteSpace(profileId))
        {
            if (@operator.Parameters.Any(p => p.Name == "IpAddress") && string.IsNullOrWhiteSpace(host))
            {
                return ValidationResult.Invalid("主机地址不能为空");
            }

            if (port < 1 || port > 65535)
            {
                return ValidationResult.Invalid("端口号必须在 1-65535 之间");
            }
        }

        if (timeout is < TcpCommunicationProfile.MinTimeoutMs or > TcpCommunicationProfile.MaxTimeoutMs)
        {
            return ValidationResult.Invalid($"超时时间必须在 {TcpCommunicationProfile.MinTimeoutMs}-{TcpCommunicationProfile.MaxTimeoutMs} ms 之间");
        }

        if (responseTimeoutMs is < TcpCommunicationProfile.MinTimeoutMs or > TcpCommunicationProfile.MaxTimeoutMs)
        {
            return ValidationResult.Invalid($"响应超时时间必须在 {TcpCommunicationProfile.MinTimeoutMs}-{TcpCommunicationProfile.MaxTimeoutMs} ms 之间");
        }

        if (encoding is not (
            TcpCommunicationProfile.EncodingUtf8 or
            TcpCommunicationProfile.EncodingAscii or
            TcpCommunicationProfile.EncodingGbk or
            TcpCommunicationProfile.EncodingHex))
        {
            return ValidationResult.Invalid("编码必须是 UTF8、ASCII、GBK 或 HEX");
        }

        return ValidationResult.Valid();
    }

    private static string ResolvePayload(
        object? inputData,
        string sendData,
        string payloadTemplate,
        bool useFixedSendData)
    {
        var inputText = inputData?.ToString() ?? string.Empty;
        if (!string.IsNullOrEmpty(payloadTemplate))
        {
            return payloadTemplate
                .Replace("{Data}", inputText, StringComparison.OrdinalIgnoreCase)
                .Replace("{SendData}", sendData ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        if (!useFixedSendData && inputData != null)
        {
            return inputText;
        }

        return sendData ?? string.Empty;
    }

    private static bool IsHexEncoding(string encoding)
    {
        return string.Equals(
            TcpCommunicationProfile.NormalizeEncoding(encoding),
            TcpCommunicationProfile.EncodingHex,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLegacyProfileId(string ipAddress, int port)
    {
        return $"legacy-{ipAddress.Trim()}-{port}".Replace(":", "-", StringComparison.Ordinal);
    }
}
