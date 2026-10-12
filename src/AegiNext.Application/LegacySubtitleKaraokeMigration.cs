using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal static class LegacySubtitleKaraokeMigration
{
    internal static SubtitleLine Upgrade(SubtitleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Karaoke.IsDefaultOrEmpty && line.InactiveKaraoke.IsDefaultOrEmpty)
        {
            ProjectValidator.ValidateSubtitleKaraoke(line);
            return line;
        }
        if (line.Text is null)
        {
            throw new InvalidDataException("Subtitle text cannot be null.");
        }
        var boundaries = new SubtitleTextBoundaries(line.Text);
        ProjectValidator.ValidateSubtitleKaraoke(line, boundaries);
        return NormalizeValidated(line, boundaries);
    }

    private static SubtitleLine NormalizeValidated(SubtitleLine line, SubtitleTextBoundaries? boundaries = null)
    {
        if (line.Karaoke.IsEmpty && line.InactiveKaraoke.IsEmpty)
        {
            return line;
        }

        boundaries ??= new(line.Text);
        var ids = line.Karaoke.Concat(line.InactiveKaraoke).Select(clip => clip.Id).ToHashSet();
        var karaoke = NormalizeSegments(line.Karaoke, boundaries, ids);
        var inactiveKaraoke = NormalizeSegments(line.InactiveKaraoke, boundaries, ids);
        return karaoke == line.Karaoke && inactiveKaraoke == line.InactiveKaraoke
            ? line : line with { Karaoke = karaoke, InactiveKaraoke = inactiveKaraoke };
    }

    private static ImmutableArray<KaraokeSegment> NormalizeSegments(ImmutableArray<KaraokeSegment> segments,
        SubtitleTextBoundaries boundaries, HashSet<Guid> ids)
    {
        ImmutableArray<KaraokeSegment>.Builder? changed = null;
        for (var index = 0; index < segments.Length; index++)
        {
            var clip = segments[index];
            var first = boundaries.IndexOf(clip.Utf16Start);
            var count = boundaries.IndexOf(clip.Utf16Start + clip.Utf16Length) - first;
            if (count == 1)
            {
                changed?.Add(clip);
                continue;
            }

            if (changed is null)
            {
                changed = ImmutableArray.CreateBuilder<KaraokeSegment>();
                changed.AddRange(segments.AsSpan(0, index));
            }
            var start = clip.Start;
            for (var glyph = 0; glyph < count; glyph++)
            {
                var end = KaraokeTimingMath.Interpolate(clip.Start, clip.End, glyph + 1, count);
                changed.Add(clip with
                {
                    Id = glyph == 0 ? clip.Id : NewId(ids),
                    Utf16Start = boundaries[first + glyph],
                    Utf16Length = boundaries[first + glyph + 1] - boundaries[first + glyph],
                    Start = start,
                    End = end
                });
                start = end;
            }
        }

        return changed is null ? segments : changed.ToImmutable();
    }

    internal static ProjectDocument Upgrade(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        ImmutableArray<SubtitleLine>.Builder? changed = null;
        for (var index = 0; index < document.Subtitles.Length; index++)
        {
            var line = document.Subtitles[index];
            var normalized = NormalizeValidated(line);
            if (!ReferenceEquals(line, normalized))
            {
                changed ??= document.Subtitles.ToBuilder();
                changed[index] = normalized;
            }
        }
        return changed is null ? document : document with { Subtitles = changed.ToImmutable() };
    }

    private static Guid NewId(HashSet<Guid> ids)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        }
        while (!ids.Add(id));
        return id;
    }
}
