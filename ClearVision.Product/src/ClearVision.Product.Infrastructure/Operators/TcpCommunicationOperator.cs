// TcpCommunicationOperator.cs
// TCP client operator backed by the global TCP device manager.
// 作者：蘅芜君

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
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
    CategoryId = OperatorCategoryId.Communication,
    IconName = "tcp",
    Keywords = new[] { "TCP", "网络", "Socket", "通信", "发送", "接收", "IP", "Communication" }
)]
[InputPort("Data", "数据", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "响应", PortDataType.String)]
[OutputPort("Status", "状态", PortDataType.Boolean)]
[OutputPort("NormalizedResponse", "Normalized Response", PortDataType.String)]
[OperatorParam("ProfileId", "全局Profile", "string", DefaultValue = "")]
[OperatorParam("UseGlobalProfile", "使用全局Profile", "bool", DefaultValue = false)]
[OperatorParam("Mode", "模式", "enum", DefaultValue = "Client", Options = new[] { "Client|客户端", "Server|服务器" })]
[OperatorParam("IpAddress", "IP地址", "string", DefaultValue = "127.0.0.1")]
[OperatorParam("Port", "端口", "int", DefaultValue = 8080, Min = 1, Max = 65535)]
[OperatorParam("SendData", "发送数据", "string", DefaultValue = "")]
[OperatorParam("UseFixedSendData", "固定发送数据", "bool", DefaultValue = false)]
[OperatorParam("PayloadTemplate", "报文模板", "string", DefaultValue = "")]
[OperatorParam("DecodeEscapeSequences", "Decode Escape Sequences", "bool", Description = "启用后解析发送报文、分隔符和匹配条件中的 \\r、\\n、\\xHH 等转义序列。", DefaultValue = false)]
[OperatorParam("WaitResponse", "等待响应", "bool", DefaultValue = true)]
[OperatorParam("ResponseTimeoutMs", "响应超时(ms)", "int", DefaultValue = 5000, Min = 100, Max = 600000)]
[OperatorParam("Timeout", "超时(ms)", "int", DefaultValue = 5000, Min = 100, Max = 600000)]
[OperatorParam("Encoding", "编码", "enum", DefaultValue = "UTF8", Options = new[] { "UTF8|UTF-8", "ASCII|ASCII", "GBK|GBK", "HEX|HEX" })]
[OutputPort("RequestPayload", "Request Payload", PortDataType.String)]
[OutputPort("ParseSuccess", "Parse Success", PortDataType.Boolean)]
[OutputPort("ParsedValue", "Parsed Value", PortDataType.Any)]
[OutputPort("ParsedFields", "Parsed Fields", PortDataType.Any)]
[OutputPort("ParseError", "Parse Error", PortDataType.String)]
[OutputPort("MissingResponseFields", "Missing Response Fields", PortDataType.Any)]
[OutputPort("ResponseAccepted", "Response Accepted", PortDataType.Boolean)]
[OutputPort("ResponseMatchError", "Response Match Error", PortDataType.String)]
[OutputPort("ResponseMatchValue", "Response Match Value", PortDataType.String)]
[OperatorParam("FailOnUnresolvedPayloadPlaceholder", "Fail On Unresolved Payload Placeholder", "bool", Description = "启用后，请求报文模板中存在未解析占位符时执行失败。", DefaultValue = true)]
[OperatorParam("FailOnParseError", "Fail On Parse Error", "bool", Description = "启用后，响应解析失败时执行失败。", DefaultValue = false)]
[OperatorParam("FailOnUnexpectedResponse", "Fail On Unexpected Response", "bool", Description = "启用后，响应未满足期望或命中拒绝条件时执行失败。", DefaultValue = false)]
[OperatorParam("ResponseParseMode", "Response Parse Mode", "enum", Description = "选择响应解析方式：不解析、JSON路径、键值对、正则、分隔符或固定宽度。", DefaultValue = "None", Options = new[] { "None|None", "JsonPath|JSON path", "KeyValue|Key-value", "Regex|Regex", "Delimited|Delimited", "FixedWidth|Fixed width" })]
[OperatorParam("ResponseFieldName", "Response Field Name", "string", Description = "单字段解析目标，例如 JSONPath 或解析字段名。", DefaultValue = "")]
[OperatorParam("ResponseFieldNames", "Response Field Names", "string", Description = "多字段解析名称列表，通常用逗号分隔。", DefaultValue = "")]
[OperatorParam("RequiredResponseFields", "Required Response Fields", "string", Description = "必需响应字段列表，缺失时记录 MissingResponseFields。", DefaultValue = "")]
[OperatorParam("ResponseFieldWidths", "Response Field Widths", "string", Description = "固定宽度解析时每个字段的字符宽度列表。", DefaultValue = "")]
[OperatorParam("ResponseRegexPattern", "Response Regex Pattern", "string", Description = "正则解析或正则匹配使用的表达式。", DefaultValue = "")]
[OperatorParam("ResponseRegexIgnoreCase", "Response Regex Ignore Case", "bool", Description = "启用后，响应正则解析忽略大小写。", DefaultValue = false)]
[OperatorParam("ResponseKeyValuePairDelimiter", "Response Key-Value Pair Delimiter", "string", Description = "键值对响应中不同键值对之间的主分隔符。", DefaultValue = ";")]
[OperatorParam("ResponseKeyValuePairDelimiters", "Additional Key-Value Pair Delimiters", "string", Description = "键值对响应的附加分隔符，多个值用 | 分隔。", DefaultValue = "")]
[OperatorParam("ResponseKeyValueSeparator", "Response Key-Value Separator", "string", Description = "键和值之间的主分隔符。", DefaultValue = "=")]
[OperatorParam("ResponseKeyValueSeparators", "Additional Key-Value Separators", "string", Description = "键和值之间的附加分隔符，多个值用 | 分隔。", DefaultValue = "")]
[OperatorParam("ResponseDelimiter", "Response Delimiter", "string", Description = "分隔符解析时使用的主分隔符。", DefaultValue = ",")]
[OperatorParam("ResponseDelimiters", "Additional Response Delimiters", "string", Description = "分隔符解析时使用的附加分隔符，多个值用 | 分隔。", DefaultValue = "")]
[OperatorParam("ResponseIndex", "Response Index", "int", Description = "分隔符解析时选取的字段索引，从 0 开始。", DefaultValue = 0, Min = 0, Max = 4096)]
[OperatorParam("TrimResponseBeforeParse", "Trim Response Before Parse", "bool", Description = "解析前先裁剪响应两端空白字符。", DefaultValue = false)]
[OperatorParam("ResponseStartMarker", "Response Start Marker", "string", Description = "响应帧起始标记，配置后仅截取标记后的内容。", DefaultValue = "")]
[OperatorParam("ResponseEndMarker", "Response End Marker", "string", Description = "响应帧结束标记，配置后仅截取标记前的内容。", DefaultValue = "")]
[OperatorParam("FailOnMissingResponseFrame", "Fail On Missing Response Frame", "bool", Description = "启用后，响应未找到配置的起止标记时执行失败。", DefaultValue = false)]
[OperatorParam("ExpectedResponse", "Expected Response", "string", Description = "期望响应内容；配置后用于判断响应是否通过。", DefaultValue = "")]
[OperatorParam("RejectedResponse", "Rejected Response", "string", Description = "拒绝响应内容；命中后 ResponseAccepted 为 false。", DefaultValue = "")]
[OperatorParam("ResponseMatchMode", "Response Match Mode", "enum", Description = "响应判断方式：包含、等于、开头、结尾或正则。", DefaultValue = "Contains", Options = new[] { "Contains|Contains", "Equals|Equals", "StartsWith|Starts with", "EndsWith|Ends with", "Regex|Regex" })]
[OperatorParam("ResponseMatchIgnoreCase", "Response Match Ignore Case", "bool", Description = "启用后，期望/拒绝响应匹配忽略大小写。", DefaultValue = false)]
[OperatorParam("ResponseMatchSource", "Response Match Source", "enum", Description = "选择响应判断的数据来源：原始响应、归一化响应或解析值。", DefaultValue = "Response", Options = new[] { "Response|Raw response", "NormalizedResponse|Normalized response", "ParsedValue|Parsed value" })]
public class TcpCommunicationOperator : OperatorBase
{
    private static readonly Regex PayloadPlaceholderRegex = new(
        @"\{(?<path>[A-Za-z_][A-Za-z0-9_.-]*)\}",
        RegexOptions.Compiled);

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

