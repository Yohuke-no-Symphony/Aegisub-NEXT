using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>从脚本声明判断预设是否依赖字幕内容；具体时长、基础值和几何仍由编译器校验。</summary>
public static class EffectScriptCompatibility
{
    /// <summary>字幕目标、文字分组、视觉状态和字幕专用属性仅能用于字幕片段。</summary>
    public static bool RequiresSubtitle(EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Scopes.Any(scope => scope.Target.Kind != EffectScriptTargetKind.CURRENT ||
            scope.Unit.Kind != EffectScriptUnitKind.GROUP || scope.State != SubtitleAnimationState.NORMAL))
        {
            return true;
        }

        return script.Segments.Concat(script.Scopes.SelectMany(scope => scope.Segments))
            .SelectMany(segment => segment.Keyframes)
            .Any(frame => AnimationPropertyMetadata.IsSubtitleOnlyProperty(
                EffectScriptPropertyMetadata.GetAnimationProperty(frame.Property)));
    }
}
