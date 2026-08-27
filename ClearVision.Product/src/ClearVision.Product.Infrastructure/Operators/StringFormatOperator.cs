// StringFormatOperator.cs
// 字符串格式化算子 - Sprint 3 Task 3.6a
// 支持模板替换和字符串拼接
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 字符串格式化算子
/// 
/// 功能：
/// - 模板替换：{0}, {1}, {Name}, {Value}
/// - 字符串拼接
/// - 日期格式化
/// 
/// 使用场景：
/// - 报告生成
/// - 日志拼装
/// - 文件名生成
/// </summary>
[OperatorMeta(
    DisplayName = "字符串格式化",
    Description = "按模板生成字符串",
    CategoryId = OperatorCategoryId.DataProcessing,
    IconName = "text"
)]
[InputPort("Arg1", "参数 1", PortDataType.Any, IsRequired = false)]
[InputPort("Arg2", "参数 2", PortDataType.Any, IsRequired = false)]
[OutputPort("Result", "结果", PortDataType.String)]
[OutputPort("Length", "结果长度", PortDataType.Integer)]
[OutputPort("IsEmpty", "结果为空", PortDataType.Boolean)]
[OperatorParam("Mode", "格式模式", "enum", DefaultValue = "Template", Options = new[] { "Template|模板", "Join|拼接", "Date|日期时间" })]
[OperatorParam("Template", "模板", "string", DefaultValue = "Result is {0} and {1}")]
[OperatorParam("Separator", "分隔符", "string", DefaultValue = "")]
[OperatorParam("DateFormat", "日期格式", "string", DefaultValue = "yyyy-MM-dd HH:mm:ss")]
[OperatorParameterRule("Template", DisabledWhenAll = new[] { "Mode!=Template" }, HiddenWhenAll = new[] { "Mode!=Template" }, IgnoredWhenAll = new[] { "Mode!=Template" }, ReasonCode = "STRING_FORMAT_TEMPLATE_MODE_ONLY")]
[OperatorParameterRule("Separator", DisabledWhenAll = new[] { "Mode!=Join" }, HiddenWhenAll = new[] { "Mode!=Join" }, IgnoredWhenAll = new[] { "Mode!=Join" }, ReasonCode = "STRING_FORMAT_JOIN_MODE_ONLY")]
[OperatorParameterRule("DateFormat", DisabledWhenAll = new[] { "Mode!=Date" }, HiddenWhenAll = new[] { "Mode!=Date" }, IgnoredWhenAll = new[] { "Mode!=Date" }, ReasonCode = "STRING_FORMAT_DATE_MODE_ONLY")]
public class StringFormatOperator : OperatorBase
{
    private static readonly string[] DeclaredInputNames = ["Arg1", "Arg2"];

    public override OperatorType OperatorType => OperatorType.StringFormat;

    public StringFormatOperator(ILogger<StringFormatOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("StringFormat 算子需要输入数据"));
        }

        // 获取参数
        var template = GetStringParam(@operator, "Template", "Result is {0} and {1}");
        var separator = GetStringParam(@operator, "Separator", "");
        var mode = GetStringParam(@operator, "Mode", "Template"); // Template, Join, Date

        string result;

        switch (mode.ToLower())
        {
            case "template":
                result = FormatTemplate(template, inputs);
                break;

            case "join":
                result = string.Join(separator, GetDeclaredInputValues(inputs));
                break;

            case "date":
                var format = GetStringParam(@operator, "DateFormat", "yyyy-MM-dd HH:mm:ss");
                result = DateTime.Now.ToString(format);
                break;

            default:
                return Task.FromResult(OperatorExecutionOutput.Failure($"不支持的模式: {mode}"));
        }

        Logger.LogDebug("[StringFormat] 模式={Mode}, 结果={Result}", mode, result);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            { "Result", result },
            { "Length", result.Length },
            { "IsEmpty", string.IsNullOrEmpty(result) }
        }));
    }

    /// <summary>
    /// 模板替换
    /// 支持 {0}, {1}, ... 和 {KeyName}
    /// </summary>
    private static string FormatTemplate(string template, IReadOnlyDictionary<string, object> inputs)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Concat(GetDeclaredInputValues(inputs));
        }

        var result = template;
        for (var index = 0; index < DeclaredInputNames.Length; index += 1)
        {
            var inputName = DeclaredInputNames[index];
            if (!inputs.TryGetValue(inputName, out var value))
            {
                continue;
            }

            var text = value?.ToString() ?? string.Empty;
            result = result.Replace($"{{{index}}}", text, StringComparison.Ordinal);
            result = result.Replace($"{{{inputName}}}", text, StringComparison.Ordinal);
        }

        return result;
    }

    private static IEnumerable<string> GetDeclaredInputValues(IReadOnlyDictionary<string, object> inputs)
    {
        foreach (var inputName in DeclaredInputNames)
        {
            if (inputs.TryGetValue(inputName, out var value))
            {
                yield return value?.ToString() ?? string.Empty;
            }
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var mode = GetStringParam(@operator, "Mode", "Template");

        var validModes = new[] { "Template", "Join", "Date" };
        if (!validModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid($"Mode 必须是以下之一: {string.Join(", ", validModes)}");
        }

        return ValidationResult.Valid();
    }
}
