using System.Globalization;
using System.Text.Json;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectVariableExpressionEvaluator
{
    public const int MaxExpressionLength = 2048;
    public const int MaxTokenCount = 512;
    public const int MaxAstDepth = 64;
    public const int MaxFunctionArgumentCount = 8;

    public static bool TryCompile(
        string expression,
        IEnumerable<string> knownVariableNames,
        out string? error)
    {
        error = null;
        try
        {
            var variables = knownVariableNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(name => name, _ => (object?)null, StringComparer.OrdinalIgnoreCase);
            var parser = new Parser(expression, variables, parseOnly: true);
            parser.Parse();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException or DivideByZeroException)
        {
            error = NormalizeError(ex);
            return false;
        }
    }

    public static bool TryEvaluate(
        string expression,
        IReadOnlyDictionary<string, object?> variables,
        out object? value,
        out string? error)
    {
        value = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Expression is empty.";
            return false;
        }

        try
        {
            var parser = new Parser(expression, variables, parseOnly: false);
            value = parser.Parse();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException or DivideByZeroException)
        {
            error = NormalizeError(ex);
            return false;
        }
    }

    private static string NormalizeError(Exception exception)
    {
        var message = exception.Message;
        if (message.StartsWith("GV", StringComparison.Ordinal))
        {
            return message;
        }

        var code = exception switch
        {
            DivideByZeroException => "GV036",
            OverflowException => "GV037",
            InvalidOperationException when message.Contains("Unknown variable", StringComparison.OrdinalIgnoreCase) => "GV035",
            InvalidOperationException when
                message.Contains("maximum length", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("token count", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("AST depth", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("function argument count", StringComparison.OrdinalIgnoreCase) => "GV039",
            InvalidOperationException when
                message.Contains("must be finite", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("not numeric", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("precision", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("requires numeric", StringComparison.OrdinalIgnoreCase) => "GV038",
            _ => "GV034"
        };

        return $"{code}: {message}";
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, object?> _variables;
        private readonly bool _parseOnly;
        private int _position;
        private int _tokenCount;
        private int _depth;

        public Parser(string text, IReadOnlyDictionary<string, object?> variables, bool parseOnly)
        {
            if (text.Length > MaxExpressionLength)
            {
                throw new InvalidOperationException($"Expression exceeds maximum length {MaxExpressionLength}.");
            }

            _text = text;
            _variables = variables;
            _parseOnly = parseOnly;
        }

        public object Parse()
        {
            var value = ParseOr();
            SkipWhiteSpace();
            if (_position != _text.Length)
            {
                throw Error($"Unexpected token '{_text[_position]}'.");
            }

            return value;
        }

        private object ParseOr()
        {
            var left = ParseAnd();
            while (Match("||"))
            {
                var right = ParseAnd();
                left = _parseOnly ? true : ToBool(left) || ToBool(right);
            }

            return left;
        }

        private object ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
            {
                var right = ParseEquality();
                left = _parseOnly ? true : ToBool(left) && ToBool(right);
            }

            return left;
        }

        private object ParseEquality()
        {
            var left = ParseComparison();
            while (true)
            {
                if (Match("=="))
                {
                    var right = ParseComparison();
                    left = _parseOnly ? true : AreEqual(left, right);
                }
                else if (Match("!="))
                {
                    var right = ParseComparison();
                    left = _parseOnly ? true : !AreEqual(left, right);
                }
                else
                {
                    return left;
                }
            }
        }

        private object ParseComparison()
        {
            var left = ParseAdditive();
            while (true)
            {
                if (Match(">="))
                {
                    var right = ParseAdditive();
                    left = _parseOnly ? true : CompareValues(left, right) >= 0;
                }
                else if (Match("<="))
                {
                    var right = ParseAdditive();
                    left = _parseOnly ? true : CompareValues(left, right) <= 0;
                }
                else if (Match(">"))
                {
                    var right = ParseAdditive();
                    left = _parseOnly ? true : CompareValues(left, right) > 0;
                }
                else if (Match("<"))
                {
                    var right = ParseAdditive();
                    left = _parseOnly ? true : CompareValues(left, right) < 0;
                }
                else
                {
                    return left;
                }
            }
        }

        private object ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (Match("+"))
                {
                    var right = ParseMultiplicative();
                    left = _parseOnly ? 0L : AddValues(left, right);
                }
                else if (Match("-"))
                {
                    var right = ParseMultiplicative();
                    left = _parseOnly ? 0L : SubtractValues(left, right);
                }
                else
                {
                    return left;
                }
            }
        }

        private object ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                if (Match("*"))
                {
                    var right = ParseUnary();
                    left = _parseOnly ? 0L : MultiplyValues(left, right);
                }
                else if (Match("/"))
                {
                    var right = ParseUnary();
                    left = _parseOnly ? 0L : DivideValues(left, right);
                }
                else if (Match("%"))
                {
                    var right = ParseUnary();
                    left = _parseOnly ? 0L : ModuloValues(left, right);
                }
                else
                {
                    return left;
                }
            }
        }

        private object ParseUnary()
        {
            if (Match("+"))
            {
                var value = ParseUnary();
                return _parseOnly ? 0L : NormalizeNumeric(value);
            }

            if (Match("-"))
            {
                var value = ParseUnary();
                return _parseOnly ? 0L : NegateValue(value);
            }

            if (Match("!"))
            {
                var value = ParseUnary();
                return _parseOnly ? true : !ToBool(value);
            }

            return ParsePrimary();
        }

        private object ParsePrimary()
        {
            SkipWhiteSpace();
            if (Match("("))
            {
                return WithDepth(() =>
                {
                    var value = ParseOr();
                    Expect(")");
                    return value;
                });
            }

            if (PeekNumberStart())
            {
                return ParseNumber();
            }

            if (PeekIdentifierStart())
            {
                var identifier = ParseIdentifier();
                if (Match("("))
                {
                    var args = new List<object>();
                    if (!Match(")"))
                    {
                        do
                        {
                            args.Add(ParseOr());
                            if (args.Count > MaxFunctionArgumentCount)
                            {
                                throw Error($"Expression function argument count exceeds {MaxFunctionArgumentCount}.");
                            }
                        }
                        while (Match(","));
                        Expect(")");
                    }

                    return EvaluateFunction(identifier, args);
                }

                if (identifier.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (identifier.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!_variables.TryGetValue(identifier, out var variableValue))
                {
                    throw Error($"Unknown variable '{identifier}'.");
                }

                return _parseOnly ? 0L : NormalizeValue(variableValue, identifier);
            }

            throw Error("Expected expression.");
        }

        private object ParseNumber()
        {
            SkipWhiteSpace();
            CountToken();
            var start = _position;
            while (_position < _text.Length &&
                   (char.IsDigit(_text[_position]) ||
                    _text[_position] is '.' or 'e' or 'E' or '+' or '-'))
            {
                if ((_text[_position] is '+' or '-') && _position > start && _text[_position - 1] is not ('e' or 'E'))
                {
                    break;
                }

                _position++;
            }

            var token = _text[start.._position];
            var isIntegerLiteral = token.IndexOfAny(['.', 'e', 'E']) < 0;
            if (isIntegerLiteral &&
                long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            {
                return longValue;
            }

            if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            {
                return decimalValue;
            }

            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) ||
                !double.IsFinite(doubleValue))
            {
                throw Error($"Invalid numeric literal '{token}'.");
            }

            return doubleValue;
        }

        private string ParseIdentifier()
        {
            SkipWhiteSpace();
            CountToken();
            var start = _position;
            while (_position < _text.Length &&
                   (char.IsLetterOrDigit(_text[_position]) || _text[_position] is '_' or '.'))
            {
                _position++;
            }

            return _text[start.._position];
        }

        private object EvaluateFunction(string name, IReadOnlyList<object> args)
        {
            if (_parseOnly)
            {
                return name.ToLowerInvariant() switch
                {
                    "round" or "floor" or "ceil" or "ceiling" or "truncate" or "trunc" or "abs" or "sqrt" when args.Count == 1 => 0L,
                    "min" or "max" or "pow" when args.Count == 2 => 0L,
                    _ => throw Error($"Unsupported expression function '{name}' or wrong argument count.")
                };
            }

            return name.ToLowerInvariant() switch
            {
                "round" when args.Count == 1 => Math.Round(ToDouble(args[0]), MidpointRounding.AwayFromZero),
                "floor" when args.Count == 1 => Math.Floor(ToDouble(args[0])),
                "ceil" or "ceiling" when args.Count == 1 => Math.Ceiling(ToDouble(args[0])),
                "truncate" or "trunc" when args.Count == 1 => Math.Truncate(ToDouble(args[0])),
                "abs" when args.Count == 1 => AbsValue(args[0]),
                "sqrt" when args.Count == 1 => EnsureFinite(Math.Sqrt(ToDouble(args[0]))),
                "min" when args.Count == 2 => LessThanOrEqual(args[0], args[1]) ? args[0] : args[1],
                "max" when args.Count == 2 => LessThanOrEqual(args[0], args[1]) ? args[1] : args[0],
                "pow" when args.Count == 2 => EnsureFinite(Math.Pow(ToDouble(args[0]), ToDouble(args[1]))),
                _ => throw Error($"Unsupported expression function '{name}' or wrong argument count.")
            };
        }

        private bool Match(string token)
        {
            SkipWhiteSpace();
            if (!_text.AsSpan(_position).StartsWith(token, StringComparison.Ordinal))
            {
                return false;
            }

            _position += token.Length;
            CountToken();
            return true;
        }

        private void Expect(string token)
        {
            if (!Match(token))
            {
                throw Error($"Expected '{token}'.");
            }
        }

        private void SkipWhiteSpace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        private bool PeekNumberStart()
        {
            SkipWhiteSpace();
            return _position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.');
        }

        private bool PeekIdentifierStart()
        {
            SkipWhiteSpace();
            return _position < _text.Length && (char.IsLetter(_text[_position]) || _text[_position] == '_');
        }

        private InvalidOperationException Error(string message) => new($"{message} Position={_position}.");

        private void CountToken()
        {
            _tokenCount++;
            if (_tokenCount > MaxTokenCount)
            {
                throw Error($"Expression token count exceeds {MaxTokenCount}.");
            }
        }

        private object WithDepth(Func<object> parse)
        {
            _depth++;
            if (_depth > MaxAstDepth)
            {
                throw Error($"Expression AST depth exceeds {MaxAstDepth}.");
            }

            try
            {
                return parse();
            }
            finally
            {
                _depth--;
            }
        }

        private static object NormalizeValue(object? value, string name)
        {
            if (value is JsonElement element)
            {
                return ProjectVariableValueConverter.ToObject(element)
                    ?? throw new InvalidOperationException($"Variable '{name}' is null.");
            }

            return value ?? throw new InvalidOperationException($"Variable '{name}' is null.");
        }

        private static bool AreEqual(object left, object right)
        {
            if (left is bool || right is bool)
            {
                return ToBool(left) == ToBool(right);
            }

            return ToDecimal(left) == ToDecimal(right);
        }

        private static bool ToBool(object value)
        {
            return value switch
            {
                bool boolean => boolean,
                string text when bool.TryParse(text, out var parsed) => parsed,
                long longValue => longValue != 0,
                int intValue => intValue != 0,
                decimal decimalValue => decimalValue != 0m,
                _ => Math.Abs(ToDouble(value)) > double.Epsilon
            };
        }

        private static object NormalizeNumeric(object value)
        {
            _ = ToDecimal(value);
            return value;
        }

        private static object AddValues(object left, object right)
        {
            if (TryGetInt64(left, out var leftLong) && TryGetInt64(right, out var rightLong))
            {
                return checked(leftLong + rightLong);
            }

            return checked(ToDecimal(left) + ToDecimal(right));
        }

        private static object SubtractValues(object left, object right)
        {
            if (TryGetInt64(left, out var leftLong) && TryGetInt64(right, out var rightLong))
            {
                return checked(leftLong - rightLong);
            }

            return checked(ToDecimal(left) - ToDecimal(right));
        }

        private static object MultiplyValues(object left, object right)
        {
            if (TryGetInt64(left, out var leftLong) && TryGetInt64(right, out var rightLong))
            {
                return checked(leftLong * rightLong);
            }

            return checked(ToDecimal(left) * ToDecimal(right));
        }

        private static object DivideValues(object left, object right)
        {
            var rightDecimal = ToDecimal(right);
            if (rightDecimal == 0m)
            {
                throw new DivideByZeroException("Division by zero.");
            }

            if (TryGetInt64(left, out var leftLong) &&
                TryGetInt64(right, out var rightLong) &&
                leftLong % rightLong == 0)
            {
                return checked(leftLong / rightLong);
            }

            return checked(ToDecimal(left) / rightDecimal);
        }

        private static object ModuloValues(object left, object right)
        {
            var rightDecimal = ToDecimal(right);
            if (rightDecimal == 0m)
            {
                throw new DivideByZeroException("Division by zero.");
            }

            if (TryGetInt64(left, out var leftLong) && TryGetInt64(right, out var rightLong))
            {
                return checked(leftLong % rightLong);
            }

            return checked(ToDecimal(left) % rightDecimal);
        }

        private static object NegateValue(object value)
        {
            if (TryGetInt64(value, out var longValue))
            {
                return checked(-longValue);
            }

            return checked(-ToDecimal(value));
        }

        private static object AbsValue(object value)
        {
            if (TryGetInt64(value, out var longValue))
            {
                return checked(Math.Abs(longValue));
            }

            return Math.Abs(ToDecimal(value));
        }

        private static bool LessThanOrEqual(object left, object right) => CompareValues(left, right) <= 0;

        private static bool TryGetInt64(object value, out long result)
        {
            switch (value)
            {
                case long longValue:
                    result = longValue;
                    return true;
                case int intValue:
                    result = intValue;
                    return true;
                case short shortValue:
                    result = shortValue;
                    return true;
                case byte byteValue:
                    result = byteValue;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static decimal ToDecimal(object value)
        {
            return value switch
            {
                decimal decimalValue => decimalValue,
                long longValue => longValue,
                int intValue => intValue,
                short shortValue => shortValue,
                byte byteValue => byteValue,
                double doubleValue when double.IsFinite(doubleValue) => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
                float floatValue when float.IsFinite(floatValue) => Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture),
                bool boolValue => boolValue ? 1m : 0m,
                string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new InvalidOperationException($"Value type '{value.GetType().Name}' is not numeric.")
            };
        }

        private static int CompareValues(object left, object right)
        {
            if (left is bool or string || right is bool or string)
            {
                throw new InvalidOperationException("Relational comparison requires numeric values.");
            }

            return ToDecimal(left).CompareTo(ToDecimal(right));
        }

        private static double ToDouble(object value)
        {
            var result = value switch
            {
                double doubleValue => doubleValue,
                float floatValue => floatValue,
                decimal decimalValue => ToExactDouble(decimalValue),
                long longValue => ToExactDouble(longValue),
                int intValue => intValue,
                short shortValue => shortValue,
                byte byteValue => byteValue,
                bool boolValue => boolValue ? 1.0d : 0.0d,
                string text when decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue) =>
                    ToExactDouble(decimalValue),
                string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new InvalidOperationException($"Value type '{value.GetType().Name}' is not numeric.")
            };

            return EnsureFinite(result);
        }

        private static double ToExactDouble(long value)
        {
            var result = (double)value;
            if ((decimal)result != value)
            {
                throw new InvalidOperationException($"Int64 value '{value}' cannot be converted to Double without precision loss.");
            }

            return result;
        }

        private static double ToExactDouble(decimal value)
        {
            var result = (double)value;
            try
            {
                if (Convert.ToDecimal(result, CultureInfo.InvariantCulture) != value)
                {
                    throw new InvalidOperationException(
                        $"Decimal value '{value.ToString(CultureInfo.InvariantCulture)}' cannot be converted to Double without precision loss.");
                }
            }
            catch (OverflowException ex)
            {
                throw new InvalidOperationException(
                    $"Decimal value '{value.ToString(CultureInfo.InvariantCulture)}' cannot be converted to Double without range loss.",
                    ex);
            }

            return result;
        }

        private static double EnsureFinite(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("Expression result must be finite.");
            }

            return value;
        }
    }
}
