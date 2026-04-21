using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Services;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "循环计数器",
    Description = "获取当前循环次数和统计信息",
    Category = "变量",
    IconName = "cycle"
)]
[OutputPort("CycleCount", "当前次数", PortDataType.Integer)]
[OutputPort("MaxCycles", "最大次数", PortDataType.Integer)]
[OutputPort("IsLimitReached", "是否达到限制", PortDataType.Boolean)]
[OutputPort("RemainingCycles", "剩余次数", PortDataType.Integer)]
[OutputPort("Progress", "进度(%)", PortDataType.Float)]
[OperatorParam("Action", "操作", "enum", Description = "读取/重置/递增", DefaultValue = "Read", Options = new[] { "Read|读取", "Reset|重置", "Increment|递增" })]
[OperatorParam("MaxCycles", "最大循环次数", "int", Description = "0表示无限制", DefaultValue = 0)]
public class CycleCounterOperator : OperatorBase
{
    private const string ReadAction = "read";
    private const string ResetAction = "reset";
    private const string IncrementAction = "increment";

    private readonly IVariableContext _variableContext;

    public override OperatorType OperatorType => OperatorType.CycleCounter;

    public CycleCounterOperator(
        ILogger<CycleCounterOperator> logger,
        IVariableContext variableContext) : base(logger)
    {
        _variableContext = variableContext;
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var action = NormalizeAction(GetStringParam(@operator, "Action", "Read"));
        if (!IsSupportedAction(action))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure($"Unsupported action: {action}"));
        }

        var maxCycles = GetParam(@operator, "MaxCycles", 0);
        if (maxCycles < 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("MaxCycles must be greater than or equal to 0."));
        }

        long currentCount = _variableContext.CycleCount;
        bool isLimitReached = maxCycles > 0 && currentCount >= maxCycles;

        switch (action)
        {
            case ResetAction:
                _variableContext.ResetCycleCount();
                currentCount = 0;
                Logger.LogInformation("[CycleCounter] 循环计数器已重置");
                break;

            case IncrementAction:
                if (maxCycles > 0 && currentCount >= maxCycles)
                {
                    isLimitReached = true;
                    Logger.LogWarning("[CycleCounter] 循环计数器已达到最大次数 {MaxCycles}", maxCycles);
                    break;
                }

                if (currentCount == long.MaxValue)
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure("CycleCount cannot be incremented beyond Int64.MaxValue."));
                }

                _variableContext.IncrementCycleCount();
                currentCount = _variableContext.CycleCount;
                if (currentCount < 0)
                {
                    return Task.FromResult(OperatorExecutionOutput.Failure("CycleCount overflow detected after increment."));
                }

                isLimitReached = maxCycles > 0 && currentCount >= maxCycles;
                Logger.LogInformation("[CycleCounter] 循环计数器递增: {Count}", currentCount);
                break;

            default:
                Logger.LogDebug("[CycleCounter] 读取循环计数: {Count}", currentCount);
                break;
        }

        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            { "CycleCount", currentCount },
            { "MaxCycles", maxCycles },
            { "IsLimitReached", isLimitReached },
            { "RemainingCycles", maxCycles > 0 ? Math.Max(0L, maxCycles - currentCount) : -1L },
            { "Progress", maxCycles > 0 ? Math.Min(100d, (double)currentCount / maxCycles * 100d) : 0d }
        }));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var action = NormalizeAction(GetStringParam(@operator, "Action", "Read"));
        if (!IsSupportedAction(action))
        {
            return ValidationResult.Invalid($"Unsupported action: {action}");
        }

        var maxCycles = GetParam(@operator, "MaxCycles", 0);
        if (maxCycles < 0)
        {
            return ValidationResult.Invalid("MaxCycles must be greater than or equal to 0.");
        }

        return ValidationResult.Valid();
    }

    private static string NormalizeAction(string action)
    {
        return (action ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static bool IsSupportedAction(string action)
    {
        return action is ReadAction or ResetAction or IncrementAction;
    }
}
