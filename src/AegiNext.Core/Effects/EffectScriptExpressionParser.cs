using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal sealed class EffectScriptExpressionParser(string source, int line, int column)
{
    private const int MAXIMUM_DEPTH = 64;
    private int offset;
    private int depth;
    private string token = string.Empty;
    private int tokenStart;

    internal static EffectScriptExpression Parse(string source, int line, int column)
    {
        var parser = new EffectScriptExpressionParser(source, line, column);
        parser.Next();
        var result = parser.Read(0);
        if (parser.token.Length != 0)
        {
            throw parser.Error("表达式包含多余内容。");
        }
        ValidateDepth(result);
        return result;
    }

    private static void ValidateDepth(EffectScriptExpression expression)
    {
        var pending = new Stack<(EffectScriptExpression Expression, int Depth)>();
        pending.Push((expression, 1));
        while (pending.TryPop(out var current))
        {
            if (current.Depth > MAXIMUM_DEPTH)
            {
                throw new EffectScriptException($"表达式树深度超过 {MAXIMUM_DEPTH} 层预算。", current.Expression.Line, current.Expression.Column);
            }
            foreach (var argument in current.Expression.Arguments)
            {
                pending.Push((argument, current.Depth + 1));
            }
        }
    }

    private EffectScriptExpression Read(int minimum)
    {
        if (++depth > MAXIMUM_DEPTH)
        {
            throw Error($"表达式嵌套超过 {MAXIMUM_DEPTH} 层预算。");
        }
        var start = tokenStart;
        EffectScriptExpression left;
        if (token is "+" or "-" or "not")
        {
            var operation = token;
            Next();
            left = new(EffectScriptExpressionKind.UNARY, operation, [Read(7)], line, column + start);
        }
        else if (token == "(")
        {
            Next();
            left = Read(0);
            if (token == ",")
            {
                var args = ImmutableArray.CreateBuilder<EffectScriptExpression>();
                args.Add(left);
                while (token == ",")
                {
                    Next();
                    args.Add(Read(0));
                }
                left = new(EffectScriptExpressionKind.CALL, args.Count == 2 ? "vec2" : "vec3", args.ToImmutable(), line, column + start);
            }
            Require(")");
        }
        else if (token.Length > 0 && (char.IsAsciiDigit(token[0]) || token[0] == '.'))
        {
            var text = token;
            Next();
            var duration = text.EndsWith('s');
            var milliseconds = text.EndsWith("ms", StringComparison.Ordinal);
            var number = duration ? text[..^(milliseconds ? 2 : 1)] : text;
            EffectScriptRational exact;
            try
            {
                exact = EffectScriptRational.Parse(number);
            }
            catch (Exception error) when (error is FormatException or OverflowException)
            {
                throw new EffectScriptException("应使用预算内的精确数字字面量。", line, column + start, error);
            }
            _ = decimal.TryParse(number, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value);
            var literal = duration ? EffectScriptParameterValue.FromDuration(
                EffectScriptRational.Divide(exact, new(milliseconds ? 1000 : 1, 1)).Time) : EffectScriptParameterValue.FromNumber(value);
            left = new(EffectScriptExpressionKind.LITERAL, text, [], line, column + start) { Literal = literal, ExactNumber = duration ? null : exact };
        }
        else if (token.StartsWith('"'))
        {
            var value = System.Text.Json.JsonSerializer.Deserialize<string>(token) ?? string.Empty;
            ProjectValidator.ValidateText(value);
            left = new(EffectScriptExpressionKind.LITERAL, token, [], line, column + start) { Literal = EffectScriptParameterValue.FromString(value) };
            Next();
        }
        else if (token is "true" or "false")
        {
            left = new(EffectScriptExpressionKind.LITERAL, token, [], line, column + start) { Literal = EffectScriptParameterValue.FromBoolean(token == "true") };
            Next();
        }
        else if (token.Length > 0 && (char.IsAsciiLetter(token[0]) || token[0] == '_'))
        {
            var name = token;
            Next();
            if (token == "(")
            {
                Next();
                var args = ImmutableArray.CreateBuilder<EffectScriptExpression>();
                if (token != ")")
                {
                    args.Add(Read(0));
                    while (token == ",")
                    {
                        Next();
                        args.Add(Read(0));
                    }
                }
                Require(")");
                left = new(EffectScriptExpressionKind.CALL, name, args.ToImmutable(), line, column + start);
            }
            else
            {
                left = new(EffectScriptExpressionKind.NAME, name, [], line, column + start);
            }
        }
        else
        {
            throw Error("缺少表达式。");
        }
        while (Precedence(token) is var precedence && precedence > minimum)
        {
            var operation = token;
            var operationColumn = tokenStart;
            Next();
            left = new(EffectScriptExpressionKind.BINARY, operation, [left, Read(precedence)], line, column + operationColumn);
        }
        depth--;
        return left;
    }

    private static int Precedence(string value) => value switch
    {
        "or" => 1,
        "and" => 2,
        "==" or "!=" => 3,
        "<" or ">" or "<=" or ">=" => 4,
        "+" or "-" => 5,
        "*" or "/" or "%" => 6,
        _ => 0
    };

    private void Require(string value)
    {
        if (token != value)
        {
            throw Error($"应使用 {value}。");
        }
        Next();
    }

    private void Next()
    {
        while (offset < source.Length && char.IsWhiteSpace(source[offset]))
        {
            offset++;
        }
        tokenStart = offset;
        if (offset == source.Length)
        {
            token = string.Empty;
            return;
        }
        var first = source[offset++];
        if (first == '"')
        {
            var escaped = false;
            while (offset < source.Length)
            {
                var next = source[offset++];
                if (next == '"' && !escaped)
                {
                    token = source[tokenStart..offset];
                    return;
                }
                escaped = next == '\\' && !escaped;
            }
            throw Error("字符串缺少闭合引号。");
        }
        if (char.IsAsciiDigit(first) || first == '.')
        {
            while (offset < source.Length && (char.IsAsciiDigit(source[offset]) || source[offset] == '.'))
            {
                offset++;
            }
            if (offset < source.Length && source[offset] is 'e' or 'E')
            {
                offset++;
                if (offset < source.Length && source[offset] is '+' or '-')
                {
                    offset++;
                }
                while (offset < source.Length && char.IsAsciiDigit(source[offset]))
                {
                    offset++;
                }
            }
            if (source.AsSpan(offset).StartsWith("ms", StringComparison.Ordinal))
            {
                offset += 2;
            }
            else if (offset < source.Length && source[offset] == 's')
            {
                offset++;
            }
        }
        else if (char.IsAsciiLetter(first) || first == '_')
        {
            while (offset < source.Length && (char.IsAsciiLetterOrDigit(source[offset]) || source[offset] is '_' or '.'))
            {
                offset++;
            }
        }
        else if (offset < source.Length && source[offset] == '=' && first is '=' or '!' or '<' or '>')
        {
            offset++;
        }
        token = source[tokenStart..offset];
    }

    private EffectScriptException Error(string message) => new(message, line, column + tokenStart);
}
