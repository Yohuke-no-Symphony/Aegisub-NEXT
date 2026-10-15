using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

public static partial class EffectScriptCompiler
{
    /// <summary>编译一个实例为临时范围与保留上游语义的仿射通道；不修改手工轨道或工程。</summary>
    public static CompiledEffectScript CompileInstance(EffectScript script, EffectInstance instance, ProjectLayer layer,
        SubtitleLine? subtitle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(layer);
        try
        {
            return EffectScriptInstanceCompiler.Compile(script, instance, layer, subtitle, cancellationToken);
        }
        catch (Exception error) when (error is OverflowException or DivideByZeroException or InvalidOperationException or InvalidDataException)
        {
            throw new EffectScriptException("实例的精确求值超出表示范围：" + error.Message, innerException: error);
        }
    }

    private static EffectScript LegacyProjection(EffectScript script, bool tracksOnly)
    {
        if (script.Version != 3)
        {
            return script;
        }
        if (tracksOnly && script.Scopes is [{ Target.Kind: EffectScriptTargetKind.CURRENT, Unit.Kind: EffectScriptUnitKind.GROUP, State: SubtitleAnimationState.NORMAL } scope])
        {
            return new(script.Id, script.ShortClipPolicy, scope.Segments);
        }
        return script with { Version = 2, Parameters = [], Statements = [] };
    }

    internal static AnimationTrackTarget ResolveInstanceTarget(EffectScriptKeyframe frame, ProjectLayer layer,
        AnimationTrackTarget context, SubtitleLine? subtitle) => ResolveTarget(frame, layer, context, subtitle);
}
