using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "变量写入",
    Description = "写入单次运行变量或项目全局变量",
    Category = "变量",
    IconName = "variable-write")]
[InputPort("Value", "值", PortDataType.Any, IsRequired = false)]
[OutputPort("VariableName", "变量名", PortDataType.String)]
[OutputPort("Value", "写入的值", PortDataType.Any)]
[OutputPort("CycleCount", "循环计数", PortDataType.Integer)]
[OperatorParam("Scope", "作用域", "enum", DefaultValue = "Run", Options = new[] { "Run|单次运行", "Project|项目全局" })]
[OperatorParam("VariableId", "变量ID", "string", Description = "Project 作用域变量的稳定 ID", DefaultValue = "")]
[OperatorParam("VariableName", "变量名", "string", Description = "要写入的变量名称", DefaultValue = "")]
[OperatorParam("DataType", "数据类型", "enum", DefaultValue = "String", Options = new[] { "String|字符串", "Int|整数", "Double|浮点数", "Bool|布尔值" })]
[OperatorParam("UseInputValue", "使用输入值", "bool", Description = "优先使用上游输入值，否则使用静态值", DefaultValue = true)]
[OperatorParam("StaticValue", "静态值", "string", Description = "没有上游输入时使用的值", DefaultValue = "0")]
[OperatorParam("ConversionMode", "Conversion Mode", "enum", DefaultValue = "Exact", Options = new[] { "Exact|Exact", "Round|Round", "Floor|Floor", "Ceiling|Ceiling", "Truncate|Truncate" })]
[OperatorParam("Expression", "Expression", "string", Description = "Optional controlled expression evaluated before Project variable write. Use value for the raw input.", DefaultValue = "")]
public class VariableWriteOperator : OperatorBase
{
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    public override OperatorType OperatorType => OperatorType.VariableWrite;

    public VariableWriteOperator(
        ILogger<VariableWriteOperator> logger,
        IVariableContext variableContext,
        IProjectVariableExecutionContextAccessor? projectVariableContextAccessor = null) : base(logger)
    {
        _variableContext = variableContext;
        _projectVariableContextAccessor = projectVariableContextAccessor ?? new ProjectVariableExecutionContextAccessor();
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var scope = GetStringParam(@operator, "Scope", "Run");
        var variableIdText = GetStringParam(@operator, "VariableId", "");
        var variableName = GetStringParam(@operator, "VariableName", "");
        var dataType = GetStringParam(@operator, "DataType", "String");
        var conversionMode = GetConversionMode(@operator);
        var expression = GetStringParam(@operator, "Expression", "");
        var value = ResolveWriteValue(@operator, inputs, variableName, dataType);

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(WriteProjectVariable(@operator.Id, variableIdText, variableName, value, conversionMode, expression));
        }

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("变量名不能为空"));
        }

        var converted = ConvertRunValue(value, dataType);
        _variableContext.SetValue(variableName, converted);

        Logger.LogDebug("[VariableWrite] Write {VariableName} = {Value}", variableName, converted);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["VariableName"] = variableName,
            ["Value"] = converted,
            ["CycleCount"] = _variableContext.CycleCount
        }));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var scope = GetStringParam(@operator, "Scope", "Run");
        var variableName = GetStringParam(@operator, "VariableName", "");
        var variableIdText = GetStringParam(@operator, "VariableId", "");

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(variableIdText) && string.IsNullOrWhiteSpace(variableName))
            {
                return ValidationResult.Invalid("Project 作用域必须配置 VariableId 或 VariableName");
            }
        }
        else if (string.IsNullOrWhiteSpace(variableName))
        {
            return ValidationResult.Invalid("变量名不能为空");
        }

        var validTypes = new[] { "string", "int", "integer", "double", "float", "bool", "boolean" };
        var dataType = GetStringParam(@operator, "DataType", "String").ToLowerInvariant();
        if (!validTypes.Contains(dataType))
        {
            return ValidationResult.Invalid($"不支持的数据类型: {dataType}");
        }

        return ValidationResult.Valid();
    }

    private object ResolveWriteValue(Operator @operator, Dictionary<string, object>? inputs, string variableName, string dataType)
    {
        var useInputValue = GetBoolParam(@operator, "UseInputValue", true);
        if (useInputValue && inputs != null)
        {
            if (inputs.TryGetValue("Value", out var inputValue))
            {
                return inputValue;
            }

            if (!string.IsNullOrWhiteSpace(variableName) && inputs.TryGetValue(variableName, out var namedValue))
            {
                return namedValue;
            }
        }

        return GetStaticValue(@operator, dataType);
    }

    private OperatorExecutionOutput WriteProjectVariable(
        Guid operatorId,
        string variableIdText,
        string variableName,
        object value,
        ProjectVariableConversionMode conversionMode,
        string? expression)
    {
        var context = _projectVariableContextAccessor.Current;
        if (context == null)
        {
            return OperatorExecutionOutput.Failure("Project variable session is not available.");
        }

        ProjectGlobalVariableDefinition definition;
        if (Guid.TryParse(variableIdText, out var variableId) &&
            context.Session.TryGetDefinition(variableId, out definition!))
        {
        }
        else if (!string.IsNullOrWhiteSpace(variableName) &&
            context.Session.TryGetDefinitionByName(variableName, out definition!))
        {
        }
        else
        {
            return OperatorExecutionOutput.Failure($"Project variable '{variableIdText}'/'{variableName}' does not exist.");
        }

        try
        {
            var expressionVariables = ProjectVariableValueTransform.BuildExpressionVariables(context.Session, value);
            if (!ProjectVariableValueTransform.TryConvertToVariableValue(
                    value,
                    definition.ValueType,
                    conversionMode,
                    expression,
                    expressionVariables,
                    out var converted,
                    out var convertError))
            {
                return OperatorExecutionOutput.Failure(convertError ?? "Project variable value conversion failed.");
            }

            var snapshot = context.Session.SetValue(
                definition.Id,
                converted,
                ProjectVariableUpdatedBy.VariableWrite,
                context.RunId,
                operatorId);
            return OperatorExecutionOutput.Success(new Dictionary<string, object>
            {
                ["VariableName"] = definition.Name,
                ["Value"] = ProjectVariableValueConverter.ToObject(snapshot.Value)!,
                ["CycleCount"] = _variableContext.CycleCount
            });
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }
    }

    private object GetStaticValue(Operator @operator, string dataType)
    {
        var staticValue = GetStringParam(@operator, "StaticValue", "");
        return ConvertRunValue(staticValue, dataType);
    }

    private static object ConvertRunValue(object value, string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => Convert.ToInt64(value),
            "double" or "float" => Convert.ToDouble(value),
            "bool" or "boolean" => Convert.ToBoolean(value),
            _ => value?.ToString() ?? string.Empty
        };
    }

    private ProjectVariableConversionMode GetConversionMode(Operator @operator)
    {
        var text = GetStringParam(@operator, "ConversionMode", "Exact");
        return Enum.TryParse<ProjectVariableConversionMode>(text, ignoreCase: true, out var mode)
            ? mode
            : ProjectVariableConversionMode.Exact;
    }
}
