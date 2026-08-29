// ScriptOperator.cs
// 脚本算子
// 执行内嵌脚本并输出脚本运行结果
// 作者：蘅芜君
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

[OperatorMeta(
    DisplayName = "脚本算子",
    Description = "运行用户自定义 C# 表达式。",
    CategoryId = OperatorCategoryId.DataProcessing,
    IconName = "script",
    Version = "1.0.1",
    Keywords = new[] { "script", "custom", "code", "expression", "formula" }
)]
[InputPort("Input1", "Input 1", PortDataType.Any, IsRequired = false)]
[InputPort("Input2", "Input 2", PortDataType.Any, IsRequired = false)]
[InputPort("Input3", "Input 3", PortDataType.Any, IsRequired = false)]
[InputPort("Input4", "Input 4", PortDataType.Any, IsRequired = false)]
[OutputPort("Output1", "Output 1", PortDataType.Any)]
[OutputPort("Output2", "Output 2", PortDataType.Any)]
[OperatorParam("ScriptLanguage", "Script Language", "enum", DefaultValue = "CSharpExpression", Options = new[] { "CSharpExpression|CSharpExpression" })]
[OperatorParam("Code", "Code", "string", DefaultValue = "Input1 + Input2")]
[OperatorParam("Timeout", "Timeout (ms)", "int", DefaultValue = 5000, Min = 1, Max = 120000)]
public class ScriptOperator : OperatorBase
{
    internal const string UnsupportedLanguageCode = "SCRIPT_LANGUAGE_UNSUPPORTED";
    internal const string InvalidLanguageCode = "SCRIPT_LANGUAGE_INVALID";
    internal const string InvalidAssignmentCode = "SCRIPT_ASSIGNMENT_INVALID";
    internal const string UnresolvedVariableCode = "SCRIPT_VARIABLE_UNRESOLVED";
    internal const string UnsupportedFunctionCode = "SCRIPT_FUNCTION_UNSUPPORTED";
    internal const string InvalidExpressionCode = "SCRIPT_EXPRESSION_INVALID";

    private const string SupportedLanguage = "CSharpExpression";
    private static readonly Regex AssignmentTargetPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IdentifierPattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> ExpressionKeywords = new(
        ["true", "false", "and", "or", "not", "null"],
        StringComparer.OrdinalIgnoreCase);

    public override OperatorType OperatorType => OperatorType.ScriptOperator;

    public ScriptOperator(ILogger<ScriptOperator> logger) : base(logger)
    {
    }

