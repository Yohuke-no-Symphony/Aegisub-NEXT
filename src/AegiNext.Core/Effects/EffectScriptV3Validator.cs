using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal static class EffectScriptV3Validator
{
    internal static void Validate(EffectScript script)
    {
        if (script.Id is not { Length: > 0 and <= 64 } || !char.IsAsciiLetterLower(script.Id[0]) ||
            script.Id.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('.' or '-')) ||
            !Enum.IsDefined(script.ShortClipPolicy) || script.Parameters.IsDefault || script.Statements.IsDefault ||
            script.Statements.IsEmpty && (!script.Segments.IsEmpty || !script.Scopes.IsEmpty))
        {
            throw new EffectScriptException("脚本标识、策略或声明集合无效。");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in script.Parameters)
        {
            if (parameter is null || string.IsNullOrEmpty(parameter.Name) || !names.Add(parameter.Name) || parameter.Options.IsDefault || parameter.Suggestions.IsDefault)
            {
                throw new EffectScriptException("参数声明重复或无效。");
            }
            EffectScriptParameterBinder.Validate(parameter.DefaultValue, parameter.Line, parameter.Column);
        }
        var symbols = script.Parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Kind, StringComparer.Ordinal);
        symbols.Add("clip.duration", EffectScriptParameterKind.DURATION);
        symbols.Add("effect.duration", EffectScriptParameterKind.DURATION);
        Visit(script.Statements, symbols);
    }

    private static void Visit(ImmutableArray<EffectScriptStatement> statements, Dictionary<string, EffectScriptParameterKind> symbols)
    {
        foreach (var statement in statements)
        {
            var expressions = statement.Expressions;
            switch (statement.Kind)
            {
                case EffectScriptStatementKind.IF:
                    Require(expressions[0], symbols, EffectScriptParameterKind.BOOLEAN);
                    Visit(statement.Statements, symbols);
                    foreach (var alternative in statement.Alternatives)
                    {
                        if (!alternative.Expressions.IsEmpty)
                        {
                            Require(alternative.Expressions[0], symbols, EffectScriptParameterKind.BOOLEAN);
                        }
                        Visit(alternative.Statements, symbols);
                    }
                    break;
                case EffectScriptStatementKind.FOR:
                    if (symbols.ContainsKey(statement.Name) || statement.Name is "clip" or "effect" or "group" or "base")
                    {
                        throw Error(statement, "循环索引只读，不能遮蔽已有变量。");
                    }
                    foreach (var expression in expressions)
                    {
                        Require(expression, symbols, EffectScriptParameterKind.NUMBER);
                    }
                    var local = new Dictionary<string, EffectScriptParameterKind>(symbols, StringComparer.Ordinal) { [statement.Name] = EffectScriptParameterKind.NUMBER };
                    Visit(statement.Statements, local);
                    break;
                case EffectScriptStatementKind.SCOPE:
                    Target(expressions[0], symbols, "range", EffectScriptParameterKind.NUMBER, 2);
                    foreach (var field in statement.Statements.Where(child => child.Kind == EffectScriptStatementKind.FIELD))
                    {
                        Field(field, symbols);
                    }
                    var grouped = new Dictionary<string, EffectScriptParameterKind>(symbols, StringComparer.Ordinal)
                    {
                        ["group.index"] = EffectScriptParameterKind.NUMBER,
                        ["group.count"] = EffectScriptParameterKind.NUMBER
                    };
                    Visit(statement.Statements.Where(child => child.Kind != EffectScriptStatementKind.FIELD).ToImmutableArray(), grouped);
                    break;
                case EffectScriptStatementKind.SEGMENT:
                    Require(expressions[0], symbols, statement.Text == "fixed" ? EffectScriptParameterKind.DURATION : EffectScriptParameterKind.NUMBER);
                    Require(expressions[1], symbols, EffectScriptParameterKind.NUMBER);
                    Require(expressions[2], symbols, EffectScriptParameterKind.DURATION);
                    Visit(statement.Statements, symbols);
                    break;
                case EffectScriptStatementKind.KEYFRAME:
                    Require(expressions[0], symbols, EffectScriptParameterKind.NUMBER);
                    Require(expressions[2], symbols, EffectScriptParameterKind.NUMBER);
                    var property = EffectScriptParser.ParseProperty(statement.Text, statement.Line, statement.Column).Property;
                    if (property == EffectScriptProperty.PATH_PROGRESS && statement.ValueKind != EffectScriptValueKind.ABSOLUTE)
                    {
                        throw Error(statement, "path-progress 需要显式数值。");
                    }
                    if (statement.ValueKind == EffectScriptValueKind.BASE)
                    {
                        break;
                    }
                    var expected = AnimationPropertyMetadata.GetValueKind(EffectScriptPropertyMetadata.GetAnimationProperty(property)) switch
                    {
                        AnimationValueKind.VECTOR => EffectScriptParameterKind.VECTOR2,
                        AnimationValueKind.VECTOR3 => EffectScriptParameterKind.VECTOR3,
                        AnimationValueKind.COLOR => EffectScriptParameterKind.COLOR,
                        _ => EffectScriptParameterKind.NUMBER
                    };
                    var actual = EffectScriptExpressionEvaluator.Infer(expressions[1], symbols);
                    if (actual != expected && !(property == EffectScriptProperty.ROTATION && actual == EffectScriptParameterKind.VECTOR3))
                    {
                        throw Error(statement, "关键帧值维度与属性不匹配。");
                    }
                    if (expected == EffectScriptParameterKind.COLOR && statement.ValueKind is not (EffectScriptValueKind.ABSOLUTE or EffectScriptValueKind.BASE))
                    {
                        throw Error(statement, "颜色只支持 rgba 或 base。");
                    }
                    if (property == EffectScriptProperty.PATH_PROGRESS && statement.ValueKind != EffectScriptValueKind.ABSOLUTE)
                    {
                        throw Error(statement, "path-progress 需要显式数值。");
                    }
                    break;
            }
        }
    }

    private static void Field(EffectScriptStatement statement, Dictionary<string, EffectScriptParameterKind> symbols)
    {
        var expression = statement.Expressions[0];
        if (statement.Name == "unit" && expression.Kind == EffectScriptExpressionKind.CALL)
        {
            Target(expression, symbols, expression.Text, expression.Text == "split" ? EffectScriptParameterKind.STRING : EffectScriptParameterKind.NUMBER,
                expression.Text == "split" ? -1 : 1);
        }
        else
        {
            Require(expression, symbols, statement.Name is "delay" or "stagger" ? EffectScriptParameterKind.DURATION : EffectScriptParameterKind.STRING);
        }
    }

    private static void Target(EffectScriptExpression expression, Dictionary<string, EffectScriptParameterKind> symbols, string function, EffectScriptParameterKind kind, int count)
    {
        if (expression.Kind != EffectScriptExpressionKind.CALL)
        {
            Require(expression, symbols, EffectScriptParameterKind.STRING);
            return;
        }
        if (expression.Text != function || count >= 0 && expression.Arguments.Length != count || count < 0 && expression.Arguments.IsEmpty)
        {
            throw new EffectScriptException("目标或分组构造器参数无效。", expression.Line, expression.Column);
        }
        foreach (var argument in expression.Arguments)
        {
            Require(argument, symbols, kind);
        }
    }

    private static void Require(EffectScriptExpression expression, Dictionary<string, EffectScriptParameterKind> symbols, EffectScriptParameterKind kind)
    {
        if (EffectScriptExpressionEvaluator.Infer(expression, symbols) != kind)
        {
            throw new EffectScriptException("表达式类型或单位不匹配。", expression.Line, expression.Column);
        }
    }

    private static EffectScriptException Error(EffectScriptStatement statement, string message) => new(message, statement.Line, statement.Column);
}
