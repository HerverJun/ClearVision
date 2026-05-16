// DelayOperator.cs
// 延时算子
// 按配置阻塞或延迟流程执行指定时间
// 作者：蘅芜君
using Acme.Product.Core.Attributes;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;
namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "延时",
    Description = "等待指定时间后继续执行，常用于通信前等待下位机就绪",
    Category = "流程控制",
    IconName = "timer",
    Keywords = new[] { "延时", "等待", "暂停", "定时", "休眠", "Delay", "Wait", "Sleep", "Timer" }
)]
[InputPort("Input", "透传输入", PortDataType.Any, IsRequired = false)]
[OutputPort("Output", "透传输出", PortDataType.Any)]
[OutputPort("ElapsedMs", "实际耗时(ms)", PortDataType.Integer)]
[OperatorParam("Milliseconds", "延时毫秒", "int", DefaultValue = 200, Min = 0, Max = 60000)]
public class DelayOperator : OperatorBase
{
    private const int DefaultDelayMs = 200;
    private const int MaxDelayMs = 60_000;

    public override OperatorType OperatorType => OperatorType.Delay;

    public DelayOperator(ILogger<DelayOperator> logger) : base(logger) { }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(Operator @operator, Dictionary<string, object>? inputs, CancellationToken cancellationToken)
    {
        var ms = GetParam(@operator, "Milliseconds", DefaultDelayMs);
        if (ms < 0)
        {
            return OperatorExecutionOutput.Failure("Milliseconds must be greater than or equal to 0.");
        }

        if (ms > MaxDelayMs)
        {
            return OperatorExecutionOutput.Failure($"Milliseconds must be less than or equal to {MaxDelayMs}.");
        }

        var start = DateTime.UtcNow;
        await Task.Delay(ms, cancellationToken);
        var elapsed = (int)(DateTime.UtcNow - start).TotalMilliseconds;

        object? input = null;
        if (inputs != null && inputs.TryGetValue("Input", out var v))
            input = v;

        return OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Output"] = input ?? string.Empty,
            ["ElapsedMs"] = elapsed
        });
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var ms = GetParam(@operator, "Milliseconds", DefaultDelayMs);
        if (ms < 0)
        {
            return ValidationResult.Invalid("Milliseconds must be greater than or equal to 0.");
        }

        if (ms > MaxDelayMs)
        {
            return ValidationResult.Invalid($"Milliseconds must be less than or equal to {MaxDelayMs}.");
        }

        return ValidationResult.Valid();
    }
}
