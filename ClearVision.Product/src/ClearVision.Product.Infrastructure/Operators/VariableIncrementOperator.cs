using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.ProjectVariables;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "变量递增",
    Description = "递增单次运行变量或项目全局 Int64 变量",
    Category = "变量",
    IconName = "counter")]
[OutputPort("VariableName", "变量名", PortDataType.String)]
[OutputPort("PreviousValue", "前值", PortDataType.Integer)]
[OutputPort("NewValue", "新值", PortDataType.Integer)]
[OutputPort("Delta", "增量", PortDataType.Integer)]
[OutputPort("WasReset", "是否已重置", PortDataType.Boolean)]
[OperatorParam("Scope", "作用域", "enum", DefaultValue = "Run", Options = new[] { "Run|单次运行", "Project|项目全局" })]
[OperatorParam("VariableId", "变量ID", "string", Description = "Project 作用域变量的稳定 ID", DefaultValue = "")]
[OperatorParam("VariableName", "变量名", "string", Description = "计数器变量名称", DefaultValue = "counter")]
[OperatorParam("Delta", "增量", "int", Description = "每次递增的值，可为负数", DefaultValue = 1)]
[OperatorParam("ResetCondition", "重置条件", "enum", Description = "满足条件时重置计数器", DefaultValue = "None", Options = new[] { "None|不重置", "GreaterThan|大于阈值", "LessThan|小于阈值", "Equal|等于阈值" })]
[OperatorParam("ResetThreshold", "重置阈值", "int", DefaultValue = 100)]
[OperatorParam("ResetValue", "重置后值", "int", Description = "重置后的起始值", DefaultValue = 0)]
public class VariableIncrementOperator : OperatorBase
{
    private readonly IVariableContext _variableContext;
    private readonly IProjectVariableExecutionContextAccessor _projectVariableContextAccessor;

    public override OperatorType OperatorType => OperatorType.VariableIncrement;

    public VariableIncrementOperator(
        ILogger<VariableIncrementOperator> logger,
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
        var delta = GetIntParam(@operator, "Delta", 1);
        var resetCondition = GetStringParam(@operator, "ResetCondition", "None");
        var resetThreshold = GetIntParam(@operator, "ResetThreshold", 0);
        var resetValue = GetIntParam(@operator, "ResetValue", 0);

        if (scope.Equals("Project", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(IncrementProjectVariable(
                @operator.Id,
                variableIdText,
                variableName,
                delta,
                resetCondition,
                resetThreshold,
                resetValue));
        }

        if (string.IsNullOrWhiteSpace(variableName))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("变量名不能为空"));
        }

        var currentValue = _variableContext.GetValue<long>(variableName, 0L);
        var (newValue, wasReset) = ApplyIncrement(
            currentValue,
            delta,
            resetCondition,
            resetThreshold,
            resetValue,
            value => _variableContext.SetValue(variableName, value),
            () => _variableContext.Increment(variableName, delta));

        return Task.FromResult(BuildSuccess(variableName, currentValue, newValue, delta, wasReset));
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

        var validConditions = new[] { "none", "greaterthan", "lessthan", "equal" };
        var condition = GetStringParam(@operator, "ResetCondition", "None").ToLowerInvariant();
        if (!validConditions.Contains(condition))
        {
            return ValidationResult.Invalid($"不支持的重置条件: {condition}");
        }

        return ValidationResult.Valid();
    }

    private OperatorExecutionOutput IncrementProjectVariable(
        Guid operatorId,
        string variableIdText,
        string variableName,
        long delta,
        string resetCondition,
        long resetThreshold,
        long resetValue)
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

        if (definition.ValueType != ProjectGlobalVariableValueType.Int64)
        {
            return OperatorExecutionOutput.Failure($"Project variable '{definition.Name}' must be Int64 for increment.");
        }

        try
        {
            var increment = context.Session.IncrementAtomic(
                definition.Id,
                delta,
                ProjectVariableUpdatedBy.VariableIncrement,
                context.RunId,
                operatorId,
                resetCondition,
                resetThreshold,
                resetValue);
            return BuildSuccess(
                definition.Name,
                increment.PreviousValue,
                increment.NewValue,
                delta,
                increment.WasReset);
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure(ex.Message);
        }
    }

    private static (long NewValue, bool WasReset) ApplyIncrement(
        long currentValue,
        long delta,
        string resetCondition,
        long resetThreshold,
        long resetValue,
        Action<long> setValue,
        Func<long> increment)
    {
        var shouldReset = ShouldReset(currentValue, resetCondition, resetThreshold);
        if (shouldReset)
        {
            var newValue = resetValue + delta;
            setValue(newValue);
            return (newValue, true);
        }

        return (increment(), false);
    }

    private static bool ShouldReset(long currentValue, string resetCondition, long resetThreshold)
    {
        return resetCondition.ToLowerInvariant() switch
        {
            "greaterthan" => currentValue > resetThreshold,
            "lessthan" => currentValue < resetThreshold,
            "equal" => currentValue == resetThreshold,
            _ => false
        };
    }

    private OperatorExecutionOutput BuildSuccess(string variableName, long previousValue, long newValue, long delta, bool wasReset)
    {
        Logger.LogDebug("[VariableIncrement] {VariableName}: {PreviousValue} + {Delta} = {NewValue}",
            variableName, previousValue, delta, newValue);

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["VariableName"] = variableName,
            ["PreviousValue"] = previousValue,
            ["NewValue"] = newValue,
            ["Delta"] = delta,
            ["WasReset"] = wasReset,
            ["CycleCount"] = _variableContext.CycleCount
        });
    }
}
