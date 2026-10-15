using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptBoundKey(EffectScriptRational Progress, EffectScriptProperty Property,
    AnimationProperty NativeProperty, EffectScriptNodeSelector? NodeSelector, AnimationValue Multiplier,
    AnimationValue Offset, AnimationCurve Curve, int Line, int Column);
