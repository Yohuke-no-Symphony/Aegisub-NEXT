using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>将 ASS 字幕、蒙版和动画原子导入同一批字幕片段。</summary>
    public static ProjectDocument ImportSubtitleLines(ProjectDocument document, AssImportResult imported, string name)
    {
        ArgumentNullException.ThrowIfNull(imported);
        var result = ImportSubtitleLines(document, imported.Lines, name);
        if (imported.Clips.IsEmpty)
        {
            return result;
        }
        if (imported.Clips.Length != imported.Lines.Length || !imported.Clips.Select(clip => clip.Line).SequenceEqual(imported.Lines))
        {
            throw new InvalidDataException("The imported ASS clips do not match the subtitle identities.");
        }
        var clips = imported.Clips.ToDictionary(clip => clip.Line.Id);
        return Verified(result with
        {
            Layers = result.Layers.Select(layer => layer.SubtitleId is { } id && clips.TryGetValue(id, out var clip)
                ? layer with { Mask = clip.Mask, Tracks = clip.Tracks, AnimationOffset = clip.ContentOffset, Transform = clip.Transform } : layer).ToImmutableArray()
        });
    }

    /// <summary>一次准备整批字幕的新独立轨道；保留样式与来源叠覆顺序，将重叠区间分到独立轨道。</summary>
    public static ProjectDocument ImportSubtitleLines(ProjectDocument document, IEnumerable<SubtitleLine> lines, string name)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ProjectValidator.ValidateText(name);
        var imported = lines.Cast<SubtitleLine?>().Take(100001)
            .Select(line => line ?? throw new InvalidDataException("An imported subtitle cannot be null.")).ToArray();
        if (imported.Length == 0)
        {
            return document;
        }
        if (imported.Length > 100000 || document.Subtitles.Length + imported.Length > 100000 || name.Length > 1024)
        {
            throw new InvalidDataException("The subtitle import count or track name exceeds the budget.");
        }
        var slots = new SubtitleImportTrackAllocator(imported).Allocate(imported);
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
            Subtitles = document.Subtitles.AddRange(imported),
            Layers = document.Layers.AddRange(imported.Select((line, index) => new ProjectLayer
            {
                Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                TrackId = tracks[slots[index]].Id,
                Start = line.Start, End = line.End
            }))
        });
    }
}