    protected override Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        var language = GetStringParam(@operator, "ScriptLanguage", "CSharpExpression");
        var code = GetStringParam(@operator, "Code", string.Empty).Trim();
        var timeoutMs = GetIntParam(@operator, "Timeout", 5000, 1, 120000);

        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Code is required"));
        }

        if (!TryValidateLanguage(language, out var languageError))
        {
            return Task.FromResult(OperatorExecutionOutput.Failure(languageError));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        var context = BuildContext(inputs);
        object? output1 = null;
        object? output2 = null;

        var statements = SplitStatements(code);
        if (statements.Count == 0)
        {
            return Task.FromResult(OperatorExecutionOutput.Failure("Code does not contain executable statements"));
        }

        foreach (var raw in statements)
        {
            cts.Token.ThrowIfCancellationRequested();

            var statement = NormalizeStatement(raw);
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            var assignment = ParseAssignment(statement);
            if (!assignment.IsValid)
            {
                return Task.FromResult(OperatorExecutionOutput.Failure(
                    $"{InvalidAssignmentCode}: Assignment targets must be identifiers and assignments must include an expression."));
            }

            if (!TryEvaluateExpression(assignment.Expression, context, out var value, out var evaluationError))
            {
                return Task.FromResult(OperatorExecutionOutput.Failure(evaluationError));
            }

            if (assignment.HasAssignment)
            {
                if (assignment.Target.Equals("Output1", StringComparison.OrdinalIgnoreCase))
                {
                    output1 = value;
                }
                else if (assignment.Target.Equals("Output2", StringComparison.OrdinalIgnoreCase))
                {
                    output2 = value;
                }
                else
                {
                    context[assignment.Target] = value!;
                }
            }
            else
            {
                output1 = value;
            }
        }

        var result = new Dictionary<string, object>
        {
            { "Output1", output1 ?? string.Empty },
            { "Output2", output2 ?? string.Empty }
        };

        return Task.FromResult(OperatorExecutionOutput.Success(result));
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var language = GetStringParam(@operator, "ScriptLanguage", "CSharpExpression");
        if (!TryValidateLanguage(language, out var languageError))
        {
            return ValidationResult.Invalid(languageError);
        }

        var code = GetStringParam(@operator, "Code", string.Empty);
        if (string.IsNullOrWhiteSpace(code))
        {
            return ValidationResult.Invalid("Code cannot be empty");
        }

        var timeout = GetIntParam(@operator, "Timeout", 5000);
        if (timeout <= 0)
        {
            return ValidationResult.Invalid("Timeout must be greater than 0");
        }

        var statements = SplitStatements(code);
        if (statements.Count == 0)
        {
            return ValidationResult.Invalid($"{InvalidExpressionCode}: Code does not contain an expression.");
        }

        var context = BuildContext(null);
        foreach (var raw in statements)
        {
            var statement = NormalizeStatement(raw);
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            var assignment = ParseAssignment(statement);
            if (!assignment.IsValid)
            {
                return ValidationResult.Invalid(
                    $"{InvalidAssignmentCode}: Assignment targets must be identifiers and assignments must include an expression.");
            }

            if (!TryEvaluateExpression(assignment.Expression, context, out var value, out var evaluationError))
            {
                return ValidationResult.Invalid(evaluationError);
            }

            if (assignment.HasAssignment)
            {
                context[assignment.Target] = value!;
            }
        }

        return ValidationResult.Valid();
    }

    private static bool TryValidateLanguage(string language, out string error)
    {
        if (string.Equals(language, SupportedLanguage, StringComparison.Ordinal))
        {
            error = string.Empty;
            return true;
        }

        error = string.Equals(language, "CSharpScript", StringComparison.OrdinalIgnoreCase)
            ? $"{UnsupportedLanguageCode}: CSharpScript is not supported; use CSharpExpression."
            : $"{InvalidLanguageCode}: ScriptLanguage must be exactly CSharpExpression.";
        return false;
    }

    private static Dictionary<string, object> BuildContext(Dictionary<string, object>? inputs)
    {
        var context = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (inputs != null)
        {
            foreach (var (key, value) in inputs)
            {
                context[key] = value;
            }
        }

        for (var i = 1; i <= 4; i++)
        {
            var key = $"Input{i}";
            if (!context.ContainsKey(key))
            {
                context[key] = 0d;
            }
        }

        return context;
    }

    private static List<string> SplitStatements(string code)
    {
        return code
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static string NormalizeStatement(string statement)
    {
        var trimmed = statement.Trim();
        if (trimmed.StartsWith("return ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[7..].Trim();
        }

        return trimmed;
    }

    private static AssignmentParseResult ParseAssignment(string statement)
    {
        var assignmentIndex = FindAssignmentIndex(statement);
        if (assignmentIndex < 0)
        {
            return new AssignmentParseResult(false, true, string.Empty, statement);
        }

        var target = statement[..assignmentIndex].Trim();
        var expression = statement[(assignmentIndex + 1)..].Trim();
        var isValid = AssignmentTargetPattern.IsMatch(target) && !string.IsNullOrWhiteSpace(expression);
        return new AssignmentParseResult(true, isValid, target, expression);
    }

    private static int FindAssignmentIndex(string statement)
    {
        for (var index = 0; index < statement.Length; index++)
        {
            if (statement[index] != '=')
            {
                continue;
            }

            var previous = index > 0 ? statement[index - 1] : '\0';
            var next = index + 1 < statement.Length ? statement[index + 1] : '\0';
            if (previous is '<' or '>' or '!' or '=' || next == '=')
            {
                continue;
            }

            return index;
        }

        return -1;
    }

    private static bool TryEvaluateExpression(
        string expression,
        IReadOnlyDictionary<string, object> context,
        out object? value,
        out string error)
    {
        value = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = $"{InvalidExpressionCode}: Expression cannot be empty.";
            return false;
        }

        var trimmed = expression.Trim();

        if (IsQuotedLiteral(trimmed))
        {
            value = trimmed[1..^1];
            return true;
        }

        if (context.TryGetValue(trimmed, out var directValue))
        {
            value = directValue;
            return true;
        }

        if (bool.TryParse(trimmed, out var booleanResult))
        {
            value = booleanResult;
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericResult))
        {
            value = numericResult;
            return true;
        }

        var unquotedExpression = MaskQuotedText(trimmed);
        var function = IdentifierPattern.Matches(unquotedExpression)
            .Select(match => match.Value)
            .FirstOrDefault(identifier =>
            {
                var identifierIndex = unquotedExpression.IndexOf(identifier, StringComparison.Ordinal);
                if (identifierIndex < 0)
                {
                    return false;
                }

                var following = unquotedExpression[(identifierIndex + identifier.Length)..].TrimStart();
                return following.StartsWith('(');
            });
        if (!string.IsNullOrWhiteSpace(function))
        {
            error = $"{UnsupportedFunctionCode}: Function calls are not supported by CSharpExpression.";
            return false;
        }

        var unresolved = IdentifierPattern.Matches(unquotedExpression)
            .Select(match => match.Value)
            .FirstOrDefault(identifier =>
                !ExpressionKeywords.Contains(identifier) &&
                !context.ContainsKey(identifier));
        if (!string.IsNullOrWhiteSpace(unresolved))
        {
            error = $"{UnresolvedVariableCode}: Variable '{unresolved}' is not defined.";
            return false;
        }

        var numericExpression = ReplaceVariables(trimmed, context);

        try
        {
            using var table = new DataTable();
            var raw = table.Compute(numericExpression, null);

            if (raw is null)
            {
                value = string.Empty;
                return true;
            }

            if (raw is bool b)
            {
                value = b;
                return true;
            }

            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            error = $"{InvalidExpressionCode}: Expression syntax or operand types are invalid.";
            return false;
        }
    }

    private static string MaskQuotedText(string expression)
    {
        var chars = expression.ToCharArray();
        char quote = '\0';
        for (var index = 0; index < chars.Length; index++)
        {
            if (quote == '\0' && chars[index] is '\'' or '"')
            {
                quote = chars[index];
                chars[index] = ' ';
                continue;
            }

            if (quote != '\0')
            {
                if (chars[index] == quote)
                {
                    quote = '\0';
                }

                chars[index] = ' ';
            }
        }

        return new string(chars);
    }

    private static bool IsQuotedLiteral(string text)
    {
        return text.Length >= 2 &&
               ((text.StartsWith('"') && text.EndsWith('"')) ||
                (text.StartsWith('\'') && text.EndsWith('\'')));
    }

    private static string ReplaceVariables(string expression, IReadOnlyDictionary<string, object> context)
    {
        var result = expression;

        foreach (var (key, value) in context)
        {
            if (!TryConvertToDouble(value, out var numeric))
            {
                continue;
            }

            var pattern = $@"\b{Regex.Escape(key)}\b";
            result = Regex.Replace(
                result,
                pattern,
                numeric.ToString(CultureInfo.InvariantCulture),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    private static bool TryConvertToDouble(object? raw, out double value)
    {
        value = 0;
        if (raw is null)
        {
            return false;
        }

        return raw switch
        {
            double d => (value = d) == d,
            float f => (value = f) == f,
            int i => (value = i) == i,
            long l => (value = l) == l,
            bool b => (value = b ? 1d : 0d) >= 0,
            _ => double.TryParse(raw.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)
        };
    }

    private readonly record struct AssignmentParseResult(
        bool HasAssignment,
        bool IsValid,
        string Target,
        string Expression);
}

