using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Ui.Tests;

internal static class TimelineEffectClipTestState
{
    internal static TimelineViewState ExpandAll(ProjectDocument document)
    {
        return document.TimelineViewState with
        {
            ExpandedAnimationClips = document.Layers.SelectMany(layer => layer.Tracks.Select(track =>
                new TimelineEffectClipId(layer.Id, track.Target))).ToImmutableArray()
        };
    }
}