        var mode = NormalizePendingString(GetStringParam(@operator, "Mode", "Client"));
        var profileId = NormalizePendingString(GetStringParam(@operator, "ProfileId", string.Empty)).Trim();
        var useGlobalProfile = GetBoolParam(@operator, "UseGlobalProfile", false);
        var ipAddress = GetStringParam(@operator, "IpAddress", "127.0.0.1");
        var port = GetIntParam(@operator, "Port", 8080, 1, 65535);
        var sendData = NormalizePendingString(GetStringParam(@operator, "SendData", string.Empty));
        var payloadTemplate = NormalizePendingString(GetStringParam(@operator, "PayloadTemplate", string.Empty));
        var useFixedSendData = GetBoolParam(@operator, "UseFixedSendData", false);
        var decodeEscapeSequences = GetBoolParam(@operator, "DecodeEscapeSequences", false);
        var waitResponse = GetBoolParam(@operator, "WaitResponse", true);
        var responseTimeoutMs = GetIntParam(@operator, "ResponseTimeoutMs", 5000, 100, 600000);
        var timeout = GetIntParam(@operator, "Timeout", 5000, 100, 600000);
        var encoding = TcpCommunicationProfile.NormalizeEncoding(GetStringParam(@operator, "Encoding", "UTF8"));
        var failOnParseError = GetBoolParam(@operator, "FailOnParseError", false);
        var failOnUnresolvedPayloadPlaceholder = GetBoolParam(@operator, "FailOnUnresolvedPayloadPlaceholder", true);
        var failOnUnexpectedResponse = GetBoolParam(@operator, "FailOnUnexpectedResponse", false);
        var expectedResponse = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "ExpectedResponse", string.Empty)), decodeEscapeSequences);
        var rejectedResponse = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "RejectedResponse", string.Empty)), decodeEscapeSequences);
        var responseMatchMode = NormalizeResponseMatchMode(NormalizePendingString(GetStringParam(@operator, "ResponseMatchMode", "Contains")));
        var responseMatchSource = NormalizeResponseMatchSource(NormalizePendingString(GetStringParam(@operator, "ResponseMatchSource", "Response")));
        var responseMatchIgnoreCase = GetBoolParam(@operator, "ResponseMatchIgnoreCase", false);
        var responseRegexIgnoreCase = GetBoolParam(@operator, "ResponseRegexIgnoreCase", false);
        var trimResponseBeforeParse = GetBoolParam(@operator, "TrimResponseBeforeParse", false);
        var responseStartMarker = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "ResponseStartMarker", string.Empty)), decodeEscapeSequences);
        var responseEndMarker = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "ResponseEndMarker", string.Empty)), decodeEscapeSequences);
        var failOnMissingResponseFrame = GetBoolParam(@operator, "FailOnMissingResponseFrame", false);
        var validation = ValidateParameters(@operator);
        if (!validation.IsValid)
        {
            return OperatorExecutionOutput.Failure(string.Join("; ", validation.Errors));
        }

        if (!TryResolvePayload(inputs, inputData, sendData, payloadTemplate, useFixedSendData, failOnUnresolvedPayloadPlaceholder, decodeEscapeSequences, out var payload, out var payloadError))
        {
            return OperatorExecutionOutput.Failure(payloadError);
        }

        TcpDeviceSendResult result;
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            result = await _tcpDeviceManager.SendAsync(
                profileId,
                new TcpDeviceSendRequest(
                    payload,
                    false,
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

        var responseNormalization = waitResponse
            ? NormalizeResponse(
                result.Response,
                trimResponseBeforeParse,
                responseStartMarker,
                responseEndMarker,
                failOnMissingResponseFrame)
            : ResponseNormalizationResult.Ok(result.Response, false);
        if (!responseNormalization.Success)
        {
            return new OperatorExecutionOutput
            {
                IsSuccess = false,
                ErrorMessage = responseNormalization.Error,
                OutputData = BuildResponseFrameFailureOutput(
                    payload,
                    result.Response,
                    responseNormalization,
                    expectedResponse,
                    rejectedResponse,
                    responseMatchMode,
                    responseMatchSource,
                    responseMatchIgnoreCase,
                    responseRegexIgnoreCase,
                    decodeEscapeSequences,
                    trimResponseBeforeParse,
                    responseStartMarker,
                    responseEndMarker,
                    profileId,
                    ipAddress,
                    port,
                    string.IsNullOrWhiteSpace(profileId) ? mode : "Profile")
            };
        }

        var parseResult = waitResponse
            ? ParseResponse(@operator, responseNormalization.Response)
            : ResponseParseResult.Ok(
                responseNormalization.Response,
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
        var responseJudgment = waitResponse
            ? EvaluateResponseJudgment(
                result.Response,
                responseNormalization.Response,
                parseResult,
                expectedResponse,
                rejectedResponse,
                responseMatchMode,
                responseMatchSource,
                responseMatchIgnoreCase)
            : ResponseJudgment.Ok(responseNormalization.Response);
        var outputData = new Dictionary<string, object>
        {
            { "RequestPayload", payload },
            { "Response", result.Response },
            { "NormalizedResponse", responseNormalization.Response },
            { "Status", result.Success && (!failOnParseError || parseResult.Success) && responseJudgment.Accepted },
            { "ParseSuccess", parseResult.Success },
            { "ParsedValue", parseResult.Value ?? string.Empty },
            { "ParsedFields", parseResult.Fields },
            { "ParseError", parseResult.Error },
            { "MissingResponseFields", parseResult.MissingFields },
            { "ResponseFrameFound", responseNormalization.FrameFound },
            { "ResponseFrameError", responseNormalization.Error },
            { "ResponseAccepted", responseJudgment.Accepted },
            { "ResponseMatchError", responseJudgment.Error },
            { "ResponseMatchValue", responseJudgment.Value },
            { "ExpectedResponse", expectedResponse },
            { "RejectedResponse", rejectedResponse },
            { "ResponseMatchMode", responseMatchMode },
            { "ResponseMatchSource", responseMatchSource },
            { "ResponseMatchIgnoreCase", responseMatchIgnoreCase },
            { "ResponseRegexIgnoreCase", responseRegexIgnoreCase },
            { "DecodeEscapeSequences", decodeEscapeSequences },
            { "TrimResponseBeforeParse", trimResponseBeforeParse },
            { "ResponseStartMarker", responseStartMarker },
            { "ResponseEndMarker", responseEndMarker },
            { "Mode", string.IsNullOrWhiteSpace(profileId) ? mode : "Profile" },
            { "ProfileId", profileId },
            { "IpAddress", ipAddress },
            { "Port", port }
        };

        if (waitResponse && failOnParseError && !parseResult.Success)
        {
            return new OperatorExecutionOutput
            {
                IsSuccess = false,
                ErrorMessage = string.IsNullOrWhiteSpace(parseResult.Error)
                    ? "TCP response parsing failed."
                    : parseResult.Error,
                OutputData = outputData
            };
        }

        if (waitResponse && failOnUnexpectedResponse && !responseJudgment.Accepted)
        {
            return new OperatorExecutionOutput
            {
                IsSuccess = false,
                ErrorMessage = string.IsNullOrWhiteSpace(responseJudgment.Error)
                    ? "TCP response did not match expected response rule."
                    : responseJudgment.Error,
                OutputData = outputData
            };
        }

        return OperatorExecutionOutput.Success(outputData);
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var profileId = NormalizePendingString(GetStringParam(@operator, "ProfileId", string.Empty)).Trim();
        var useGlobalProfile = GetBoolParam(@operator, "UseGlobalProfile", false);
        var host = NormalizePendingString(GetStringParam(@operator, "IpAddress", "127.0.0.1"));
        var port = GetIntParam(@operator, "Port", 8080);
        var timeout = GetIntParam(@operator, "Timeout", 5000);
        var responseTimeoutMs = GetIntParam(@operator, "ResponseTimeoutMs", 5000);
        var mode = NormalizePendingString(GetStringParam(@operator, "Mode", "Client"));
        var encoding = TcpCommunicationProfile.NormalizeEncoding(GetStringParam(@operator, "Encoding", "UTF8"));
        var waitResponse = GetBoolParam(@operator, "WaitResponse", true);
        var responseParseMode = NormalizeResponseParseMode(
            NormalizePendingString(GetStringParam(@operator, "ResponseParseMode", "None")));
        var responseMatchMode = NormalizeResponseMatchMode(
            NormalizePendingString(GetStringParam(@operator, "ResponseMatchMode", "Contains")));
        var responseMatchSource = NormalizeResponseMatchSource(
            NormalizePendingString(GetStringParam(@operator, "ResponseMatchSource", "Response")));
        var decodeEscapeSequences = GetBoolParam(@operator, "DecodeEscapeSequences", false);

        if (useGlobalProfile && string.IsNullOrWhiteSpace(profileId))
        {
            return ValidationResult.Invalid("启用全局 Profile 时必须配置 ProfileId。");
        }

        if (string.IsNullOrWhiteSpace(profileId) &&
            mode != TcpCommunicationProfile.ModeClient && mode != TcpCommunicationProfile.ModeServer)
        {
            return ValidationResult.Invalid("模式必须是 Client 或 Server");
        }

        if (string.IsNullOrWhiteSpace(profileId) && mode == TcpCommunicationProfile.ModeServer)
        {
            return ValidationResult.Invalid("Server 监听请在全局 TCP 通讯页启动。");
        }

        if (string.IsNullOrWhiteSpace(profileId) &&
            mode == TcpCommunicationProfile.ModeClient &&
            !useGlobalProfile)
        {
            if (@operator.Parameters.Any(p => p.Name == "IpAddress") &&
                OperatorParameterValueSemantics.IsMissing(host))
            {
                return ValidationResult.Invalid("主机地址不能为空");
            }

            if (port < 1 || port > 65535)
            {
                return ValidationResult.Invalid("端口号必须在 1-65535 之间");
            }
        }

        if (string.IsNullOrWhiteSpace(profileId) &&
            mode == TcpCommunicationProfile.ModeClient &&
            !useGlobalProfile &&
            timeout is < TcpCommunicationProfile.MinTimeoutMs or > TcpCommunicationProfile.MaxTimeoutMs)
        {
            return ValidationResult.Invalid($"超时时间必须在 {TcpCommunicationProfile.MinTimeoutMs}-{TcpCommunicationProfile.MaxTimeoutMs} ms 之间");
        }

        if (waitResponse &&
            responseTimeoutMs is < TcpCommunicationProfile.MinTimeoutMs or > TcpCommunicationProfile.MaxTimeoutMs)
        {
            return ValidationResult.Invalid($"响应超时时间必须在 {TcpCommunicationProfile.MinTimeoutMs}-{TcpCommunicationProfile.MaxTimeoutMs} ms 之间");
        }

        if (string.IsNullOrWhiteSpace(profileId) &&
            mode == TcpCommunicationProfile.ModeClient &&
            !useGlobalProfile &&
            encoding is not (
                TcpCommunicationProfile.EncodingUtf8 or
                TcpCommunicationProfile.EncodingAscii or
                TcpCommunicationProfile.EncodingGbk or
                TcpCommunicationProfile.EncodingHex))
        {
            return ValidationResult.Invalid("编码必须是 UTF8、ASCII、GBK 或 HEX");
        }

        if (waitResponse &&
            responseParseMode is not ("none" or "jsonpath" or "keyvalue" or "regex" or "delimited" or "fixedwidth"))
        {
            return ValidationResult.Invalid("ResponseParseMode must be None, JsonPath, KeyValue, Regex, Delimited or FixedWidth.");
        }

        if (waitResponse &&
            responseMatchMode is not ("contains" or "equals" or "startswith" or "endswith" or "regex"))
        {
            return ValidationResult.Invalid("ResponseMatchMode must be Contains, Equals, StartsWith, EndsWith or Regex.");
        }

        if (waitResponse &&
            responseMatchSource is not ("response" or "normalizedresponse" or "parsedvalue"))
        {
            return ValidationResult.Invalid("ResponseMatchSource must be Response, NormalizedResponse or ParsedValue.");
        }

        if (waitResponse && responseParseMode == "regex")
        {
            var pattern = NormalizePendingString(GetStringParam(@operator, "ResponseRegexPattern", string.Empty));
            var regexOptions = GetBoolParam(@operator, "ResponseRegexIgnoreCase", false)
                ? RegexOptions.IgnoreCase
                : RegexOptions.None;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return ValidationResult.Invalid("ResponseRegexPattern is required when ResponseParseMode is Regex.");
            }

            try
            {
                _ = new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(100));
            }
            catch (ArgumentException ex)
            {
                return ValidationResult.Invalid($"ResponseRegexPattern is invalid: {ex.Message}");
            }
        }

        if (waitResponse && responseParseMode == "keyvalue" &&
            (BuildDelimiterOptions(
                 NormalizePendingString(GetStringParam(@operator, "ResponseKeyValuePairDelimiter", ";")),
                 NormalizePendingString(GetStringParam(@operator, "ResponseKeyValuePairDelimiters", string.Empty)),
                 decodeEscapeSequences).Length == 0 ||
             BuildDelimiterOptions(
                 NormalizePendingString(GetStringParam(@operator, "ResponseKeyValueSeparator", "=")),
                 NormalizePendingString(GetStringParam(@operator, "ResponseKeyValueSeparators", string.Empty)),
                 decodeEscapeSequences).Length == 0))
        {
            return ValidationResult.Invalid("KeyValue response parsing requires non-empty pair and key/value delimiters.");
        }

        if (waitResponse && responseParseMode == "delimited" &&
            BuildDelimiterOptions(
                NormalizePendingString(GetStringParam(@operator, "ResponseDelimiter", ",")),
                NormalizePendingString(GetStringParam(@operator, "ResponseDelimiters", string.Empty)),
                decodeEscapeSequences).Length == 0)
        {
            return ValidationResult.Invalid("Delimited response parsing requires a non-empty delimiter.");
        }

        if (waitResponse && responseParseMode == "fixedwidth" &&
            !TryParseFixedWidths(
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldWidths", string.Empty)),
                out _,
                out var widthsError))
        {
            return ValidationResult.Invalid(widthsError);
        }

        if (waitResponse && responseMatchMode == "regex")
        {
            var expectedResponse = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "ExpectedResponse", string.Empty)), decodeEscapeSequences);
            var rejectedResponse = DecodeIfEnabled(NormalizePendingString(GetStringParam(@operator, "RejectedResponse", string.Empty)), decodeEscapeSequences);
            if (!TryValidateOptionalRegex(expectedResponse, "ExpectedResponse", out var expectedRegexError))
            {
                return ValidationResult.Invalid(expectedRegexError);
            }

            if (!TryValidateOptionalRegex(rejectedResponse, "RejectedResponse", out var rejectedRegexError))
            {
                return ValidationResult.Invalid(rejectedRegexError);
            }
        }

        return ValidationResult.Valid();
    }

    private static string NormalizePendingString(string? value)
    {
        return OperatorParameterValueSemantics.IsMissing(value) ? string.Empty : value!;
    }

    private ResponseParseResult ParseResponse(Operator @operator, string response)
    {
        var parseMode = NormalizeResponseParseMode(NormalizePendingString(GetStringParam(@operator, "ResponseParseMode", "None")));
        var requiredFields = SplitFieldNames(NormalizePendingString(GetStringParam(@operator, "RequiredResponseFields", string.Empty)));
        var decodeEscapeSequences = GetBoolParam(@operator, "DecodeEscapeSequences", false);
        if (parseMode == "none")
        {
            return ApplyRequiredResponseFields(
                ResponseParseResult.Ok(response, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)),
                requiredFields);
        }

        if (string.IsNullOrEmpty(response))
        {
            return ResponseParseResult.Fail("Response is empty.");
        }

        var parseResult = parseMode switch
        {
            "jsonpath" => ParseJsonResponse(
                response,
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldName", string.Empty))),
            "keyvalue" => ParseKeyValueResponse(
                response,
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldName", string.Empty)),
                BuildDelimiterOptions(
                    NormalizePendingString(GetStringParam(@operator, "ResponseKeyValuePairDelimiter", ";")),
                    NormalizePendingString(GetStringParam(@operator, "ResponseKeyValuePairDelimiters", string.Empty)),
                    decodeEscapeSequences),
                BuildDelimiterOptions(
                    NormalizePendingString(GetStringParam(@operator, "ResponseKeyValueSeparator", "=")),
                    NormalizePendingString(GetStringParam(@operator, "ResponseKeyValueSeparators", string.Empty)),
                    decodeEscapeSequences)),
            "regex" => ParseRegexResponse(
                response,
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldName", string.Empty)),
                NormalizePendingString(GetStringParam(@operator, "ResponseRegexPattern", string.Empty)),
                GetBoolParam(@operator, "ResponseRegexIgnoreCase", false)),
            "delimited" => ParseDelimitedResponse(
                response,
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldName", string.Empty)),
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldNames", string.Empty)),
                BuildDelimiterOptions(
                    NormalizePendingString(GetStringParam(@operator, "ResponseDelimiter", ",")),
                    NormalizePendingString(GetStringParam(@operator, "ResponseDelimiters", string.Empty)),
                    decodeEscapeSequences),
                GetIntParam(@operator, "ResponseIndex", 0, 0, 4096)),
            "fixedwidth" => ParseFixedWidthResponse(
                response,
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldName", string.Empty)),
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldNames", string.Empty)),
                NormalizePendingString(GetStringParam(@operator, "ResponseFieldWidths", string.Empty)),
                GetIntParam(@operator, "ResponseIndex", 0, 0, 4096)),
            _ => ResponseParseResult.Fail($"Unsupported response parse mode '{parseMode}'.")
        };
        return ApplyRequiredResponseFields(parseResult, requiredFields);
    }

    private static ResponseParseResult ParseJsonResponse(string response, string fieldName)
    {
        try
        {
            using var document = JsonDocument.Parse(response);
            var rootValue = ConvertJsonElement(document.RootElement);
            var fields = rootValue as Dictionary<string, object> ??
                         new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                         {
                             ["$"] = rootValue ?? string.Empty
                         };

            var path = string.IsNullOrWhiteSpace(fieldName) ? "$" : fieldName.Trim();
            if (path == "$")
            {
                return ResponseParseResult.Ok(rootValue, fields);
            }

            return TryExtractJsonPath(document.RootElement, path, out var selected)
                ? ResponseParseResult.Ok(ConvertJsonElement(selected), fields)
                : ResponseParseResult.Fail($"JSON response path '{fieldName}' was not found.", fields);
        }
        catch (JsonException ex)
        {
            return ResponseParseResult.Fail($"JSON response parse failed: {ex.Message}");
        }
    }

    private static ResponseParseResult ParseKeyValueResponse(
        string response,
        string fieldName,
        IReadOnlyList<string> pairDelimiters,
        IReadOnlyList<string> keyValueSeparators)
    {
        if (pairDelimiters.Count == 0 || keyValueSeparators.Count == 0)
        {
            return ResponseParseResult.Fail("Key-value response parsing requires non-empty delimiters.");
        }

        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPair in response.Split(pairDelimiters.ToArray(), StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryFindFirstDelimiter(rawPair, keyValueSeparators, out var separatorIndex, out var keyValueSeparator))
            {
                continue;
            }

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = rawPair[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = rawPair[(separatorIndex + keyValueSeparator.Length)..].Trim();
            fields[key] = InferScalar(value);
        }

        if (fields.Count == 0)
        {
            return ResponseParseResult.Fail("Key-value response did not contain any parseable pairs.");
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            return TryResolveInputPath(fields, fieldName, out var selected)
                ? ResponseParseResult.Ok(selected, fields)
                : ResponseParseResult.Fail($"Response field '{fieldName}' was not found.", fields);
        }

        return ResponseParseResult.Ok(fields, fields);
    }

    private static ResponseParseResult ParseRegexResponse(
        string response,
        string fieldName,
        string pattern,
        bool ignoreCase)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return ResponseParseResult.Fail("ResponseRegexPattern is required.");
        }

        try
        {
            var regexOptions = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            var regex = new Regex(pattern, regexOptions, TimeSpan.FromMilliseconds(100));
            var match = regex.Match(response);
            if (!match.Success)
            {
                return ResponseParseResult.Fail("Regex response pattern did not match.");
            }

            var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["0"] = match.Value
            };
            var firstNamedGroup = string.Empty;
            foreach (var groupName in regex.GetGroupNames())
            {
                var group = match.Groups[groupName];
                if (!group.Success)
                {
                    continue;
                }

                fields[groupName] = InferScalar(group.Value);
                if (!int.TryParse(groupName, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                    string.IsNullOrEmpty(firstNamedGroup))
                {
                    firstNamedGroup = groupName;
                }
            }

            if (!string.IsNullOrWhiteSpace(fieldName))
            {
                return TryResolveInputPath(fields, fieldName, out var selected)
                    ? ResponseParseResult.Ok(selected, fields)
                    : ResponseParseResult.Fail($"Response field '{fieldName}' was not found.", fields);
            }

            var value = !string.IsNullOrEmpty(firstNamedGroup) ? fields[firstNamedGroup] : match.Value;
            return ResponseParseResult.Ok(value, fields);
        }
        catch (ArgumentException ex)
        {
            return ResponseParseResult.Fail($"Regex response pattern is invalid: {ex.Message}");
        }
        catch (RegexMatchTimeoutException)
        {
            return ResponseParseResult.Fail("Regex response parsing timed out.");
        }
    }

    private static ResponseParseResult ParseDelimitedResponse(
        string response,
        string fieldName,
        string fieldNames,
        IReadOnlyList<string> delimiters,
        int index)
    {
        if (delimiters.Count == 0)
        {
            return ResponseParseResult.Fail("Delimited response parsing requires a non-empty delimiter.");
        }

        var tokens = response
            .Split(delimiters.ToArray(), StringSplitOptions.None)
            .Select(token => InferScalar(token.Trim()))
            .ToArray();
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tokens.Length; i++)
        {
            fields[i.ToString(CultureInfo.InvariantCulture)] = tokens[i] ?? string.Empty;
        }

        var mappedNames = SplitFieldNames(fieldNames);
        for (var i = 0; i < mappedNames.Length && i < tokens.Length; i++)
        {
            var mappedName = mappedNames[i];
            if (!string.IsNullOrWhiteSpace(mappedName))
            {
                fields[mappedName] = tokens[i] ?? string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            return TryResolveInputPath(fields, fieldName, out var selected)
                ? ResponseParseResult.Ok(selected, fields)
                : ResponseParseResult.Fail($"Response field '{fieldName}' was not found.", fields);
        }

        return index >= 0 && index < tokens.Length
            ? ResponseParseResult.Ok(tokens[index], fields)
            : ResponseParseResult.Fail($"Response index {index} is outside the parsed token range.", fields);
    }

    private static ResponseParseResult ParseFixedWidthResponse(
        string response,
        string fieldName,
        string fieldNames,
        string fieldWidths,
        int index)
    {
        if (!TryParseFixedWidths(fieldWidths, out var widths, out var error))
        {
            return ResponseParseResult.Fail(error);
        }

        var mappedNames = SplitFieldNames(fieldNames);
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            var width = widths[i];
            if (offset + width > response.Length)
            {
                return ResponseParseResult.Fail($"Fixed-width response is shorter than configured field widths at index {i}.", fields);
            }

            var value = InferScalar(response.Substring(offset, width).Trim());
            fields[i.ToString(CultureInfo.InvariantCulture)] = value;
            if (i < mappedNames.Length && !string.IsNullOrWhiteSpace(mappedNames[i]))
            {
                fields[mappedNames[i]] = value;
            }

            offset += width;
        }

        if (offset < response.Length)
        {
            fields["$remainder"] = response[offset..];
        }

        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            return TryResolveInputPath(fields, fieldName, out var selected)
                ? ResponseParseResult.Ok(selected, fields)
                : ResponseParseResult.Fail($"Response field '{fieldName}' was not found.", fields);
        }

        return index >= 0 && index < widths.Length
            ? ResponseParseResult.Ok(fields[index.ToString(CultureInfo.InvariantCulture)], fields)
            : ResponseParseResult.Fail($"Response index {index} is outside the fixed-width field range.", fields);
    }

    private static ResponseParseResult ApplyRequiredResponseFields(
        ResponseParseResult parseResult,
        string[] requiredFields)
    {
        if (!parseResult.Success || requiredFields.Length == 0)
        {
            return parseResult;
        }

        var missingFields = requiredFields
            .Where(field => !string.IsNullOrWhiteSpace(field) && !TryResolveInputPath(parseResult.Fields, field, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingFields.Length == 0)
        {
            return parseResult;
        }

        return ResponseParseResult.Fail(
            $"Required response fields were not found: {string.Join(", ", missingFields)}.",
            parseResult.Fields,
            missingFields);
    }

    private static string[] BuildDelimiterOptions(
        string primaryDelimiter,
        string additionalDelimiters,
        bool decodeEscapeSequences)
    {
        var delimiters = new List<string>();
        AddDelimiter(primaryDelimiter);

        if (!string.IsNullOrWhiteSpace(additionalDelimiters))
        {
            foreach (var delimiter in additionalDelimiters.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddDelimiter(delimiter);
            }
        }

        return delimiters
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        void AddDelimiter(string delimiter)
        {
            var decoded = DecodeIfEnabled(delimiter ?? string.Empty, decodeEscapeSequences);
            if (!string.IsNullOrEmpty(decoded))
            {
                delimiters.Add(decoded);
            }
        }
    }

    private static bool TryFindFirstDelimiter(
        string value,
        IReadOnlyList<string> delimiters,
        out int delimiterIndex,
        out string delimiter)
    {
        delimiterIndex = -1;
        delimiter = string.Empty;
        foreach (var candidate in delimiters)
        {
            var index = value.IndexOf(candidate, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            if (delimiterIndex >= 0 && index >= delimiterIndex)
            {
                continue;
            }

            delimiterIndex = index;
            delimiter = candidate;
        }

        return delimiterIndex >= 0;
    }

    private static bool TryParseFixedWidths(string fieldWidths, out int[] widths, out string error)
    {
        widths = Array.Empty<int>();
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(fieldWidths))
        {
            error = "Fixed-width response parsing requires ResponseFieldWidths.";
            return false;
        }

        var segments = fieldWidths.Split(new[] { ",", ";", "|" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            error = "Fixed-width response parsing requires at least one field width.";
            return false;
        }

        var parsed = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) || width <= 0)
            {
                error = $"ResponseFieldWidths contains invalid width '{segments[i]}'.";
                return false;
            }

            parsed[i] = width;
        }

        widths = parsed;
        return true;
    }

    private static string[] SplitFieldNames(string fieldNames)
    {
        if (string.IsNullOrWhiteSpace(fieldNames))
        {
            return Array.Empty<string>();
        }

        return fieldNames
            .Split(new[] { ",", ";", "|" }, StringSplitOptions.None)
            .Select(name => name.Trim())
            .ToArray();
    }

    private static bool TryExtractJsonPath(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        var normalizedPath = path.Trim();
        if (normalizedPath.StartsWith("$.", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }
        else if (normalizedPath.StartsWith("$", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[1..].TrimStart('.');
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return true;
        }

        foreach (var segment in normalizedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryApplyJsonPathSegment(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyJsonPathSegment(JsonElement source, string segment, out JsonElement value)
    {
        value = source;
        var propertyName = segment;
        int? index = null;
        var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracketIndex >= 0 && segment.EndsWith(']'))
        {
            propertyName = segment[..bracketIndex];
            var indexText = segment[(bracketIndex + 1)..^1];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIndex))
            {
                return false;
            }

            index = parsedIndex;
        }

        if (!string.IsNullOrEmpty(propertyName))
        {
            if (source.ValueKind != JsonValueKind.Object || !TryGetJsonProperty(source, propertyName, out source))
            {
                return false;
            }
        }

        if (index.HasValue)
        {
            if (source.ValueKind != JsonValueKind.Array || index.Value < 0 || index.Value >= source.GetArrayLength())
            {
                return false;
            }

            value = source.EnumerateArray().ElementAt(index.Value);
            return true;
        }

        value = source;
        return true;
    }

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => ConvertJsonElement(property.Value) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static object InferScalar(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue;
        }

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue) &&
            double.IsFinite(doubleValue))
        {
            return doubleValue;
        }

        return value;
    }

    private static string NormalizeResponseParseMode(string mode)
    {
        return (mode ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizeResponseMatchMode(string mode)
    {
        return (mode ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string NormalizeResponseMatchSource(string source)
    {
        return (source ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string DecodeIfEnabled(string value, bool decodeEscapeSequences)
    {
        return decodeEscapeSequences ? DecodeEscapeSequences(value) : value;
    }

    private static string DecodeEscapeSequences(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (current != '\\' || i + 1 >= value.Length)
            {
                builder.Append(current);
                continue;
            }

            var next = value[++i];
            switch (next)
            {
                case 'r':
                    builder.Append('\r');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case 'x' when TryReadHex(value, i + 1, 2, out var byteValue):
                    builder.Append((char)byteValue);
                    i += 2;
                    break;
                case 'u' when TryReadHex(value, i + 1, 4, out var charValue):
                    builder.Append((char)charValue);
                    i += 4;
                    break;
                default:
                    builder.Append('\\');
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool TryReadHex(string value, int start, int length, out int parsed)
    {
        parsed = 0;
        if (start + length > value.Length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            var digit = value[start + i];
            var numeric = digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'a' and <= 'f' => digit - 'a' + 10,
                >= 'A' and <= 'F' => digit - 'A' + 10,
                _ => -1
            };
            if (numeric < 0)
            {
                parsed = 0;
                return false;
            }

            parsed = (parsed << 4) + numeric;
        }

        return true;
    }

    private static bool TryValidateOptionalRegex(string pattern, string parameterName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"{parameterName} is invalid: {ex.Message}";
            return false;
        }
    }

    private static ResponseNormalizationResult NormalizeResponse(
        string response,
        bool trim,
        string startMarker,
        string endMarker,
        bool failOnMissingFrame)
    {
        var normalized = response ?? string.Empty;
        var hasStartMarker = !string.IsNullOrEmpty(startMarker);
        var hasEndMarker = !string.IsNullOrEmpty(endMarker);
        var frameFound = false;

        if (hasStartMarker)
        {
            var startIndex = normalized.IndexOf(startMarker, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                return failOnMissingFrame
                    ? ResponseNormalizationResult.Fail(normalized, "Response start marker was not found.")
                    : ResponseNormalizationResult.Ok(trim ? normalized.Trim() : normalized, false);
            }

            normalized = normalized[(startIndex + startMarker.Length)..];
            frameFound = true;
        }

        if (hasEndMarker)
        {
            var endIndex = normalized.IndexOf(endMarker, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                return failOnMissingFrame
                    ? ResponseNormalizationResult.Fail(normalized, "Response end marker was not found.")
                    : ResponseNormalizationResult.Ok(trim ? normalized.Trim() : normalized, frameFound);
            }

            normalized = normalized[..endIndex];
            frameFound = true;
        }

        if (trim)
        {
            normalized = normalized.Trim();
        }

        return ResponseNormalizationResult.Ok(normalized, frameFound);
    }

    private static Dictionary<string, object> BuildResponseFrameFailureOutput(
        string payload,
        string response,
        ResponseNormalizationResult normalization,
        string expectedResponse,
        string rejectedResponse,
        string responseMatchMode,
        string responseMatchSource,
        bool responseMatchIgnoreCase,
        bool responseRegexIgnoreCase,
        bool decodeEscapeSequences,
        bool trimResponseBeforeParse,
        string responseStartMarker,
        string responseEndMarker,
        string profileId,
        string ipAddress,
        int port,
        string mode)
    {
        return new Dictionary<string, object>
        {
            { "RequestPayload", payload },
            { "Response", response },
            { "NormalizedResponse", normalization.Response },
            { "Status", false },
            { "ParseSuccess", false },
            { "ParsedValue", string.Empty },
            { "ParsedFields", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) },
            { "ParseError", normalization.Error },
            { "MissingResponseFields", Array.Empty<string>() },
            { "ResponseFrameFound", normalization.FrameFound },
            { "ResponseFrameError", normalization.Error },
            { "ResponseAccepted", false },
            { "ResponseMatchError", normalization.Error },
            { "ResponseMatchValue", normalization.Response },
            { "ExpectedResponse", expectedResponse },
            { "RejectedResponse", rejectedResponse },
            { "ResponseMatchMode", responseMatchMode },
            { "ResponseMatchSource", responseMatchSource },
            { "ResponseMatchIgnoreCase", responseMatchIgnoreCase },
            { "ResponseRegexIgnoreCase", responseRegexIgnoreCase },
            { "DecodeEscapeSequences", decodeEscapeSequences },
            { "TrimResponseBeforeParse", trimResponseBeforeParse },
            { "ResponseStartMarker", responseStartMarker },
            { "ResponseEndMarker", responseEndMarker },
            { "Mode", mode },
            { "ProfileId", profileId },
            { "IpAddress", ipAddress },
            { "Port", port }
        };
    }

    private static ResponseJudgment EvaluateResponseJudgment(
        string response,
        string normalizedResponse,
        ResponseParseResult parseResult,
        string expectedResponse,
        string rejectedResponse,
        string matchMode,
        string matchSource,
        bool ignoreCase)
    {
        var hasExpected = !string.IsNullOrWhiteSpace(expectedResponse);
        var hasRejected = !string.IsNullOrWhiteSpace(rejectedResponse);
        var value = matchSource switch
        {
            "parsedvalue" => FormatPayloadValue(parseResult.Value),
            "normalizedresponse" => normalizedResponse,
            _ => response
        };

        if (!hasExpected && !hasRejected)
        {
            return ResponseJudgment.Ok(value);
        }

        if (matchSource == "parsedvalue" && !parseResult.Success)
        {
            return ResponseJudgment.Fail(value, "ParsedValue is unavailable because response parsing failed.");
        }

        if (hasRejected)
        {
            var rejectedMatched = TryMatchResponse(value, rejectedResponse, matchMode, ignoreCase, out var rejectedError);
            if (!string.IsNullOrEmpty(rejectedError))
            {
                return ResponseJudgment.Fail(value, rejectedError);
            }

            if (rejectedMatched)
            {
                return ResponseJudgment.Fail(value, "Response matched RejectedResponse.");
            }
        }

        if (hasExpected)
        {
            var expectedMatched = TryMatchResponse(value, expectedResponse, matchMode, ignoreCase, out var expectedError);
            if (!string.IsNullOrEmpty(expectedError))
            {
                return ResponseJudgment.Fail(value, expectedError);
            }

            return expectedMatched
                ? ResponseJudgment.Ok(value)
                : ResponseJudgment.Fail(value, "Response did not match ExpectedResponse.");
        }

        return ResponseJudgment.Ok(value);
    }

    private static bool TryMatchResponse(string value, string pattern, string matchMode, bool ignoreCase, out string error)
    {
        error = string.Empty;
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return matchMode switch
        {
            "contains" => value.Contains(pattern, comparison),
            "equals" => string.Equals(value, pattern, comparison),
            "startswith" => value.StartsWith(pattern, comparison),
            "endswith" => value.EndsWith(pattern, comparison),
            "regex" => TryRegexResponseMatch(value, pattern, ignoreCase, out error),
            _ => false
        };
    }

    private static bool TryRegexResponseMatch(string value, string pattern, bool ignoreCase, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            return Regex.IsMatch(value, pattern, options, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException ex)
        {
            error = $"Response match regex is invalid: {ex.Message}";
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            error = "Response match regex timed out.";
            return false;
        }
    }

    private static bool TryResolvePayload(
        Dictionary<string, object> inputs,
        object? inputData,
        string sendData,
        string payloadTemplate,
        bool useFixedSendData,
        bool failOnUnresolvedPayloadPlaceholder,
        bool decodeEscapeSequences,
        out string payload,
        out string error)
    {
        payload = string.Empty;
        error = string.Empty;
        var inputText = FormatPayloadValue(inputData);
        var configuredSendData = DecodeIfEnabled(sendData ?? string.Empty, decodeEscapeSequences);
        var configuredPayloadTemplate = DecodeIfEnabled(payloadTemplate ?? string.Empty, decodeEscapeSequences);
        if (!string.IsNullOrEmpty(configuredPayloadTemplate))
        {
            var unresolvedPaths = new List<string>();
            payload = PayloadPlaceholderRegex.Replace(configuredPayloadTemplate, match =>
            {
                var path = match.Groups["path"].Value;
                if (path.Equals("Data", StringComparison.OrdinalIgnoreCase))
                {
                    return inputText;
                }

                if (path.Equals("SendData", StringComparison.OrdinalIgnoreCase))
                {
                    return configuredSendData;
                }

                return TryResolveInputPath(inputs, path, out var value)
                    ? FormatPayloadValue(value)
                    : PreserveUnresolvedPlaceholder(match, path, unresolvedPaths);
            });

            if (failOnUnresolvedPayloadPlaceholder && unresolvedPaths.Count > 0)
            {
                error = $"PayloadTemplate contains unresolved placeholders: {string.Join(", ", unresolvedPaths.Distinct(StringComparer.OrdinalIgnoreCase))}.";
                return false;
            }

            return true;
        }

        if (!useFixedSendData && inputData != null)
        {
            payload = inputText;
            return true;
        }

        payload = configuredSendData;
        return true;
    }

    private static string PreserveUnresolvedPlaceholder(Match match, string path, List<string> unresolvedPaths)
    {
        unresolvedPaths.Add(path);
        return match.Value;
    }

    private static bool TryResolveInputPath(
        IReadOnlyDictionary<string, object> inputs,
        string path,
        out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || !TryGetDictionaryValue(inputs, segments[0], out value))
        {
            return false;
        }

        for (var i = 1; i < segments.Length; i++)
        {
            if (!TryGetMemberValue(value, segments[i], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetDictionaryValue(
        IReadOnlyDictionary<string, object> values,
        string key,
        out object? value)
    {
        if (values.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var item in values)
        {
            if (item.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = item.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetMemberValue(object? source, string memberName, out object? value)
    {
        value = null;
        if (source == null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        if (source is string text && TryParseJsonValue(text, out var parsedJson))
        {
            return TryGetMemberValue(parsedJson, memberName, out value);
        }

        if (source is IReadOnlyDictionary<string, object> readOnlyDictionary)
        {
            return TryGetDictionaryValue(readOnlyDictionary, memberName, out value);
        }

        if (source is IDictionary<string, object> dictionary)
        {
            foreach (var item in dictionary)
            {
                if (item.Key.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
        }

        if (source is System.Collections.IDictionary nonGenericDictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in nonGenericDictionary)
            {
                if (entry.Key?.ToString()?.Equals(memberName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    value = entry.Value;
                    return true;
                }
            }

            return false;
        }

        if (source is JsonElement jsonElement)
        {
            return TryGetMemberValue(ConvertJsonElement(jsonElement), memberName, out value);
        }

        if (int.TryParse(memberName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            TryGetIndexedValue(source, index, out value))
        {
            return true;
        }

        var property = source.GetType().GetProperty(
            memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property == null)
        {
            return false;
        }

        value = property.GetValue(source);
        return true;
    }

    private static bool TryGetIndexedValue(object? source, int index, out object? value)
    {
        value = null;
        if (source == null || index < 0)
        {
            return false;
        }

        if (source is System.Collections.IList list)
        {
            if (index >= list.Count)
            {
                return false;
            }

            value = list[index];
            return true;
        }

        if (source is System.Collections.IEnumerable enumerable &&
            source is not string &&
            source is not System.Collections.IDictionary)
        {
            var currentIndex = 0;
            foreach (var item in enumerable)
            {
                if (currentIndex == index)
                {
                    value = item;
                    return true;
                }

                currentIndex++;
            }
        }

        return false;
    }

    private static bool TryGetJsonElementMember(JsonElement element, string memberName, out object? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!property.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number when property.Value.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.Number when property.Value.TryGetDouble(out var doubleValue) => doubleValue,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value
            };
            return true;
        }

        return false;
    }

    private static bool TryParseJsonValue(string text, out object? value)
    {
        value = null;
        var trimmed = text.Trim();
        if (trimmed.Length < 2 ||
            (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            value = ConvertJsonElement(document.RootElement);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string FormatPayloadValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is ImageWrapper)
        {
            return "[ImageWrapper]";
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                JsonValueKind.Undefined => string.Empty,
                _ => jsonElement.GetRawText()
            };
        }

        if (value is IFormattable formattable && value is not string)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (value is System.Collections.IEnumerable and not string)
        {
            try
            {
                return JsonSerializer.Serialize(value);
            }
            catch
            {
                return value.ToString() ?? string.Empty;
            }
        }

        return value.ToString() ?? string.Empty;
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

    private sealed record ResponseParseResult(
        bool Success,
        object? Value,
        Dictionary<string, object> Fields,
        string Error,
        IReadOnlyList<string> MissingFields)
    {
        public static ResponseParseResult Ok(object? value, Dictionary<string, object> fields)
        {
            return new ResponseParseResult(true, value, fields, string.Empty, Array.Empty<string>());
        }

        public static ResponseParseResult Fail(string error)
        {
            return new ResponseParseResult(false, string.Empty, new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase), error, Array.Empty<string>());
        }

        public static ResponseParseResult Fail(string error, Dictionary<string, object> fields)
        {
            return new ResponseParseResult(false, string.Empty, fields, error, Array.Empty<string>());
        }

        public static ResponseParseResult Fail(string error, Dictionary<string, object> fields, IReadOnlyList<string> missingFields)
        {
            return new ResponseParseResult(false, string.Empty, fields, error, missingFields);
        }
    }

    private sealed record ResponseNormalizationResult(
        bool Success,
        string Response,
        bool FrameFound,
        string Error)
    {
        public static ResponseNormalizationResult Ok(string response, bool frameFound)
        {
            return new ResponseNormalizationResult(true, response, frameFound, string.Empty);
        }

        public static ResponseNormalizationResult Fail(string response, string error)
        {
            return new ResponseNormalizationResult(false, response, false, error);
        }
    }

    private sealed record ResponseJudgment(bool Accepted, string Value, string Error)
    {
        public static ResponseJudgment Ok(string value)
        {
            return new ResponseJudgment(true, value, string.Empty);
        }

        public static ResponseJudgment Fail(string value, string error)
        {
            return new ResponseJudgment(false, value, error);
        }
    }
}
