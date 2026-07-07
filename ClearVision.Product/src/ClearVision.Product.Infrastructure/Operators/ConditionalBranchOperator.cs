// ConditionalBranchOperator.cs
// 条件分支算子 - 流程控制（True// 功能实现False分支）
// 作者：蘅芜君

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// 条件分支算子 - 流程控制（True/False分支）
/// </summary>
/// <remarks>
/// 注意：此算子需要FlowExecutionService支持条件分支路由
/// 输出数据会通过True或False端口传递，供后续算子使用
/// </remarks>
[OperatorMeta(
    DisplayName = "条件分支",
    Description = "根据数值/字符串/布尔条件执行 True/False 两路分支，常用于 OK/NG 判定路由",
    Category = "控制",
    IconName = "branch",
    Keywords = new[] { "条件", "分支", "判断", "如果", "否则", "IF", "Branch", "Condition", "Switch" }
)]
[InputPort("Value", "判断值", PortDataType.Any, IsRequired = true)]
[OutputPort("True", "True分支", PortDataType.Any)]
[OutputPort("False", "False分支", PortDataType.Any)]
[OutputPort("EvaluationSuccess", "Evaluation Success", PortDataType.Boolean)]
[OutputPort("EvaluationError", "Evaluation Error", PortDataType.String)]
[OperatorParam("Condition", "条件", "enum", DefaultValue = "GreaterThan", Options = new[] { "GreaterThan|大于", "GreaterThanOrEqual|大于等于", "LessThan|小于", "LessThanOrEqual|小于等于", "Equal|等于", "NotEqual|不等于", "InRange|范围内", "Between|介于", "NotInRange|范围外", "InList|列表内", "NotInList|列表外", "Contains|包含", "StartsWith|开头是", "EndsWith|结尾是", "Matches|正则匹配", "IsTrue|为真/OK", "IsFalse|为假/NG", "IsEmpty|为空", "IsNotEmpty|非空" })]
[OperatorParam("CompareValue", "比较值", "string", DefaultValue = "0")]
[OperatorParam("CompareListDelimiter", "Compare List Delimiter", "string", DefaultValue = ",")]
[OperatorParam("CompareListDelimiters", "Additional Compare List Delimiters", "string", DefaultValue = "")]
[OperatorParam("FieldName", "字段名", "string", DefaultValue = "")]
[OutputPort("ActualSource", "Actual Source", PortDataType.String)]
[InputPort("Compare", "Compare Value", PortDataType.Any, IsRequired = false)]
[OperatorParam("CompareFieldName", "Compare Field Name", "string", DefaultValue = "")]
[OperatorParam("FailOnMissingField", "Fail On Missing Field", "bool", DefaultValue = false)]
[OperatorParam("FailOnEvaluationError", "Fail On Evaluation Error", "bool", DefaultValue = false)]
[OperatorParam("NumericTolerance", "Numeric Tolerance", "double", DefaultValue = 0.0, Min = 0.0)]
[OperatorParam("IgnoreCase", "Ignore Case", "bool", DefaultValue = false)]
[OperatorParam("RangeMin", "Range Min", "double", DefaultValue = 0.0)]
[OperatorParam("RangeMax", "Range Max", "double", DefaultValue = 1.0)]
public class ConditionalBranchOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ConditionalBranch;

    public ConditionalBranchOperator(ILogger<ConditionalBranchOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (inputs == null || !inputs.TryGetValue("Value", out var value) || value == null)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("未提供判断值"));
        }

        // 获取参数
        var condition = GetStringParam(@operator, "Condition", "GreaterThan");
        var compareValueStr = GetStringParam(@operator, "CompareValue", "0");
        var compareListDelimiter = GetStringParam(@operator, "CompareListDelimiter", ",");
        var compareListDelimiters = GetStringParam(@operator, "CompareListDelimiters", "");
        var fieldName = GetStringParam(@operator, "FieldName", "");
        var compareFieldName = GetStringParam(@operator, "CompareFieldName", "");
        var failOnMissingField = GetBoolParam(@operator, "FailOnMissingField", false);
        var failOnEvaluationError = GetBoolParam(@operator, "FailOnEvaluationError", false);
        var numericTolerance = GetDoubleParam(@operator, "NumericTolerance", 0.0, 0.0);
        var ignoreCase = GetBoolParam(@operator, "IgnoreCase", false);
        var rangeMin = GetDoubleParam(@operator, "RangeMin", 0.0);
        var rangeMax = GetDoubleParam(@operator, "RangeMax", 1.0);

        // 如果指定了字段名，尝试从字典中获取字段值
        object? actualValue = value;
        var actualSource = "Value";
        if (!string.IsNullOrWhiteSpace(fieldName))
        {
            if (TryResolveFieldPath(value, fieldName, out var resolvedFieldValue))
            {
                actualValue = resolvedFieldValue;
                actualSource = "Field";
            }
            else if (failOnMissingField)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure($"Field '{fieldName}' was not found."));
            }
            else
            {
                actualSource = "ValueFallback";
            }
        }

        // 执行条件判断
        object? compareValue = compareValueStr;
        var compareSource = "Static";
        if (inputs.TryGetValue("Compare", out var compareInput) && compareInput != null)
        {
            compareValue = compareInput;
            compareSource = "Input";
            if (!string.IsNullOrWhiteSpace(compareFieldName))
            {
                if (!TryResolveFieldPath(compareInput, compareFieldName, out compareValue))
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure($"Compare input field '{compareFieldName}' was not found."));
                }

                compareSource = "InputField";
            }
        }
        else if (!string.IsNullOrWhiteSpace(compareFieldName))
        {
            if (!TryResolveFieldPath(value, compareFieldName, out compareValue))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure($"Compare field '{compareFieldName}' was not found."));
            }

            compareSource = "Field";
        }

        var rangeSource = "Parameters";
        if (IsRangeCondition(condition))
        {
            if (TryResolveRangeBounds(compareValue, out var dynamicRangeMin, out var dynamicRangeMax))
            {
                rangeMin = dynamicRangeMin;
                rangeMax = dynamicRangeMax;
                rangeSource = "CompareValue";
            }

            if (rangeMin > rangeMax)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure("RangeMin must be less than or equal to RangeMax."));
            }
        }

        var evaluation = EvaluateCondition(actualValue, condition, compareValue, numericTolerance, ignoreCase, compareListDelimiter, compareListDelimiters, rangeMin, rangeMax);
        if (!evaluation.Success && failOnEvaluationError)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(evaluation.Error));
        }

        bool result = evaluation.Result;

        // 准备输出数据
        var outputData = new Dictionary<string, object>
        {
            { "Condition", condition },
            { "CompareValue", FormatScalar(compareValue) },
            { "ActualValue", actualValue is ImageWrapper ? "[ImageWrapper]" : (actualValue ?? "null") },
            { "Result", result },
            { "EvaluationSuccess", evaluation.Success },
            { "EvaluationError", evaluation.Error },
            { "FieldName", fieldName },
            { "ActualSource", actualSource },
            { "CompareFieldName", compareFieldName },
            { "CompareSource", compareSource },
            { "CompareListDelimiter", compareListDelimiter },
            { "CompareListDelimiters", compareListDelimiters },
            { "NumericTolerance", numericTolerance },
            { "IgnoreCase", ignoreCase },
            { "RangeMin", rangeMin },
            { "RangeMax", rangeMax },
            { "RangeSource", rangeSource }
        };

        // 根据结果将原始值输出到对应的端口
        // True端口：条件成立时的输出
        // False端口：条件不成立时的输出
        if (result)
        {
            outputData["True"] = PreserveOutputValue(value);
            outputData["False"] = null!;
        }
        else
        {
            outputData["True"] = null!;
            outputData["False"] = PreserveOutputValue(value);
        }

        return Task.FromResult(OperatorExecutionOutput.Success(outputData));
    }

    private static object PreserveOutputValue(object value)
    {
        if (value is ImageWrapper wrapper)
            return wrapper.AddRef();
        return value;
    }

    private static BranchEvaluationResult EvaluateCondition(
        object? actualValue,
        string condition,
        object? compareValue,
        double numericTolerance,
        bool ignoreCase,
        string compareListDelimiter,
        string compareListDelimiters,
        double rangeMin,
        double rangeMax)
    {
        // 尝试将值转换为数字进行比较
        var normalizedCondition = NormalizeCondition(condition);
        var actualStr = FormatScalar(actualValue);
        var compareStr = FormatScalar(compareValue);
        double actualNum = 0;
        double compareNum = 0;
        var actualIsNumeric = TryConvertToDouble(actualValue, out actualNum);
        var compareIsNumeric = TryConvertToDouble(compareValue, out compareNum);
        var isNumeric = actualIsNumeric && compareIsNumeric;
        var stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // 字符串值
        if ((normalizedCondition is "greaterthan" or "greaterthanorequal" or "lessthan" or "lessthanorequal") && !isNumeric)
        {
            return BranchEvaluationResult.Fail($"Condition '{condition}' requires numeric ActualValue and CompareValue.");
        }

        if ((normalizedCondition is "inrange" or "between" or "notinrange") && !actualIsNumeric)
        {
            return BranchEvaluationResult.Fail($"Condition '{condition}' requires numeric ActualValue.");
        }

        if ((normalizedCondition is "inlist" or "notinlist") && !HasCompareListValues(compareValue, compareListDelimiter, compareListDelimiters))
        {
            return BranchEvaluationResult.Fail($"Condition '{condition}' requires at least one compare list value.");
        }

        var regexMatched = false;
        if (normalizedCondition == "matches" &&
            !TryRegexMatch(actualStr, compareStr, ignoreCase, out regexMatched, out var regexError))
        {
            return BranchEvaluationResult.Fail(regexError);
        }

        var trueValue = false;
        if (normalizedCondition == "istrue" && !TryConvertToBool(actualValue, out trueValue))
        {
            return BranchEvaluationResult.Fail($"Condition '{condition}' requires a boolean/OK/NG ActualValue.");
        }

        var falseValue = false;
        if (normalizedCondition == "isfalse" && !TryConvertToBool(actualValue, out falseValue))
        {
            return BranchEvaluationResult.Fail($"Condition '{condition}' requires a boolean/OK/NG ActualValue.");
        }

        return normalizedCondition switch
        {
            "greaterthan" => BranchEvaluationResult.Ok(actualNum > compareNum),
            "greaterthanorequal" => BranchEvaluationResult.Ok(actualNum >= compareNum),
            "lessthan" => BranchEvaluationResult.Ok(actualNum < compareNum),
            "lessthanorequal" => BranchEvaluationResult.Ok(actualNum <= compareNum),
            "equal" => BranchEvaluationResult.Ok(isNumeric ? Math.Abs(actualNum - compareNum) <= numericTolerance : string.Equals(actualStr, compareStr, stringComparison)),
            "notequal" => BranchEvaluationResult.Ok(isNumeric ? Math.Abs(actualNum - compareNum) > numericTolerance : !string.Equals(actualStr, compareStr, stringComparison)),
            "inrange" or "between" => BranchEvaluationResult.Ok(actualNum >= rangeMin - numericTolerance && actualNum <= rangeMax + numericTolerance),
            "notinrange" => BranchEvaluationResult.Ok(actualNum < rangeMin - numericTolerance || actualNum > rangeMax + numericTolerance),
            "inlist" => BranchEvaluationResult.Ok(IsInCompareList(actualStr, actualNum, actualIsNumeric, compareValue, compareListDelimiter, compareListDelimiters, numericTolerance, stringComparison)),
            "notinlist" => BranchEvaluationResult.Ok(!IsInCompareList(actualStr, actualNum, actualIsNumeric, compareValue, compareListDelimiter, compareListDelimiters, numericTolerance, stringComparison)),
            "contains" => BranchEvaluationResult.Ok(actualStr.Contains(compareStr, stringComparison)),
            "startswith" => BranchEvaluationResult.Ok(actualStr.StartsWith(compareStr, stringComparison)),
            "endswith" => BranchEvaluationResult.Ok(actualStr.EndsWith(compareStr, stringComparison)),
            "matches" => BranchEvaluationResult.Ok(regexMatched),
            "istrue" => BranchEvaluationResult.Ok(trueValue),
            "isfalse" => BranchEvaluationResult.Ok(!falseValue),
            "isempty" => BranchEvaluationResult.Ok(IsEmpty(actualValue)),
            "isnotempty" => BranchEvaluationResult.Ok(!IsEmpty(actualValue)),
            _ => BranchEvaluationResult.Fail($"Unsupported condition '{condition}'.")
        };
    }

    private static bool TryResolveFieldPath(object? source, string path, out object? value)
    {
        value = source;
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (!TryApplyPathSegment(value, segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryApplyPathSegment(object? source, string segment, out object? value)
    {
        value = source;
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        var cursor = 0;
        if (segment[0] != '[')
        {
            var firstBracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
            if (firstBracketIndex < 0)
            {
                return TryGetMemberValue(source, segment, out value);
            }

            var memberName = segment[..firstBracketIndex].Trim();
            if (memberName.Length == 0 || !TryGetMemberValue(source, memberName, out value))
            {
                return false;
            }

            cursor = firstBracketIndex;
        }

        while (cursor < segment.Length)
        {
            if (segment[cursor] != '[')
            {
                return false;
            }

            var closeBracketIndex = segment.IndexOf(']', cursor + 1);
            if (closeBracketIndex <= cursor + 1)
            {
                return false;
            }

            var indexText = segment[(cursor + 1)..closeBracketIndex].Trim();
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ||
                value == null ||
                !TryGetIndexedValue(value, index, out value))
            {
                return false;
            }

            cursor = closeBracketIndex + 1;
        }

        return true;
    }

    private static bool HasCompareListValues(
        object? compareValue,
        string compareListDelimiter,
        string compareListDelimiters)
    {
        return SplitCompareList(compareValue, compareListDelimiter, compareListDelimiters).Count > 0;
    }

    private static bool IsInCompareList(
        string actualText,
        double actualNumber,
        bool actualIsNumeric,
        object? compareValue,
        string compareListDelimiter,
        string compareListDelimiters,
        double numericTolerance,
        StringComparison stringComparison)
    {
        foreach (var item in SplitCompareList(compareValue, compareListDelimiter, compareListDelimiters))
        {
            if (actualIsNumeric && TryConvertToDouble(item, out var itemNumber))
            {
                if (Math.Abs(actualNumber - itemNumber) <= numericTolerance)
                {
                    return true;
                }

                continue;
            }

            if (string.Equals(actualText, FormatScalar(item), stringComparison))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<object?> SplitCompareList(
        object? compareValue,
        string compareListDelimiter,
        string compareListDelimiters)
    {
        if (compareValue == null)
        {
            return Array.Empty<object?>();
        }

        if (compareValue is string text)
        {
            if (TryParseJsonValue(text, out var parsedJsonList) &&
                parsedJsonList is System.Collections.IEnumerable jsonEnumerable and not string and not System.Collections.IDictionary)
            {
                var values = new List<object?>();
                foreach (var item in jsonEnumerable)
                {
                    values.Add(item);
                }

                return values;
            }

            var delimiters = BuildCompareListDelimiters(compareListDelimiter, compareListDelimiters);
            if (delimiters.Length == 0)
            {
                return string.IsNullOrWhiteSpace(text)
                    ? Array.Empty<object?>()
                    : new object?[] { text.Trim() };
            }

            return text
                .Split(delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Cast<object?>()
                .ToArray();
        }

        if (compareValue is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            return jsonArray.EnumerateArray().Cast<object?>().ToArray();
        }

        if (compareValue is System.Collections.IEnumerable enumerable)
        {
            var values = new List<object?>();
            foreach (var item in enumerable)
            {
                values.Add(item);
            }

            return values;
        }

        return new[] { compareValue };
    }

    private static string[] BuildCompareListDelimiters(
        string compareListDelimiter,
        string compareListDelimiters)
    {
        var delimiters = new List<string>();
        AddDelimiter(compareListDelimiter);

        if (!string.IsNullOrWhiteSpace(compareListDelimiters))
        {
            foreach (var delimiter in compareListDelimiters.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddDelimiter(delimiter);
            }
        }

        return delimiters
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        void AddDelimiter(string delimiter)
        {
            if (!string.IsNullOrEmpty(delimiter))
            {
                delimiters.Add(delimiter);
            }
        }
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
            foreach (var item in readOnlyDictionary)
            {
                if (item.Key.Equals(memberName, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
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
            return TryGetJsonElementMember(jsonElement, memberName, out value);
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

    private static bool TryGetIndexedValue(object source, int index, out object? value)
    {
        value = null;
        if (index < 0)
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

        if (source is Array array)
        {
            if (index >= array.Length)
            {
                return false;
            }

            value = array.GetValue(index);
            return true;
        }

        if (source is JsonElement { ValueKind: JsonValueKind.Array } jsonArray)
        {
            if (index >= jsonArray.GetArrayLength())
            {
                return false;
            }

            value = ConvertJsonElement(jsonArray.EnumerateArray().ElementAt(index));
            return true;
        }

        if (source is System.Collections.IEnumerable enumerable and not string and not System.Collections.IDictionary)
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
        if (element.ValueKind == JsonValueKind.Array &&
            int.TryParse(memberName, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
        {
            if (index < 0 || index >= element.GetArrayLength())
            {
                return false;
            }

            value = ConvertJsonElement(element.EnumerateArray().ElementAt(index));
            return true;
        }

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

    private static string NormalizeCondition(string condition)
    {
        return (condition ?? string.Empty)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static bool IsRangeCondition(string condition)
    {
        return NormalizeCondition(condition) is "inrange" or "between" or "notinrange";
    }

    private static bool IsListCondition(string condition)
    {
        return NormalizeCondition(condition) is "inlist" or "notinlist";
    }

    private static bool TryResolveRangeBounds(object? raw, out double min, out double max)
    {
        min = 0;
        max = 0;
        if (raw == null)
        {
            return false;
        }

        if (raw is string text)
        {
            if (TryParseRangeText(text, out min, out max))
            {
                return true;
            }

            return TryParseJsonValue(text, out var parsedJsonRange) &&
                   TryResolveRangeBounds(parsedJsonRange, out min, out max);
        }

        if (raw is System.Collections.IList list && list.Count >= 2)
        {
            return TryConvertToDouble(list[0], out min) &&
                   TryConvertToDouble(list[1], out max);
        }

        var rangeFieldPairs = new[]
        {
            ("Min", "Max"),
            ("Minimum", "Maximum"),
            ("Lower", "Upper"),
            ("Low", "High"),
            ("RangeMin", "RangeMax"),
            ("LowerBound", "UpperBound")
        };
        foreach (var (minField, maxField) in rangeFieldPairs)
        {
            if (TryGetMemberValue(raw, minField, out var minValue) &&
                TryGetMemberValue(raw, maxField, out var maxValue) &&
                TryConvertToDouble(minValue, out min) &&
                TryConvertToDouble(maxValue, out max))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseRangeText(string text, out double min, out double max)
    {
        min = 0;
        max = 0;
        var parts = text.Split(new[] { ',', ';', '|', '~' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               TryConvertToDouble(parts[0], out min) &&
               TryConvertToDouble(parts[1], out max);
    }

    private static bool TryConvertToDouble(object? raw, out double value)
    {
        value = 0;
        switch (raw)
        {
            case null:
                return false;
            case double d when double.IsFinite(d):
                value = d;
                return true;
            case float f when float.IsFinite(f):
                value = f;
                return true;
            case decimal m:
                value = (double)m;
                return double.IsFinite(value);
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case short s:
                value = s;
                return true;
            case byte b:
                value = b;
                return true;
            case JsonElement element:
                return TryConvertJsonNumber(element, out value);
            default:
                var text = raw.ToString();
                return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
                       double.IsFinite(value);
        }
    }

    private static bool TryConvertJsonNumber(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out value) && double.IsFinite(value),
            JsonValueKind.String => double.TryParse(
                element.GetString(),
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value) && double.IsFinite(value),
            _ => false
        };
    }

    private static bool TryConvertToBool(object? raw, out bool value)
    {
        value = false;
        switch (raw)
        {
            case bool boolean:
                value = boolean;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                value = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                value = false;
                return true;
        }

        var text = FormatScalar(raw).Trim();
        if (bool.TryParse(text, out value))
        {
            return true;
        }

        switch (text.ToUpperInvariant())
        {
            case "1":
            case "OK":
            case "PASS":
            case "TRUE":
                value = true;
                return true;
            case "0":
            case "NG":
            case "FAIL":
            case "FALSE":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool IsEmpty(object? value)
    {
        return value switch
        {
            null => true,
            string text => string.IsNullOrWhiteSpace(text),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => true,
            JsonElement { ValueKind: JsonValueKind.String } element => string.IsNullOrWhiteSpace(element.GetString()),
            System.Collections.ICollection collection => collection.Count == 0,
            _ => false
        };
    }

    private static bool TryRegexMatch(string actual, string pattern, bool ignoreCase, out bool matched, out string error)
    {
        matched = false;
        error = string.Empty;
        if (string.IsNullOrEmpty(pattern))
        {
            error = "Condition 'Matches' requires a non-empty regex pattern.";
            return false;
        }

        try
        {
            var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
            matched = Regex.IsMatch(actual, pattern, options, TimeSpan.FromMilliseconds(100));
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"Condition 'Matches' regex is invalid: {ex.Message}";
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            error = "Condition 'Matches' regex timed out.";
            return false;
        }
    }

    private static string FormatScalar(object? value)
    {
        if (value == null)
        {
            return string.Empty;
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

        return value.ToString() ?? string.Empty;
    }

    private sealed record BranchEvaluationResult(bool Success, bool Result, string Error)
    {
        public static BranchEvaluationResult Ok(bool result)
        {
            return new BranchEvaluationResult(true, result, string.Empty);
        }

        public static BranchEvaluationResult Fail(string error)
        {
            return new BranchEvaluationResult(false, false, error);
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var condition = GetStringParam(@operator, "Condition", "GreaterThan");

        var validConditions = new[]
        {
            "GreaterThan",
            "GreaterThanOrEqual",
            "LessThan",
            "LessThanOrEqual",
            "Equal",
            "NotEqual",
            "InRange",
            "Between",
            "NotInRange",
            "InList",
            "NotInList",
            "Contains",
            "StartsWith",
            "EndsWith",
            "Matches",
            "IsTrue",
            "IsFalse",
            "IsEmpty",
            "IsNotEmpty"
        };
        if (!validConditions.Contains(condition, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid($"条件必须是以下之一: {string.Join(", ", validConditions)}");
        }

        if (IsRangeCondition(condition))
        {
            var rangeMin = GetDoubleParam(@operator, "RangeMin", 0.0);
            var rangeMax = GetDoubleParam(@operator, "RangeMax", 1.0);
            if (rangeMin > rangeMax)
            {
                return ValidationResult.Invalid("RangeMin must be less than or equal to RangeMax.");
            }
        }

        if (IsListCondition(condition) &&
            BuildCompareListDelimiters(
                GetStringParam(@operator, "CompareListDelimiter", ","),
                GetStringParam(@operator, "CompareListDelimiters", string.Empty)).Length == 0)
        {
            return ValidationResult.Invalid("Compare list delimiters must not be empty for InList/NotInList.");
        }

        return ValidationResult.Valid();
    }
}
