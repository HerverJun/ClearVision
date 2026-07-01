using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "变量读取",
    Description = "从单次运行变量或项目全局变量读取值",
    Category = "变量",
    IconName = "variable-read")]
[OutputPort("Value", "值", PortDataType.Any)]
[OutputPort("Exists", "是否存在", PortDataType.Boolean)]
[OutputPort("CycleCount", "循环计数", PortDataType.Integer)]
[OperatorParam("Scope", "作用域", "enum", DefaultValue = "Run", Options = new[] { "Run|单次运行", "Project|项目全局" })]
[OperatorParam("VariableId", "变量ID", "string", Description = "Project 作用域变量的稳定 ID", DefaultValue = "")]
[OperatorParam("VariableName", "变量名", "string", Description = "要读取的变量名称", DefaultValue = "")]
[OperatorParam("DefaultValue", "默认值", "string", Description = "变量不存在时的默认值", DefaultValue = "0")]
[OperatorParam("DataType", "数据类型", "enum", DefaultValue = "String", Options = new[] { "String|字符串", "Int|整数", "Double|浮点数", "Bool|布尔值" })]
[OperatorParam("ConversionMode", "Conversion Mode", "enum", DefaultValue = "Exact", Options = new[] { "Exact|Exact", "Round|Round", "Floor|Floor", "Ceiling|Ceiling", "Truncate|Truncate" })]
[OperatorParam("Expression", "Expression", "string", Description = "Optional controlled expression evaluated after Project variable read. Use value for the current variable value.", DefaultValue = "")]
public class VariableReadOperator : OperatorBase
{
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    public override OperatorType OperatorType => OperatorType.VariableRead;

    public VariableReadOperator(
        ILogger<VariableReadOperator> logger,
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
        var defaultValue = GetStringParam(@operator, "DefaultValue", "0");
        var dataType = GetStringParam(@operator, "DataType", "String");
        var conversionMode = GetConversionMode(@operator);
        var expression = GetStringParam(@operator, "Expression", "");

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ReadProjectVariable(variableIdText, variableName, defaultValue, dataType, conversionMode, expression));
        }

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("变量名不能为空"));
        }

        var exists = _variableContext.Contains(variableName);
        var value = ReadRunVariable(variableName, defaultValue, dataType);

        Logger.LogInformation("[VariableRead] Read {VariableName} = {Value} (exists: {Exists})",
            variableName, value, exists);

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Value"] = value,
            ["VariableName"] = variableName,
            ["Exists"] = exists,
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

    private object ReadRunVariable(string variableName, string defaultValue, string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => _variableContext.GetValue<long>(variableName, long.TryParse(defaultValue, out var intValue) ? intValue : 0L),
            "double" or "float" => _variableContext.GetValue<double>(variableName, double.TryParse(defaultValue, out var doubleValue) ? doubleValue : 0.0),
            "bool" or "boolean" => _variableContext.GetValue<bool>(variableName, bool.TryParse(defaultValue, out var boolValue) && boolValue),
            _ => _variableContext.GetValue<string>(variableName, defaultValue) ?? defaultValue
        };
    }

    private OperatorExecutionOutput ReadProjectVariable(
        string variableIdText,
        string variableName,
        string defaultValue,
        string dataType,
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

        var exists = context.Session.TryGetValue(definition.Id, out var valueElement);
        object value = defaultValue;
        if (exists)
        {
            var expressionVariables = ProjectVariableValueTransform.BuildExpressionVariables(context.Session, ProjectVariableValueConverter.ToObject(valueElement));
            if (!ProjectVariableValueTransform.TryConvertForParameter(
                    valueElement,
                    definition.ValueType,
                    ToParameterType(dataType),
                    conversionMode,
                    expression,
                    expressionVariables,
                    out var converted,
                    out var error))
            {
                return OperatorExecutionOutput.Failure($"Project variable '{definition.Name}' cannot be read as {dataType}: {error}");
            }

            value = converted ?? "";
        }

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Value"] = value,
            ["VariableName"] = definition.Name,
            ["Exists"] = exists,
            ["CycleCount"] = _variableContext.CycleCount
        });
    }

    private static string ToParameterType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "int" or "integer" => "long",
            "bool" or "boolean" => "bool",
            "double" or "float" => "double",
            _ => "string"
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
