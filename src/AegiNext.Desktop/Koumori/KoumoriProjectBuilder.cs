using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Koumori;

internal static class KoumoriProjectBuilder
{
    internal static ProjectDocument Add(ProjectDocument source, IReadOnlyList<MediaTimeRange> intervals, string trackPrefix)
    {
        var groups = new List<List<SubtitleLine>>();
        foreach (var interval in intervals.OrderBy(value => value.Start).ThenBy(value => value.End))
        {
            if (interval.Start < MediaTime.Zero || interval.End <= interval.Start)
            {
                throw new InvalidDataException("Invalid subtitle timing interval.");
            }
            var group = groups.FirstOrDefault(value => value[^1].End <= interval.Start);
            if (group is null)
            {
                group = [];
                groups.Add(group);
            }
            group.Add(new() { Start = interval.Start, End = interval.End });
        }
        var result = source;
        var names = source.Tracks.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var number = 1;
        foreach (var group in groups)
        {
            while (names.Contains($"{trackPrefix} {number}"))
            {
                number++;
            }
            var track = new ProjectTrack { Name = $"{trackPrefix} {number++}" };
            names.Add(track.Name);
            result = ProjectEditingOperations.AddTrack(result, track);
            result = ProjectEditingOperations.CreateSubtitleClips(result, group, track.Id);
        }
        return result;
    }
}
