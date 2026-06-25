using System.Globalization;
using System.Text.Json;

namespace ClearVision.Product.Core.ProjectVariables;

public static class ProjectVariableExpressionEvaluator
{
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
            var parser = new Parser(expression, variables);
            value = parser.Parse();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly IReadOnlyDictionary<string, object?> _variables;
        private int _position;

        public Parser(string text, IReadOnlyDictionary<string, object?> variables)
        {
            _text = text;
            _variables = variables;
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
                left = ToBool(left) || ToBool(ParseAnd());
            }

            return left;
        }

        private object ParseAnd()
        {
            var left = ParseEquality();
            while (Match("&&"))
            {
                left = ToBool(left) && ToBool(ParseEquality());
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
                    left = AreEqual(left, ParseComparison());
                }
                else if (Match("!="))
                {
                    left = !AreEqual(left, ParseComparison());
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
                    left = ToDouble(left) >= ToDouble(ParseAdditive());
                }
                else if (Match("<="))
                {
                    left = ToDouble(left) <= ToDouble(ParseAdditive());
                }
                else if (Match(">"))
                {
                    left = ToDouble(left) > ToDouble(ParseAdditive());
                }
                else if (Match("<"))
                {
                    left = ToDouble(left) < ToDouble(ParseAdditive());
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
                    left = EnsureFinite(ToDouble(left) + ToDouble(ParseMultiplicative()));
                }
                else if (Match("-"))
                {
                    left = EnsureFinite(ToDouble(left) - ToDouble(ParseMultiplicative()));
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
                    left = EnsureFinite(ToDouble(left) * ToDouble(ParseUnary()));
                }
                else if (Match("/"))
                {
                    left = EnsureFinite(ToDouble(left) / ToDouble(ParseUnary()));
                }
                else if (Match("%"))
                {
                    left = EnsureFinite(ToDouble(left) % ToDouble(ParseUnary()));
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
                return EnsureFinite(ToDouble(ParseUnary()));
            }

            if (Match("-"))
            {
                return EnsureFinite(-ToDouble(ParseUnary()));
            }

            if (Match("!"))
            {
                return !ToBool(ParseUnary());
            }

            return ParsePrimary();
        }

        private object ParsePrimary()
        {
            SkipWhiteSpace();
            if (Match("("))
            {
                var value = ParseOr();
                Expect(")");
                return value;
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

                return NormalizeValue(variableValue, identifier);
            }

            throw Error("Expected expression.");
        }

        private double ParseNumber()
        {
            SkipWhiteSpace();
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
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value))
            {
                throw Error($"Invalid numeric literal '{token}'.");
            }

            return value;
        }

        private string ParseIdentifier()
        {
            SkipWhiteSpace();
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
            return name.ToLowerInvariant() switch
            {
                "round" when args.Count == 1 => Math.Round(ToDouble(args[0]), MidpointRounding.AwayFromZero),
                "floor" when args.Count == 1 => Math.Floor(ToDouble(args[0])),
                "ceil" or "ceiling" when args.Count == 1 => Math.Ceiling(ToDouble(args[0])),
                "truncate" or "trunc" when args.Count == 1 => Math.Truncate(ToDouble(args[0])),
                "abs" when args.Count == 1 => Math.Abs(ToDouble(args[0])),
                "sqrt" when args.Count == 1 => EnsureFinite(Math.Sqrt(ToDouble(args[0]))),
                "min" when args.Count == 2 => Math.Min(ToDouble(args[0]), ToDouble(args[1])),
                "max" when args.Count == 2 => Math.Max(ToDouble(args[0]), ToDouble(args[1])),
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

            return Math.Abs(ToDouble(left) - ToDouble(right)) < 1e-12;
        }

        private static bool ToBool(object value)
        {
            return value switch
            {
                bool boolean => boolean,
                string text when bool.TryParse(text, out var parsed) => parsed,
                _ => Math.Abs(ToDouble(value)) > double.Epsilon
            };
        }

        private static double ToDouble(object value)
        {
            var result = value switch
            {
                double doubleValue => doubleValue,
                float floatValue => floatValue,
                decimal decimalValue => (double)decimalValue,
                long longValue => longValue,
                int intValue => intValue,
                short shortValue => shortValue,
                byte byteValue => byteValue,
                bool boolValue => boolValue ? 1.0d : 0.0d,
                string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new InvalidOperationException($"Value type '{value.GetType().Name}' is not numeric.")
            };

            return EnsureFinite(result);
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
