using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class EffectInstanceLifecycleTests
{
    [Fact]
    public void CropIntersectsInstanceWindowsAndRemovesEmptyIntersectionsWithoutBaking()
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        var layerId = editor.Snapshot.Layers[0].Id;
        editor.AddEffectInstances(layerId, "Keep", EffectInstanceTestSources.SOURCE);
        editor.AddEffectInstances(layerId, "Remove", EffectInstanceTestSources.SOURCE);
        var keep = editor.Snapshot.Layers[0].Effects[0];
        var remove = editor.Snapshot.Layers[0].Effects[1];
        editor.SetEffectInstanceTiming(layerId, keep.Id, new(1), new(3));
        editor.SetEffectInstanceTiming(layerId, remove.Id, new(0), new(1));
        var before = editor.Snapshot;

        editor.SetSubtitleTiming(layerId, new(2), new(4), TimelineEditMode.CROP);

        var layer = editor.Snapshot.Layers[0];
        var remaining = Assert.Single(layer.Effects);
        Assert.Equal(keep.Id, remaining.Id);
        Assert.Equal(new MediaTime(2), layer.AnimationOffset);
        Assert.Equal(new MediaTime(2), remaining.Start);
        Assert.Equal(new MediaTime(3), remaining.End);
        Assert.Empty(layer.Tracks);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void StretchScalesContentOffsetAndInstanceWindowsWhileMovePreservesThem()
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        var layerId = editor.Snapshot.Layers[0].Id;
        editor.UpdateLayer(layerId, layer => layer with { AnimationOffset = new(-2) });
        editor.AddEffectInstances(layerId, "Test", EffectInstanceTestSources.SOURCE);
        var instance = editor.Snapshot.Layers[0].Effects[0];
        editor.SetEffectInstanceTiming(layerId, instance.Id, new(-1), new(1));

        editor.SetSubtitleTiming(layerId, new(0), new(8), TimelineEditMode.STRETCH);

        var stretched = editor.Snapshot.Layers[0];
        Assert.Equal(new MediaTime(-4), stretched.AnimationOffset);
        Assert.Equal(new MediaTime(-2), stretched.Effects[0].Start);
        Assert.Equal(new MediaTime(2), stretched.Effects[0].End);
        editor.ShiftClips([layerId], new(10));
        var moved = editor.Snapshot.Layers[0];
        Assert.Equal(new MediaTime(10), moved.Start);
        Assert.Equal(stretched.AnimationOffset, moved.AnimationOffset);
        Assert.Equal(stretched.Effects, moved.Effects);
    }

    [Fact]
    public void CopyAndSplitRemapInstanceAndTargetIdentitiesAndClipEachHalf()
    {
        var document = EffectInstanceTestSources.Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        var editor = new ProjectEditor(document);
        var layerId = document.Layers[0].Id;
        editor.AddEffectInstances(layerId, "Grouped", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: range.Id));
        var applied = editor.Snapshot;
        var originalInstance = applied.Layers[0].Effects[0];
        var clipboard = ProjectEditingOperations.CaptureClips(applied, [layerId], layerId);
        var pasted = ProjectEditingOperations.PasteClips(applied, clipboard, new(10));
        var copy = pasted.Document.Layers.Single(layer => layer.Id == pasted.PrimaryId);
        var copySubtitle = pasted.Document.Subtitles.Single(line => line.Id == copy.SubtitleId);
        Assert.NotEqual(originalInstance.Id, Assert.Single(copy.Effects).Id);
        Assert.Equal(Assert.Single(copySubtitle.AnimationRanges).Id, copy.Effects[0].TargetContext?.TextRangeId);
        Assert.Equal(originalInstance.Start, copy.Effects[0].Start);
        Assert.Equal(originalInstance.End, copy.Effects[0].End);

        var split = ProjectEditingOperations.SplitSubtitle(applied, document.Subtitles[0].Id, new(2), 2);
        var left = split.Layers[0];
        var right = split.Layers[1];
        Assert.NotEqual(originalInstance.Id, Assert.Single(left.Effects).Id);
        Assert.NotEqual(originalInstance.Id, Assert.Single(right.Effects).Id);
        Assert.NotEqual(left.Effects[0].Id, right.Effects[0].Id);
        Assert.Equal(new MediaTime(0), left.Effects[0].Start);
        Assert.Equal(new MediaTime(2), left.Effects[0].End);
        Assert.Equal(new MediaTime(2), right.Effects[0].Start);
        Assert.Equal(new MediaTime(4), right.Effects[0].End);
        Assert.Equal(split.Subtitles[1].AnimationRanges[0].Id, right.Effects[0].TargetContext?.TextRangeId);
        Assert.Empty(left.Tracks);
        Assert.Empty(right.Tracks);
    }

    [Fact]
    public void MergeRebasesBothInstancesToTheNewContentClockAndRemapsSecondRange()
    {
        var document = EffectInstanceTestSources.Document(2);
        var firstRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        var secondRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        document = document with
        {
            Subtitles =
            [
                document.Subtitles[0] with { Start = new(10), End = new(12), AnimationRanges = [firstRange] },
                document.Subtitles[1] with { Start = new(15), End = new(17), AnimationRanges = [secondRange] }
            ],
            Layers =
            [
                document.Layers[0] with { Start = new(10), End = new(12), AnimationOffset = new(2) },
                document.Layers[1] with { Start = new(15), End = new(17), AnimationOffset = new(-3) }
            ]
        };
        var editor = new ProjectEditor(document);
        editor.AddEffectInstances(document.Layers[0].Id, "First", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: firstRange.Id));
        editor.AddEffectInstances(document.Layers[1].Id, "Second", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: secondRange.Id));
        var before = editor.Snapshot;

        var merged = ProjectEditingOperations.MergeSubtitles(before, document.Subtitles[0].Id, document.Subtitles[1].Id);

        var layer = Assert.Single(merged.Layers);
        var subtitle = Assert.Single(merged.Subtitles);
        Assert.Equal(new MediaTime(2), layer.AnimationOffset);
        Assert.Collection(layer.Effects, instance => Assert.Equal("First", instance.Name),
            instance => Assert.Equal("Second", instance.Name));
        Assert.Equal(new MediaTime(2), layer.Effects[0].Start);
        Assert.Equal(new MediaTime(4), layer.Effects[0].End);
        Assert.Equal(new MediaTime(7), layer.Effects[1].Start);
        Assert.Equal(new MediaTime(9), layer.Effects[1].End);
        Assert.Equal(subtitle.AnimationRanges[1].Id, layer.Effects[1].TargetContext?.TextRangeId);
        Assert.NotEqual(secondRange.Id, layer.Effects[1].TargetContext?.TextRangeId);
        Assert.Empty(layer.Tracks);
    }

    [Fact]
    public void DeletingTheCurrentTextRangeRemovesItsInstancesInTheSameUndo()
    {
        var document = EffectInstanceTestSources.Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        var editor = new ProjectEditor(document);
        editor.AddEffectInstances(document.Layers[0].Id, "Grouped", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: range.Id));
        var applied = editor.Snapshot;

        editor.RemoveSubtitleAnimationRange(document.Subtitles[0].Id, range.Id);

        Assert.Empty(editor.Snapshot.Layers[0].Effects);
        Assert.True(editor.Undo());
        Assert.Same(applied, editor.Snapshot);
    }

    [Fact]
    public void ProjectMergeCreatesIndependentInstanceIdsAndRemapsRangeContexts()
    {
        var document = EffectInstanceTestSources.Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 4);
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        var editor = new ProjectEditor(document);
        editor.AddEffectInstances(document.Layers[0].Id, "Grouped", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: range.Id));
        var original = editor.Snapshot;

        var result = ProjectEditingOperations.MergeProjects(new(), [new(original, "Imported", Path.GetTempPath())]);

        var copied = Assert.Single(result.Document.Layers);
        var subtitle = Assert.Single(result.Document.Subtitles);
        var instance = Assert.Single(copied.Effects);
        Assert.NotEqual(original.Layers[0].Effects[0].Id, instance.Id);
        Assert.Equal(subtitle.AnimationRanges[0].Id, instance.TargetContext?.TextRangeId);
        Assert.NotEqual(range.Id, instance.TargetContext?.TextRangeId);
        Assert.Equal(original.Layers[0].Effects[0].Source, instance.Source);
    }
}
