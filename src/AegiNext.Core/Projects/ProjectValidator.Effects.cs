using AegiNext.Core.Effects;

namespace AegiNext.Core.Projects;

public static partial class ProjectValidator
{
    private static void Effects(ProjectLayer layer, SubtitleLine? subtitle, HashSet<Guid> identities)
    {
        Require(!layer.Effects.IsDefault && layer.Effects.Length <= 128, "片段特效实例集合无效或超过 128 个。");
        var (minimum, maximum) = Editing.LayerAnimationTiming.GetRange(layer);
        foreach (var instance in layer.Effects)
        {
            NotNull(instance, "特效实例不能为 null。");
            Require(instance.Id != Guid.Empty && identities.Add(instance.Id) &&
                identities.Count <= 10000, "特效实例标识为空、重复或总数超过 10,000 个。");
            Require(!string.IsNullOrWhiteSpace(instance.Name) && instance.Name.Length <= 128 &&
                !instance.Name.Any(char.IsControl), "特效实例名称必须为不超过 128 字符的有效文字。");
            ValidateText(instance.Name);
            NotNull(instance.Source, "特效实例缺少脚本。");
            NotNull(instance.Parameters, "特效实例缺少参数。");
            ValidateText(instance.Source);
            Require(instance.Start < instance.End && instance.Start >= minimum && instance.End <= maximum,
                "特效实例时段必须位于片段内容时间范围内。");
            if (instance.TargetContext is { } context)
            {
                SubtitleAnimationTargetValidation.ValidateIdentity(context);
                Require(!context.NodeId.HasValue, "特效实例上下文不能保存蒙版节点目标。");
                Require(context.TextRangeId is null && context.State == SubtitleAnimationState.NORMAL || subtitle is not null,
                    "文字范围和视觉状态特效上下文需要字幕片段。");
                Require(context.TextRangeId is null || subtitle!.AnimationRanges.Any(range => range.Id == context.TextRangeId),
                    "特效实例引用的文字范围不存在。");
            }
            try
            {
                var script = EffectScriptParser.Parse(instance.Source);
                _ = EffectScriptParameterBinder.Bind(script, instance.Parameters);
            }
            catch (EffectScriptException error)
            {
                throw new InvalidDataException("特效实例脚本或参数无效。", error);
            }
        }
    }
}
