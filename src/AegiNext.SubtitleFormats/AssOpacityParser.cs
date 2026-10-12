using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed class AssOpacityParser(MediaTime duration, Guid subtitleId)
{
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];
    private bool hasFade;
    private AssFadeDefinition? fade;

    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal void Apply(string name, string value, int sourceStart, int sourceLength)
    {
        var arguments = AssOverrideTags.Arguments(value);
        if (arguments.Length != (name == "fad" ? 2 : 7))
        {
            Report("Ass.FadeArguments", "ASS 淡化参数数量无效，已跳过该淡化并保留文字。", sourceStart, sourceLength);
            return;
        }
        if (hasFade)
        {
            Report("Ass.DuplicateFade", "ASS 同一行重复的 fad 或 fade 已忽略，采用首个淡化。", sourceStart, sourceLength);
            return;
        }
        hasFade = true;
        if (name == "fad")
        {
            if (!TryMilliseconds(arguments[0], sourceStart, sourceLength, out var fadeIn) ||
                !TryMilliseconds(arguments[1], sourceStart, sourceLength, out var fadeOut))
            {
                return;
            }
            if (fadeIn < MediaTime.Zero || fadeOut < MediaTime.Zero || duration > new MediaTime(int.MaxValue, 1000))
            {
                Report("Ass.FadeTiming", "ASS fad 的负时长或超出范围的对白时长不能按标准淡化转换，已跳过淡化。", sourceStart, sourceLength);
                return;
            }
            fade = new(0, 1, 0, MediaTime.Zero, fadeIn, duration - fadeOut, duration);
            return;
        }
        var alphas = arguments.Take(3).Select(AssFormatValues.Number).ToArray();
        if (alphas.Any(alpha => alpha < 0 || alpha > 255 || !alpha.Equals(Math.Truncate(alpha))))
        {
            Report("Ass.FadeAlpha", "ASS fade 透明度必须为 0 至 255 的整数，已跳过无法表示的淡化。", sourceStart, sourceLength);
            return;
        }
        var times = new MediaTime[4];
        for (var index = 0; index < times.Length; index++)
        {
            if (!TryMilliseconds(arguments[index + 3], sourceStart, sourceLength, out times[index]))
            {
                return;
            }
        }
        if (times[0] == new MediaTime(-1, 1000) && times[3] == new MediaTime(-1, 1000))
        {
            times[0] = MediaTime.Zero;
            times[3] = duration;
            times[2] = duration - times[2];
        }
        if (times[1] - times[0] > new MediaTime(int.MaxValue, 1000) ||
            times[3] - times[2] > new MediaTime(int.MaxValue, 1000))
        {
            Report("Ass.FadeTiming", "ASS 淡化区间超出安全的毫秒范围，已跳过淡化。", sourceStart, sourceLength);
            return;
        }
        fade = new(1 - alphas[0] / 255, 1 - alphas[1] / 255, 1 - alphas[2] / 255,
            times[0], times[1], times[2], times[3]);
    }

    internal ImmutableArray<AnimationTrack> Tracks(MediaTime contentOffset)
    {
        if (fade is null)
        {
            return [];
        }
        var boundaries = new SortedSet<MediaTime> { MediaTime.Zero, duration };
        foreach (var time in new[] { fade.FirstStart, fade.FirstEnd, fade.LastStart, fade.LastEnd })
        {
            if (time > MediaTime.Zero && time < duration)
            {
                boundaries.Add(time);
            }
        }
        var times = boundaries.ToArray();
        var discontinuous = times.Skip(1).SkipLast(1).Any(time => !fade.Evaluate(time).Equals(fade.Evaluate(time, true)));
        var track = discontinuous ? Ordered(times, contentOffset) : Keyframes(times, contentOffset);
        if (track is null)
        {
            return [];
        }
        var hasPartialOpacity = times.Zip(times.Skip(1), (start, end) => fade.Evaluate(start + (end - start) / 2))
            .Any(opacity => opacity > 0 && opacity < 1);
        if (hasPartialOpacity)
        {
            Report("Ass.OpacityComposition", "ASS 对文字、描边和阴影分别应用淡化，项目在整层合成后应用不透明度；已保留淡化包络和各通道透明度，重叠处外观可能不同。", 0, 0);
        }
        return [track];
    }

    private AnimationTrack? Keyframes(MediaTime[] times, MediaTime contentOffset)
    {
        var keys = times.Select(time => new Keyframe(time + contentOffset,
            AnimationValue.FromScalar(fade!.Evaluate(time, time == duration)))).ToImmutableArray();
        var constant = keys.All(key => key.Value == keys[0].Value);
        if (constant && keys[0].Value.Scalar.Equals(1d))
        {
            return null;
        }
        return new(AnimationProperty.OPACITY, constant ? [keys[0]] : keys);
    }

    private AnimationTrack Ordered(MediaTime[] times, MediaTime contentOffset)
    {
        var initial = fade!.Evaluate(MediaTime.Zero);
        var current = initial;
        var operations = ImmutableArray.CreateBuilder<AnimationTransformOperation>();
        for (var index = 0; index < times.Length - 1; index++)
        {
            var start = times[index];
            var end = times[index + 1];
            var target = fade.Evaluate(end, true);
            if (!target.Equals(current))
            {
                operations.Add(new(Guid.NewGuid(), start + contentOffset, end + contentOffset, AnimationValue.FromScalar(target)));
            }
            current = target;
            if (end < duration)
            {
                var after = fade.Evaluate(end);
                if (!after.Equals(current))
                {
                    operations.Add(new(Guid.NewGuid(), end + contentOffset, end + contentOffset, AnimationValue.FromScalar(after)));
                }
                current = after;
            }
        }
        return new(AnimationProperty.OPACITY, []) { InitialValue = AnimationValue.FromScalar(initial), Transforms = operations.ToImmutable() };
    }

    private bool TryMilliseconds(string value, int sourceStart, int sourceLength, out MediaTime result)
    {
        var number = AssFormatValues.Number(value);
        result = MediaTime.Zero;
        if (number < int.MinValue || number > int.MaxValue)
        {
            Report("Ass.FadeTiming", "ASS 淡化时间超出 32 位毫秒范围，已跳过淡化。", sourceStart, sourceLength);
            return false;
        }
        result = new((long)number, 1000);
        if (!number.Equals(Math.Truncate(number)))
        {
            Report("Ass.FadeTiming", "ASS 淡化时间已按整数毫秒解析，小数毫秒被舍弃。", sourceStart, sourceLength);
        }
        return true;
    }

    private void Report(string code, string message, int sourceStart, int sourceLength)
    {
        diagnostics.Add(new(code, message, sourceStart, sourceLength, subtitleId));
    }
}
