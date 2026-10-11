using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>在首选轨道创建图片或形状；冲突时在其上方新增相邻轨道，保持整个动作原子化。</summary>
    public static GraphicClipCreationResult CreateGraphicClip(ProjectDocument document, ProjectLayer clip, Guid? preferredTrackId = null)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Kind is not (LayerKind.IMAGE or LayerKind.SHAPE))
        {
            throw new ArgumentException("只能创建图片或形状片段。", nameof(clip));
        }
        var target = preferredTrackId ?? document.Tracks.FirstOrDefault()?.Id;
        var trackIndex = target is { } targetId ? TrackIndex(document, targetId) : 0;
        Guid? created = null;
        var tracks = document.Tracks;
        if (target is null || document.Layers.Any(layer => layer.TrackId == target && layer.Start < clip.End && clip.Start < layer.End))
        {
            var track = new ProjectTrack { Name = GraphicTrackName(clip.Name) };
            tracks = tracks.Insert(trackIndex, track);
            target = track.Id;
            created = track.Id;
        }
        var added = clip with { TrackId = target.Value };
        var result = Verified(document with { Tracks = tracks, Layers = document.Layers.Add(added) });
        return new(result, added.Id, added.TrackId, created);
    }

    /// <summary>替换单个图片片段的素材与播放参数，保留矩形裁剪、外观、变换和时间。</summary>
    public static ProjectDocument ReplaceImageAsset(ProjectDocument document, Guid clipId, Guid assetId, ImagePlayback? playback = null)
    {
        return EditGraphicClip(document, clipId, layer => layer.Image is { } image
            ? layer with { Image = image with { AssetId = assetId, Playback = playback } }
            : throw new InvalidOperationException("所选片段不是图片。"));
    }

    /// <summary>匹配另一片段的时间；仅同步区间，图片速率和源起始相位保持不变。</summary>
    public static ProjectDocument MatchGraphicClipTiming(ProjectDocument document, Guid clipId, Guid referenceClipId)
    {
        ProjectValidator.Validate(document);
        var reference = document.Layers.FirstOrDefault(layer => layer.Id == referenceClipId)
            ?? throw new KeyNotFoundException("参考片段不存在。");
        return EditGraphicClip(document, clipId, layer => LayerAnimationTiming.Retime(layer, reference.Start, reference.End, TimelineEditMode.STRETCH));
    }

    /// <summary>将参数化图形转换为相同局部坐标的自由路径；保留片段变换和动画。</summary>
    public static ProjectDocument ConvertShapeToPath(ProjectDocument document, Guid clipId)
    {
        return EditGraphicClip(document, clipId, layer => layer.Shape is { } shape
            ? shape.Kind == ShapeKind.PATH ? layer : layer with { Shape = shape with { Kind = ShapeKind.PATH, Path = ShapeGeometry.CreatePath(shape) } }
            : throw new InvalidOperationException("所选片段不是形状。"));
    }

    /// <summary>提交完整形状几何草稿，非法轮廓或参数不改变文档。</summary>
    public static ProjectDocument UpdateShapeGeometry(ProjectDocument document, Guid clipId, LayerShape shape)
    {
        ProjectValidator.ValidateLayerShape(shape);
        return EditGraphicClip(document, clipId, layer => layer.Shape is not null ? layer with { Shape = shape }
            : throw new InvalidOperationException("所选片段不是形状。"));
    }

    /// <summary>提交图片矩形裁剪草稿，保持目标尺寸和变换。</summary>
    public static ProjectDocument SetImageCrop(ProjectDocument document, Guid clipId, NormalizedImageCrop crop)
    {
        ProjectValidator.ValidateImageCrop(crop);
        return EditGraphicClip(document, clipId, layer => layer.Image is { } image ? layer with { Image = image with { Crop = crop } }
            : throw new InvalidOperationException("所选片段不是图片。"));
    }

    /// <summary>提交图片播放草稿；静态模式使用空播放配置。</summary>
    public static ProjectDocument SetImagePlayback(ProjectDocument document, Guid clipId, ImagePlayback? playback)
    {
        return EditGraphicClip(document, clipId, layer => layer.Image is { } image ? layer with { Image = image with { Playback = playback } }
            : throw new InvalidOperationException("所选片段不是图片。"));
    }

    private static ProjectDocument EditGraphicClip(ProjectDocument document, Guid clipId, Func<ProjectLayer, ProjectLayer> edit)
    {
        ProjectValidator.Validate(document);
        var layer = document.Layers.FirstOrDefault(value => value.Id == clipId)
            ?? throw new KeyNotFoundException("片段不存在。");
        if (layer.Kind is not (LayerKind.IMAGE or LayerKind.SHAPE))
        {
            throw new InvalidOperationException("所选片段不是图片或形状。");
        }
        var changed = edit(layer);
        return changed == layer ? document : Verified(document with { Layers = document.Layers.Replace(layer, changed) });
    }

    private static string GraphicTrackName(string? name)
    {
        if (name is null)
        {
            return "Graphics";
        }
        var clean = new string(name.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "Graphics";
        }
        if (clean.Length <= 128)
        {
            return clean;
        }
        return clean[..(char.IsHighSurrogate(clean[127]) ? 127 : 128)];
    }
}
