using System.Collections.Immutable;
using System.Text.RegularExpressions;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal sealed class EffectScriptV3Parser
{
    private static readonly Regex header = new("^effect\\s+\"(?<id>[a-z][a-z0-9.-]{0,63})\"\\s+version\\s+3$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex namedBlock = new("^(scope|segment)\\s+(?<name>[a-z][a-z0-9.-]{0,63})\\s+(?<rest>.+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex variable = new("^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*(?<rest>.+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex loop = new("^for\\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s+in\\s+(?<range>range\\(.*\\))$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string propertyAlternatives = string.Join("|", EffectScriptPropertyMetadata.PropertyNames.Where(name => !name.StartsWith("mask-node", StringComparison.Ordinal)).OrderByDescending(name => name.Length).Select(Regex.Escape));
    private static readonly Regex keyframe = new("^at\\s+(?<progress>.+?)\\s+(?<property>" + propertyAlternatives + "|mask-node\\(\\s*\\d+\\s*,\\s*\\d+\\s*\\)\\.[a-z-]+)\\s+(?<value>.+)$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly List<(string Text, int Line, int Column)> lines = [];
    private readonly List<EffectScriptParameter> parameters = [];
    private readonly HashSet<string> scopeNames = new(StringComparer.Ordinal);
    private int offset;
    private int scopes;
    private int segments;
    private int keys;

    internal static EffectScript Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > EffectScriptParser.MAXIMUM_SOURCE_CHARACTERS)
        {
            throw new EffectScriptException("脚本超过 256 Ki 字符预算。");
        }
        var parser = new EffectScriptV3Parser();
        var rawLines = source.TrimStart('\uFEFF').Split('\n');
        for (var index = 0; index < rawLines.Length; index++)
        {
            var text = EffectScriptV2Lexer.StripComment(rawLines[index], index + 1).Trim();
            if (text.Length > 0)
            {
                parser.lines.Add((text, index + 1, rawLines[index].TakeWhile(char.IsWhiteSpace).Count() + 1));
            }
        }
        if (parser.lines.Count < 2 || !header.IsMatch(parser.lines[0].Text))
        {
            throw new EffectScriptException("首条指令应为 effect \"稳定标识\" version 3。", parser.lines.Count > 0 ? parser.lines[0].Line : 1);
        }
        var id = header.Match(parser.lines[0].Text).Groups["id"].Value;
        EffectScriptShortClipPolicy? policy = null;
        parser.offset = 1;
        while (parser.offset < parser.lines.Count)
        {
            var current = parser.lines[parser.offset];
            if (current.Text.StartsWith("var ", StringComparison.Ordinal))
            {
                parser.ReadParameter();
                continue;
            }
            if (!current.Text.StartsWith("short-clip ", StringComparison.Ordinal))
            {
                break;
            }
            if (policy.HasValue)
            {
                throw Error(current, "short-clip 只能声明一次。");
            }
            policy = current.Text switch
            {
                "short-clip compress" => EffectScriptShortClipPolicy.COMPRESS,
                "short-clip reject" => EffectScriptShortClipPolicy.REJECT,
                _ => throw Error(current, "应显式指定 short-clip compress 或 short-clip reject。")
            };
            parser.offset++;
        }
        if (!policy.HasValue)
        {
            throw new EffectScriptException("应显式指定 short-clip compress 或 short-clip reject。");
        }
        var statements = parser.ReadStatements(0, 0);
        if (parser.offset != parser.lines.Count)
        {
            var extra = parser.lines[parser.offset];
            throw new EffectScriptException("多余的块结束指令。", extra.Line, extra.Column);
        }
        var script = new EffectScript(id, policy.Value, []) { Version = 3, Parameters = parser.parameters.ToImmutableArray(), Statements = statements };
        _ = EffectScriptParameterBinder.Bind(script);
        EffectScriptV3Validator.Validate(script);
        return EffectScriptInstanceCompiler.ProjectDefaults(script);
    }

    internal static EffectScriptParameterValue ParseConstant(string source)
    {
        var expression = EffectScriptExpressionParser.Parse(source, 0, 1);
        _ = EffectScriptExpressionEvaluator.Infer(expression, new Dictionary<string, EffectScriptParameterKind>());
        var value = EffectScriptExpressionEvaluator.Evaluate(expression, new Dictionary<string, EffectScriptEvaluationValue>());
        if (value.Kind == EffectScriptParameterKind.NUMBER && EffectScriptRational.From(value.Number.Decimal) != value.Number)
        {
            throw new EffectScriptException("参数常量数字必须可精确表示为十进制数。");
        }
        var parameter = value.Parameter;
        EffectScriptParameterBinder.Validate(parameter);
        return parameter;
    }

    private void ReadParameter()
    {
        var source = lines[offset++];
        var match = variable.Match(source.Text[4..].Trim());
        if (!match.Success)
        {
            throw Error(source, "参数应为 var 名称 = 常量。");
        }
        var name = match.Groups["name"].Value;
        if (parameters.Any(parameter => parameter.Name == name) || name is "clip" or "effect" or "group" or "base" or "true" or "false" or "and" or "or" or "not")
        {
            throw Error(source, "参数名称重复或使用保留名称。");
        }
        var parts = SplitModifiers(match.Groups["rest"].Value, ["label", "options", "suggest"]);
        var value = Constant(parts[0].Value, source);
        string? label = null;
        var options = ImmutableArray<EffectScriptParameterChoice>.Empty;
        var suggestions = ImmutableArray<EffectScriptParameterChoice>.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts.Skip(1))
        {
            if (!seen.Add(part.Name))
            {
                throw Error(source, "参数元数据不能重复。");
            }
            if (part.Name == "label")
            {
                var text = Constant(part.Value, source);
                if (text.Kind != EffectScriptParameterKind.STRING)
                {
                    throw Error(source, "label 需要字符串。");
                }
                label = text.Text;
                continue;
            }
            var choices = ReadChoices(part.Value, source);
            if (choices.Any(choice => choice.Value.Kind != value.Kind) || choices.Select(choice => choice.Label).Distinct(StringComparer.Ordinal).Count() != choices.Length ||
                choices.Select(choice => choice.Value).Distinct().Count() != choices.Length)
            {
                throw Error(source, "参数选项类型必须一致，标签和值不能重复。");
            }
            if (part.Name == "options")
            {
                options = choices;
            }
            else
            {
                suggestions = choices;
            }
        }
        if (!options.IsEmpty && !suggestions.IsEmpty || !options.IsEmpty && !options.Any(choice => choice.Value == value))
        {
            throw Error(source, "options 与 suggest 不能并存，默认值必须属于 options。");
        }
        parameters.Add(new(name, value, options, suggestions, source.Line, source.Column) { Label = label });
    }

    private static EffectScriptParameterValue Constant(string text, (string Text, int Line, int Column) source)
    {
        try
        {
            return EffectScriptParameterValue.Parse(text);
        }
        catch (Exception error) when (error is EffectScriptException or System.Text.Json.JsonException or InvalidDataException)
        {
            throw new EffectScriptException(error.Message, source.Line, source.Column, error);
        }
    }

    private static ImmutableArray<EffectScriptParameterChoice> ReadChoices(string text, (string Text, int Line, int Column) source)
    {
        if (!text.StartsWith('[') || !text.EndsWith(']'))
        {
            throw Error(source, "选项应为 [\"标签\":值, ...]。");
        }
        var choices = ImmutableArray.CreateBuilder<EffectScriptParameterChoice>();
        foreach (var item in SplitTopLevel(text[1..^1], ','))
        {
            var pair = SplitTopLevel(item, ':');
            if (pair.Count != 2 || Constant(pair[0], source) is not { Kind: EffectScriptParameterKind.STRING, Text: { } label })
            {
                throw Error(source, "每个选项需要字符串标签及常量值。");
            }
            choices.Add(new(label, Constant(pair[1], source)));
        }
        if (choices.Count == 0)
        {
            throw Error(source, "选项列表不能为空。");
        }
        return choices.ToImmutable();
    }

    private ImmutableArray<EffectScriptStatement> ReadStatements(int context, int depth)
    {
        if (depth > 16)
        {
            throw new EffectScriptException("声明块嵌套超过 16 层预算。");
        }
        var result = ImmutableArray.CreateBuilder<EffectScriptStatement>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        var fieldsClosed = false;
        while (offset < lines.Count)
        {
            var source = lines[offset];
            var text = source.Text;
            if (text == "end" || text == "else" || text.StartsWith("elif ", StringComparison.Ordinal))
            {
                break;
            }
            offset++;
            if (text.StartsWith("if ", StringComparison.Ordinal))
            {
                fieldsClosed = true;
                var condition = Expression(text[3..], source, 3);
                var body = ReadStatements(context, depth + 1);
                var alternatives = ImmutableArray.CreateBuilder<EffectScriptStatement>();
                while (offset < lines.Count && (lines[offset].Text.StartsWith("elif ", StringComparison.Ordinal) || lines[offset].Text == "else"))
                {
                    var alternative = lines[offset++];
                    var expressions = alternative.Text == "else" ? ImmutableArray<EffectScriptExpression>.Empty : [Expression(alternative.Text[5..], alternative, 5)];
                    alternatives.Add(new(EffectScriptStatementKind.IF, "", "", expressions, ReadStatements(context, depth + 1), alternative.Line, alternative.Column));
                    if (expressions.IsEmpty)
                    {
                        break;
                    }
                }
                End(source);
                result.Add(new(EffectScriptStatementKind.IF, "", "", [condition], body, source.Line, source.Column) { Alternatives = alternatives.ToImmutable() });
            }
            else if (text.StartsWith("for ", StringComparison.Ordinal))
            {
                fieldsClosed = true;
                var match = loop.Match(text);
                if (!match.Success)
                {
                    throw Error(source, "循环应为 for 名称 in range(start, stop[, step])。");
                }
                var range = Expression(match.Groups["range"].Value, source, match.Groups["range"].Index);
                if (range.Kind != EffectScriptExpressionKind.CALL || range.Text != "range" || range.Arguments.Length is < 2 or > 3)
                {
                    throw Error(source, "range 需要起点、半开终点及可选步长。");
                }
                var body = ReadStatements(context, depth + 1);
                End(source);
                result.Add(new(EffectScriptStatementKind.FOR, match.Groups["name"].Value, "", range.Arguments, body, source.Line, source.Column));
            }
            else if (text.StartsWith("scope ", StringComparison.Ordinal) && context == 0)
            {
                var match = namedBlock.Match(text);
                if (!match.Success || !scopeNames.Add(match.Groups["name"].Value) || ++scopes > 128)
                {
                    throw Error(source, "作用范围声明无效、名称重复或超过 128 个源声明。");
                }
                var target = ExpressionTarget(match.Groups["rest"].Value, source, match.Groups["rest"].Index, "current", "subtitle", "range");
                var body = ReadStatements(1, depth + 1);
                End(source);
                result.Add(new(EffectScriptStatementKind.SCOPE, match.Groups["name"].Value, match.Groups["rest"].Value, [target], body, source.Line, source.Column));
            }
            else if (text.StartsWith("segment ", StringComparison.Ordinal) && context == 1)
            {
                fieldsClosed = true;
                var match = namedBlock.Match(text);
                var rest = match.Groups["rest"].Value;
                var space = rest.IndexOf(' ');
                if (!match.Success || space < 0 || rest[..space] is not ("fixed" or "flex") || ++segments > 128)
                {
                    throw Error(source, "时间段声明无效或超过 128 个源声明。");
                }
                var parts = SplitModifiers(rest[(space + 1)..], ["repeat", "cycle", "pingpong"]);
                var amount = Expression(parts[0].Value, source, text.IndexOf(parts[0].Value, StringComparison.Ordinal));
                var repeat = Expression("1", source, 0);
                var cycle = Expression("0s", source, 0);
                var pingPong = false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var part in parts.Skip(1))
                {
                    if (!seen.Add(part.Name))
                    {
                        throw Error(source, "时间段修饰符不能重复。");
                    }
                    if (part.Name == "repeat")
                    {
                        repeat = Expression(part.Value, source, text.IndexOf(part.Value, StringComparison.Ordinal));
                    }
                    else if (part.Name == "cycle")
                    {
                        cycle = Expression(part.Value, source, text.IndexOf(part.Value, StringComparison.Ordinal));
                    }
                    else if (part.Value.Length == 0)
                    {
                        pingPong = true;
                    }
                    else
                    {
                        throw Error(source, "pingpong 不带参数。");
                    }
                }
                var body = ReadStatements(2, depth + 1);
                End(source);
                result.Add(new(EffectScriptStatementKind.SEGMENT, match.Groups["name"].Value, rest[..space], [amount, repeat, cycle], body, source.Line, source.Column) { PingPong = pingPong, HasCycle = seen.Contains("cycle"), HasRepeat = seen.Contains("repeat") });
            }
            else if (text.StartsWith("at ", StringComparison.Ordinal) && context == 2)
            {
                if (++keys > 4096)
                {
                    throw Error(source, "源关键帧超过 4096 个预算。");
                }
                result.Add(ReadKeyframe(source));
            }
            else if (context == 1 && !fieldsClosed)
            {
                var space = text.IndexOf(' ');
                var field = space > 0 ? text[..space] : text;
                var value = space > 0 ? text[(space + 1)..] : "";
                if (!fields.Add(field) || field is not ("unit" or "delay" or "stagger" or "order" or "state"))
                {
                    throw Error(source, "未知、重复或位置无效的作用范围字段。");
                }
                var expression = field switch
                {
                    "unit" => ExpressionTarget(value, source, space + 1, "group", "grapheme", "word", "line", "paragraph", "chunk", "split"),
                    "order" when value is "forward" or "reverse" => String(value, source),
                    "state" when value is "normal" or "active" or "inactive" => String(value, source),
                    _ => Expression(value, source, space + 1)
                };
                result.Add(new(EffectScriptStatementKind.FIELD, field, value, [expression], [], source.Line, source.Column));
            }
            else
            {
                throw Error(source, "当前声明块不允许此指令。");
            }
        }
        return result.ToImmutable();
    }

    private static EffectScriptStatement ReadKeyframe((string Text, int Line, int Column) source)
    {
        var match = keyframe.Match(source.Text);
        if (!match.Success)
        {
            throw Error(source, "关键帧应为 at 位置 属性 值 [插值]。");
        }
        var property = match.Groups["property"].Value;
        _ = EffectScriptParser.ParseProperty(property, source.Line, source.Column + match.Groups["property"].Index);
        var text = match.Groups["value"].Value;
        var interpolation = Regex.Match(text, "\\s+(?<curve>hold|linear|ease-in|ease-out|ease-in-out|power\\(.+\\))$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        var curve = interpolation.Success ? interpolation.Groups["curve"].Value : "linear";
        if (interpolation.Success)
        {
            text = text[..interpolation.Index].TrimEnd();
        }
        var valueKind = text == "base" ? EffectScriptValueKind.BASE : text.StartsWith("offset(", StringComparison.Ordinal) ? EffectScriptValueKind.OFFSET :
            text.StartsWith("factor(", StringComparison.Ordinal) ? EffectScriptValueKind.FACTOR : EffectScriptValueKind.ABSOLUTE;
        var value = text;
        if (valueKind is EffectScriptValueKind.OFFSET or EffectScriptValueKind.FACTOR)
        {
            value = text[text.IndexOf('(')..];
        }
        var exponent = curve.StartsWith("power(", StringComparison.Ordinal) ? curve[6..^1] : "1";
        return new(EffectScriptStatementKind.KEYFRAME, curve.StartsWith("power(", StringComparison.Ordinal) ? "power" : curve, property,
            [Expression(match.Groups["progress"].Value, source, match.Groups["progress"].Index),
                Expression(valueKind == EffectScriptValueKind.BASE ? "0" : value, source, match.Groups["value"].Index), Expression(exponent, source, match.Groups["value"].Index)],
            [], source.Line, source.Column) { ValueKind = valueKind };
    }

    private void End((string Text, int Line, int Column) source)
    {
        if (offset >= lines.Count || lines[offset].Text != "end")
        {
            throw Error(source, "声明块缺少 end。");
        }
        offset++;
    }

    private static EffectScriptExpression Expression(string text, (string Text, int Line, int Column) source, int offset) => EffectScriptExpressionParser.Parse(text, source.Line, source.Column + offset);
    private static EffectScriptExpression String(string text, (string Text, int Line, int Column) source) => new(EffectScriptExpressionKind.LITERAL, text, [], source.Line, source.Column) { Literal = EffectScriptParameterValue.FromString(text) };
    private static EffectScriptExpression ExpressionTarget(string text, (string Text, int Line, int Column) source, int offset, params string[] allowed)
    {
        var expression = allowed.Contains(text, StringComparer.Ordinal) ? String(text, source) : Expression(text, source, offset);
        if (expression.Kind == EffectScriptExpressionKind.CALL && !allowed.Contains(expression.Text, StringComparer.Ordinal))
        {
            throw Error(source, "未知的目标或分组构造器。");
        }
        return expression;
    }

    internal static List<string> SplitTopLevel(string text, char separator)
    {
        var result = new List<string>();
        var depth = 0;
        var quoted = false;
        var escaped = false;
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && !escaped)
                {
                    quoted = false;
                }
                escaped = character == '\\' && !escaped;
                continue;
            }
            if (character == '"')
            {
                quoted = true;
            }
            else if (character is '(' or '[')
            {
                depth++;
            }
            else if (character is ')' or ']')
            {
                depth--;
            }
            else if (character == separator && depth == 0)
            {
                result.Add(text[start..index].Trim());
                start = index + 1;
            }
        }
        if (text[start..].Trim() is { Length: > 0 } last)
        {
            result.Add(last);
        }
        return result;
    }

    private static List<(string Name, string Value)> SplitModifiers(string text, string[] names)
    {
        var result = new List<(string Name, string Value)>();
        var words = SplitTopLevel(text, ' ');
        var name = "";
        var values = new List<string>();
        foreach (var word in words)
        {
            if (names.Contains(word, StringComparer.Ordinal))
            {
                result.Add((name, string.Join(' ', values)));
                name = word;
                values.Clear();
            }
            else
            {
                values.Add(word);
            }
        }
        result.Add((name, string.Join(' ', values)));
        return result;
    }

    private static EffectScriptException Error((string Text, int Line, int Column) source, string message) => new(message, source.Line, source.Column);
}
