using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal static class EffectScriptExpressionEvaluator
{
    internal static EffectScriptParameterKind Infer(EffectScriptExpression expression, IReadOnlyDictionary<string, EffectScriptParameterKind> symbols)
    {
        var args = expression.Arguments;
        var name = expression.Text;
        if (expression.Kind == EffectScriptExpressionKind.LITERAL)
        {
            return expression.Literal!.Kind;
        }
        if (expression.Kind == EffectScriptExpressionKind.NAME)
        {
            if (symbols.TryGetValue(name, out var kind))
            {
                return kind;
            }
            var dot = name.LastIndexOf('.');
            if (dot > 0 && symbols.TryGetValue(name[..dot], out kind) && kind is EffectScriptParameterKind.VECTOR2 or EffectScriptParameterKind.VECTOR3 or EffectScriptParameterKind.COLOR &&
                "xyzrgba".Contains(name[(dot + 1)..], StringComparison.Ordinal) && name.Length - dot == 2)
            {
                var component = name[(dot + 1)..] switch { "x" or "r" => 0, "y" or "g" => 1, "z" or "b" => 2, _ => 3 };
                var componentCount = kind == EffectScriptParameterKind.VECTOR2 ? 2 : kind == EffectScriptParameterKind.VECTOR3 ? 3 : 4;
                var validName = kind == EffectScriptParameterKind.COLOR ? "rgba" : "xyz";
                if (component >= componentCount || !validName.Contains(name[^1]))
                {
                    throw Error(expression, "参数不存在此分量。");
                }
                return EffectScriptParameterKind.NUMBER;
            }
            throw Error(expression, $"未知变量：{name}。");
        }
        if (args.IsEmpty)
        {
            throw Error(expression, "表达式构造器缺少参数。");
        }
        var first = Infer(args[0], symbols);
        if (expression.Kind == EffectScriptExpressionKind.UNARY)
        {
            if (name == "not" ? first != EffectScriptParameterKind.BOOLEAN : !Numeric(first))
            {
                throw Error(expression, "一元运算类型不匹配。");
            }
            return first;
        }
        if (expression.Kind == EffectScriptExpressionKind.CALL)
        {
            var count = name switch { "vec2" => 2, "vec3" => 3, "rgba" => 4, _ => 0 };
            if (count == 0 || args.Length != count || args.Any(arg => Infer(arg, symbols) != EffectScriptParameterKind.NUMBER))
            {
                throw Error(expression, "仅支持 vec2、vec3、rgba，且各分量须为无量纲数字。");
            }
            return name == "vec2" ? EffectScriptParameterKind.VECTOR2 : name == "vec3" ? EffectScriptParameterKind.VECTOR3 : EffectScriptParameterKind.COLOR;
        }
        var second = Infer(args[1], symbols);
        if (name is "and" or "or")
        {
            if (first != EffectScriptParameterKind.BOOLEAN || second != first)
            {
                throw Error(expression, "逻辑运算需要布尔值。");
            }
            return EffectScriptParameterKind.BOOLEAN;
        }
        if (name is "==" or "!=" or "<" or ">" or "<=" or ">=")
        {
            if (first != second || name is not ("==" or "!=") && !Numeric(first))
            {
                throw Error(expression, "比较运算需要相同类型；有序比较仅支持数字或时长。");
            }
            return EffectScriptParameterKind.BOOLEAN;
        }
        if (name is "+" or "-" && first == second && (Numeric(first) || Vector(first)))
        {
            return first;
        }
        if (name == "*" && first == EffectScriptParameterKind.NUMBER && (Numeric(second) || Vector(second)))
        {
            return second;
        }
        if (name is "*" or "/" && second == EffectScriptParameterKind.NUMBER && (Numeric(first) || Vector(first)))
        {
            return first;
        }
        if (name == "/" && first == EffectScriptParameterKind.DURATION && second == first || name == "%" && first == EffectScriptParameterKind.NUMBER && second == first)
        {
            return EffectScriptParameterKind.NUMBER;
        }
        throw Error(expression, "算术运算类型或单位不匹配。");
    }

    internal static EffectScriptEvaluationValue Evaluate(EffectScriptExpression expression, IReadOnlyDictionary<string, EffectScriptEvaluationValue> symbols)
    {
        try
        {
            return EvaluateCore(expression, symbols);
        }
        catch (Exception error) when (error is OverflowException or DivideByZeroException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            throw new EffectScriptException("表达式计算失败：" + error.Message, expression.Line, expression.Column, error);
        }
    }

    private static EffectScriptEvaluationValue EvaluateCore(EffectScriptExpression expression, IReadOnlyDictionary<string, EffectScriptEvaluationValue> symbols)
    {
        if (expression.Kind == EffectScriptExpressionKind.LITERAL)
        {
            return expression.ExactNumber is { } number ? new(EffectScriptParameterKind.NUMBER) { Number = number } : EffectScriptEvaluationValue.From(expression.Literal!);
        }
        var operation = expression.Text;
        if (expression.Kind == EffectScriptExpressionKind.NAME)
        {
            if (symbols.TryGetValue(operation, out var value))
            {
                return value;
            }
            var dot = operation.LastIndexOf('.');
            if (dot > 0 && symbols.TryGetValue(operation[..dot], out value) && value.Animation is { } animation)
            {
                var component = operation[(dot + 1)..] switch { "x" or "r" => 0, "y" or "g" => 1, "z" or "b" => 2, "a" => 3, _ => -1 };
                return new(EffectScriptParameterKind.NUMBER) { Number = EffectScriptRational.Parse(animation.GetComponent(component).ToString("R", System.Globalization.CultureInfo.InvariantCulture)) };
            }
            throw Error(expression, $"未知变量：{operation}。");
        }
        var args = expression.Arguments;
        var first = Evaluate(args[0], symbols);
        if (expression.Kind == EffectScriptExpressionKind.UNARY)
        {
            return operation == "not" ? new(first.Kind) { Boolean = !first.Boolean } :
                first with { Number = operation == "-" ? EffectScriptRational.Multiply(first.Number, new(-1, 1)) : first.Number };
        }
        if (expression.Kind == EffectScriptExpressionKind.CALL)
        {
            var values = args.Select(arg => Evaluate(arg, symbols).Number.Double).ToArray();
            var animation = operation switch
            {
                "vec2" => AnimationValue.FromVector(new(values[0], values[1])),
                "vec3" => AnimationValue.FromVector3(new(values[0], values[1], values[2])),
                _ => AnimationValue.FromColor(new(values[0], values[1], values[2], values[3]))
            };
            return new(operation == "vec2" ? EffectScriptParameterKind.VECTOR2 : operation == "vec3" ? EffectScriptParameterKind.VECTOR3 : EffectScriptParameterKind.COLOR) { Animation = animation };
        }
        if (operation == "and" && !first.Boolean || operation == "or" && first.Boolean)
        {
            return first;
        }
        var second = Evaluate(args[1], symbols);
        if (operation is "and" or "or")
        {
            return second;
        }
        if (operation is "==" or "!=" or "<" or ">" or "<=" or ">=")
        {
            var comparison = Numeric(first.Kind) ? first.Number.Compare(second.Number) :
                first.Kind == EffectScriptParameterKind.BOOLEAN ? first.Boolean.CompareTo(second.Boolean) :
                first.Kind == EffectScriptParameterKind.STRING ? string.CompareOrdinal(first.Text, second.Text) :
                first.Animation == second.Animation ? 0 : 1;
            return new(EffectScriptParameterKind.BOOLEAN) { Boolean = operation switch
            {
                "==" => comparison == 0, "!=" => comparison != 0, "<" => comparison < 0, ">" => comparison > 0,
                "<=" => comparison <= 0, _ => comparison >= 0
            } };
        }
        if (Vector(first.Kind) || Vector(second.Kind))
        {
            var vector = first.Animation ?? second.Animation!.Value;
            for (var index = 0; index < vector.ComponentCount; index++)
            {
                var a = first.Animation?.GetComponent(index) ?? first.Number.Double;
                var b = second.Animation?.GetComponent(index) ?? second.Number.Double;
                vector = vector.WithComponent(index, operation switch { "+" => a + b, "-" => a - b, "*" => a * b, _ => a / b });
            }
            if (Enumerable.Range(0, vector.ComponentCount).Any(index => !double.IsFinite(vector.GetComponent(index))))
            {
                throw Error(expression, "向量表达式结果必须为有限数值。");
            }
            return new(Vector(first.Kind) ? first.Kind : second.Kind) { Animation = vector };
        }
        var result = operation switch
        {
            "+" => EffectScriptRational.Add(first.Number, second.Number),
            "-" => EffectScriptRational.Subtract(first.Number, second.Number),
            "*" => EffectScriptRational.Multiply(first.Number, second.Number),
            "/" => EffectScriptRational.Divide(first.Number, second.Number),
            _ => new(first.Number.Integer % second.Number.Integer, 1)
        };
        var kind = operation == "/" && first.Kind == second.Kind ? EffectScriptParameterKind.NUMBER :
            first.Kind == EffectScriptParameterKind.DURATION || second.Kind == EffectScriptParameterKind.DURATION ? EffectScriptParameterKind.DURATION : EffectScriptParameterKind.NUMBER;
        return new(kind) { Number = result };
    }

    internal static bool Numeric(EffectScriptParameterKind kind) => kind is EffectScriptParameterKind.NUMBER or EffectScriptParameterKind.DURATION;
    private static bool Vector(EffectScriptParameterKind kind) => kind is EffectScriptParameterKind.VECTOR2 or EffectScriptParameterKind.VECTOR3 or EffectScriptParameterKind.COLOR;
    private static EffectScriptException Error(EffectScriptExpression expression, string message) => new(message, expression.Line, expression.Column);
}
