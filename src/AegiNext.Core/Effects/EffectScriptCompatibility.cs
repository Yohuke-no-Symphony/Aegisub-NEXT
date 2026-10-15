using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>从脚本声明判断预设是否依赖字幕内容；具体时长、基础值和几何仍由编译器校验。</summary>
public static class EffectScriptCompatibility
{
    /// <summary>字幕目标、文字分组、视觉状态和字幕专用属性仅能用于字幕片段。</summary>
    public static bool RequiresSubtitle(EffectScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Version == 3)
        {
            return script.Statements.Any(RequiresSubtitle);
        }
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

    private static bool RequiresSubtitle(EffectScriptStatement statement)
    {
        if (statement.Kind == EffectScriptStatementKind.SCOPE)
        {
            if (statement.Expressions[0].Literal?.Text != "current")
            {
                return true;
            }
            foreach (var field in statement.Statements.Where(child => child.Kind == EffectScriptStatementKind.FIELD))
            {
                if (field.Name == "unit" && field.Expressions[0].Literal?.Text != "group" ||
                    field.Name == "state" && field.Expressions[0].Literal?.Text != "normal")
                {
                    return true;
                }
            }
        }
        if (statement.Kind == EffectScriptStatementKind.KEYFRAME &&
            AnimationPropertyMetadata.IsSubtitleOnlyProperty(EffectScriptPropertyMetadata.GetAnimationProperty(
                EffectScriptParser.ParseProperty(statement.Text, statement.Line, statement.Column).Property)))
        {
            return true;
        }
        return statement.Statements.Any(RequiresSubtitle) || statement.Alternatives.Any(RequiresSubtitle);
    }
}
