using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Testing.SubtitleFormats;

internal static class SubtitleFormatTestDocument
{
    internal static ProjectDocument Create(params SubtitleLine[] lines)
    {
        return new()
        {
            Subtitles = lines.ToImmutableArray(),
            Layers = lines.Select(line => new ProjectLayer { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }).ToImmutableArray()
        };
    }
}
