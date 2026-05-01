// CommentOperator.cs
// 注释算子
// 在流程中承载备注信息，不参与图像计算
// 作者：蘅芜君
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Microsoft.Extensions.Logging;

using Acme.Product.Core.Attributes;
namespace Acme.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "注释",
    Description = "在工作流中添加说明文本，不影响数据流，仅用于标注设计意图",
    Category = "辅助",
    IconName = "comment",
    Version = "1.0.1",
    Keywords = new[] { "注释", "备注", "说明", "标注", "文本", "Comment", "Note", "Annotation" }
)]
[AlgorithmInfo(
    Name = "Workflow annotation passthrough",
    CoreApi = "optional Input -> preserve value/ImageWrapper AddRef -> Output + Message",
    ImplementationStrategy = "Reads the configured Text parameter, forwards the optional Input value unchanged, and increments image references when an image payload is passed through.",
    TimeComplexity = "O(1)",
    TypicalLatency = "Avg 1.176 ms, max 21.034 ms over 22 contract golden cases",
    SpaceComplexity = "O(1)",
    SuitableUseCases = new[] { "Annotating workflow intent without changing data flow.", "Passing scalar or image payloads through a readable checkpoint node." },
    UnsuitableUseCases = new[] { "Transforming payloads or enforcing branching logic; use dedicated flow-control operators instead.", "Storing long operator documentation; keep notes concise and externalize large text." },
    KnownLimitations = new[] { "The note text is limited to 4096 characters to keep serialized flows bounded.", "The operator intentionally exposes only Output and Message and does not mutate upstream data." }
)]
[InputPort("Input", "透传输入", PortDataType.Any, IsRequired = false)]
[OutputPort("Output", "透传输出", PortDataType.Any)]
[OutputPort("Message", "注释内容", PortDataType.String)]
[OperatorParam("Text", "注释文本", "string", DefaultValue = "")]
public class CommentOperator : OperatorBase
{
    private const int MaxTextLength = 4096;

    public override OperatorType OperatorType => OperatorType.Comment;

    public CommentOperator(ILogger<CommentOperator> logger) : base(logger) { }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(Operator @operator, Dictionary<string, object>? inputs, CancellationToken cancellationToken)
    {
        var validation = ValidateParameters(@operator);
        if (!validation.IsValid)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(string.Join("; ", validation.Errors)));
        }

        object? input = null;
        if (inputs != null && inputs.TryGetValue("Input", out var value))
        {
            input = PreserveOutputValue(value);
        }

        var text = GetStringParam(@operator, "Text", string.Empty);
        return Task.FromResult(OperatorExecutionOutput.Success(new Dictionary<string, object>
        {
            ["Output"] = input ?? string.Empty,
            ["Message"] = text
        }));
    }

    private static object PreserveOutputValue(object value)
    {
        if (value is ImageWrapper wrapper)
            return wrapper.AddRef();
        return value;
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var text = GetStringParam(@operator, "Text", string.Empty);
        if (text.Length > MaxTextLength)
        {
            return ValidationResult.Invalid($"Text must be {MaxTextLength} characters or fewer.");
        }

        return ValidationResult.Valid();
    }
}
