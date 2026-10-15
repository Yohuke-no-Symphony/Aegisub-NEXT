using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal static class EffectValueEvaluation
{
    internal static AnimationValue Apply(AnimationValue input, AnimationValue multiplier, AnimationValue offset,
        AnimationProperty property, Guid instanceId, int line, int column)
    {
        if (input.Kind != multiplier.Kind || input.Kind != offset.Kind)
        {
            throw new EffectScriptException($"特效实例 {instanceId} 的 {property} 值维度不一致。", line, column);
        }
        var result = input;
        for (var component = 0; component < input.ComponentCount; component++)
        {
            var value = input.GetComponent(component) * multiplier.GetComponent(component) + offset.GetComponent(component);
            if (!double.IsFinite(value) || value < AnimationPropertyMetadata.GetMinimum(property, component) ||
                value > AnimationPropertyMetadata.GetMaximum(property, component))
            {
                throw new EffectScriptException($"特效实例 {instanceId} 的 {property} 求值超出属性范围。", line, column);
            }
            result = result.WithComponent(component, value);
        }
        return result;
    }
}
