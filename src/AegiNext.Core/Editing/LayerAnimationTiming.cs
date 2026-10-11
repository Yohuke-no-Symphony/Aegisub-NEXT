using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>裁剪保留内容时钟及曲线相位，有序变换保留操作时间；拉伸同步缩放所有动画时间。</summary>
public static class LayerAnimationTiming
{
    /// <summary>获取片段完整的有符号内容时间范围，右端可保存结束关键帧。</summary>
    public static (MediaTime Minimum, MediaTime Maximum) GetRange(ProjectLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return (layer.AnimationOffset, layer.AnimationOffset + layer.End - layer.Start);
    }

    /// <summary>将一次关键帧编辑限制在当前片段内。</summary>
    public static MediaTime ClampTime(ProjectLayer layer, MediaTime time)
    {
        var (minimum, maximum) = GetRange(layer);
        if (maximum < minimum)
        {
            throw new InvalidOperationException("片段中没有可编辑的动画时间。");
        }

        return time < minimum ? minimum : time > maximum ? maximum : time;
    }

    /// <summary>按片段编辑语义裁剪或精确拉伸动画时间，路径内容时长同步拉伸。</summary>
    public static ProjectLayer Retime(ProjectLayer layer, MediaTime start, MediaTime end, TimelineEditMode mode)
    {
        if (start >= end || !Enum.IsDefined(mode))
        {
            throw new ArgumentException("图层时间或编辑模式无效。", nameof(end));
        }

        if (layer.Start == start && layer.End == end)
        {
            return layer;
        }

        var oldDuration = layer.End - layer.Start;
        var newDuration = end - start;
        return Clip(layer with
        {
            Start = start,
            End = end,
            AnimationOffset = mode == TimelineEditMode.CROP ? layer.AnimationOffset + start - layer.Start :
                Scale(layer.AnimationOffset, newDuration, oldDuration),
            Image = mode == TimelineEditMode.CROP && layer.Image is { Playback: { } playback } image
                ? image with { Playback = ImagePlaybackTiming.Crop(playback, start - layer.Start) } : layer.Image,
            Tracks = mode == TimelineEditMode.CROP ? layer.Tracks : layer.Tracks.Select(track => track with
            {
                Keyframes = track.Keyframes.Select(frame => frame with { Time = Scale(frame.Time, newDuration, oldDuration) }).ToImmutableArray(),
                Transforms = track.Transforms.Select(operation => operation with
                {
                    Start = Scale(operation.Start, newDuration, oldDuration),
                    End = Scale(operation.End, newDuration, oldDuration)
                }).ToImmutableArray()
            }).ToImmutableArray(),
            MotionPath = mode == TimelineEditMode.STRETCH && layer.MotionPath is { } path
                ? path with { Duration = Scale(path.Duration, newDuration, oldDuration) }
                : layer.MotionPath
        });
    }

    /// <summary>裁掉片段外的轨道关键帧，并为被截断的区间保留精确边值和曲线参数。</summary>
    public static ProjectLayer Clip(ProjectLayer layer)
    {
        var (minimum, maximum) = GetRange(layer);
        if (layer.Tracks.IsEmpty)
        {
            return layer;
        }

        var tracks = maximum < minimum ? layer.Tracks.Where(track => track.IsOrdered).ToImmutableArray() :
            layer.Tracks.Select(track => AnimationTrackSlicer.Clip(track, minimum, maximum)).ToImmutableArray();
        return tracks.SequenceEqual(layer.Tracks) ? layer : layer with { Tracks = tracks };
    }

    /// <summary>规范化旧工程的所有图层，保留工程与未改动层的引用。</summary>
    public static ProjectDocument Normalize(ProjectDocument document)
    {
        var layers = NormalizeLayers(document.Layers);
        return layers == document.Layers ? document : document with { Layers = layers };
    }

    private static ImmutableArray<ProjectLayer> NormalizeLayers(ImmutableArray<ProjectLayer> layers)
    {
        ImmutableArray<ProjectLayer>.Builder? changed = null;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var next = Clip(layer);
            if (next != layer)
            {
                changed ??= layers.ToBuilder();
                changed[index] = next;
            }
        }

        return changed?.ToImmutable() ?? layers;
    }

    private static MediaTime Scale(MediaTime time, MediaTime newDuration, MediaTime oldDuration)
    {
        var numerator = (BigInteger)time.Numerator * newDuration.Numerator * oldDuration.Denominator;
        var denominator = (BigInteger)time.Denominator * newDuration.Denominator * oldDuration.Numerator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }
}
