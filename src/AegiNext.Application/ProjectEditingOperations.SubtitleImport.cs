using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>将 ASS 字幕、原生图形、蒙版及动画作为完整批次原子导入。</summary>
    public static ProjectDocument ImportSubtitleLines(ProjectDocument document, AssImportResult imported, string name)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ProjectValidator.Validate(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProjectValidator.ValidateText(name);
        if (imported.Lines.IsDefault || imported.Clips.IsDefault || imported.Layers.IsDefault)
        {
            throw new InvalidDataException("Imported subtitle collections must be initialized.");
        }
        if (imported.Lines.Cast<SubtitleLine?>().Any(line => line is null) ||
            imported.Lines.Select(line => line.Id).Distinct().Count() != imported.Lines.Length)
        {
            throw new InvalidDataException("Imported subtitle identities must be nonnull and unique.");
        }
        if (imported.Clips.Cast<SubtitleClipImport?>().Any(clip => clip is null))
        {
            throw new InvalidDataException("An imported ASS clip cannot be null.");
        }
        if (!imported.Clips.IsEmpty && (imported.Clips.Length != imported.Lines.Length ||
            !imported.Clips.Select(clip => clip.Line).SequenceEqual(imported.Lines)))
        {
            throw new InvalidDataException("The imported ASS clips do not match the subtitle identities.");
        }
        var sourceLayers = imported.Layers;
        if (sourceLayers.IsEmpty)
        {
            sourceLayers = imported.Lines.Select((line, index) =>
            {
                var clip = imported.Clips.IsEmpty ? null : imported.Clips[index];
                return new ProjectLayer
                {
                    Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                    Start = line.Start, End = line.End,
                    Mask = clip?.Mask, Tracks = clip?.Tracks ?? [],
                    AnimationOffset = clip?.ContentOffset ?? default, Transform = clip?.Transform ?? new()
                };
            }).ToImmutableArray();
        }
        ValidateImportedLayers(imported, sourceLayers);
        if (sourceLayers.IsEmpty)
        {
            return document;
        }
        if (document.Subtitles.Length + imported.Lines.Length > 100000 ||
            document.Layers.Length + sourceLayers.Length > 10000 || name.Length > 1024)
        {
            throw new InvalidDataException("The subtitle import count, layer count or track name exceeds the budget.");
        }
        var slots = new SubtitleImportTrackAllocator(sourceLayers).Allocate(sourceLayers);
        var trackCount = slots.Max() + 1;
        if (document.Tracks.Length + trackCount > 10000)
        {
            throw new InvalidDataException("The imported track count exceeds the budget.");
        }
        var tracks = Enumerable.Range(0, trackCount).Select(slot => new ProjectTrack
        {
            Name = slot == trackCount - 1 ? name : $"{name} ({trackCount - slot})",
            AutoApplyStyle = false
        }).ToArray();
        return Verified(document with
        {
            Tracks = document.Tracks.InsertRange(0, tracks.Reverse()),
            Subtitles = document.Subtitles.AddRange(imported.Lines),
            Layers = document.Layers.AddRange(sourceLayers.Select((layer, index) => layer with { TrackId = tracks[slots[index]].Id }))
        });
    }

    /// <summary>一次准备整批字幕的新独立轨道；保留样式与来源叠覆顺序，将重叠区间分到独立轨道。</summary>
    public static ProjectDocument ImportSubtitleLines(ProjectDocument document, IEnumerable<SubtitleLine> lines, string name)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var imported = lines.Cast<SubtitleLine?>().Take(100001)
            .Select(line => line ?? throw new InvalidDataException("An imported subtitle cannot be null.")).ToImmutableArray();
        return ImportSubtitleLines(document, new AssImportResult(imported, []), name);
    }

    private static void ValidateImportedLayers(AssImportResult imported, ImmutableArray<ProjectLayer> layers)
    {
        var lines = imported.Lines.ToDictionary(line => line.Id);
        var clips = imported.Clips.ToDictionary(clip => clip.Line.Id);
        var referenced = new HashSet<Guid>();
        foreach (var layer in layers)
        {
            if (layer is null || layer.Kind is not (LayerKind.SUBTITLE or LayerKind.SHAPE))
            {
                throw new InvalidDataException("Subtitle import only supports native subtitle and shape layers.");
            }
            if (layer.Kind == LayerKind.SHAPE)
            {
                if (layer.SubtitleId.HasValue)
                {
                    throw new InvalidDataException("An imported shape cannot reference a subtitle.");
                }
                continue;
            }
            if (layer.SubtitleId is not { } id || !lines.TryGetValue(id, out var line) ||
                !referenced.Add(id) || layer.Start != line.Start || layer.End != line.End)
            {
                throw new InvalidDataException("The imported layers do not match the subtitle identities and intervals.");
            }
            if (clips.TryGetValue(id, out var clip) && (layer.Mask != clip.Mask || !layer.Tracks.SequenceEqual(clip.Tracks) ||
                layer.Transform != clip.Transform || layer.AnimationOffset != clip.ContentOffset))
            {
                throw new InvalidDataException("The imported native layers disagree with their legacy subtitle clips.");
            }
        }
        if (referenced.Count != lines.Count)
        {
            throw new InvalidDataException("Every imported subtitle must have exactly one native layer.");
        }
    }
}
