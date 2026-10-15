using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptEvaluationValue(EffectScriptParameterKind Kind)
{
    internal EffectScriptRational Number { get; init; } = new(0, 1);
    internal bool Boolean { get; init; }
    internal string? Text { get; init; }
    internal AnimationValue? Animation { get; init; }

    internal static EffectScriptEvaluationValue From(EffectScriptParameterValue value) => new(value.Kind)
    {
        Number = value.Kind == EffectScriptParameterKind.DURATION ? EffectScriptRational.From(value.Duration) : EffectScriptRational.From(value.Number),
        Boolean = value.Boolean,
        Text = value.Text,
        Animation = value.Animation
    };

    internal EffectScriptParameterValue Parameter => new(Kind)
    {
        Number = Kind == EffectScriptParameterKind.NUMBER ? Number.Decimal : 0,
        Duration = Kind == EffectScriptParameterKind.DURATION ? Number.Time : default,
        Boolean = Boolean,
        Text = Text,
        Animation = Animation
    };
}
