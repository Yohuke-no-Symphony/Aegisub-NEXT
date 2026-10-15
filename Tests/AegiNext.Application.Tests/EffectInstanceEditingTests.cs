using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.Application.Tests;

public sealed class EffectInstanceEditingTests
{
    [Fact]
    public void BatchAdditionStoresIndependentInstancesWithoutBakingAndCreatesOneUndo()
    {
        var document = EffectInstanceTestSources.Document(2);
        var editor = new ProjectEditor(document);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.AddEffectInstances(document.Layers.Select(layer => layer.Id).ToArray(), "参数模板", EffectInstanceTestSources.SOURCE);

        var applied = editor.Snapshot;
        var instances = applied.Layers.Select(layer => Assert.Single(layer.Effects)).ToArray();
        Assert.Equal(2, instances.Select(instance => instance.Id).Distinct().Count());
        Assert.All(instances, instance =>
        {
            Assert.Equal("参数模板", instance.Name);
            Assert.Equal(EffectInstanceTestSources.SOURCE, instance.Source);
            Assert.Equal(new MediaTime(0), instance.Start);
            Assert.Equal(new MediaTime(4), instance.End);
            Assert.Equal(0.5m, instance.Parameters["amount"].Number);
            Assert.Equal(new MediaTime(1, 10), instance.Parameters["duration"].Duration);
        });
        Assert.All(applied.Layers, layer => Assert.Empty(layer.Tracks));
        Assert.All(applied.Subtitles, subtitle => Assert.Empty(subtitle.AnimationRanges));
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(applied, editor.Snapshot);
    }

    [Fact]
    public void ParameterTimingEnableOrderAndRemovalPreserveIdentityAndNativeContent()
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        var layerId = editor.Snapshot.Layers[0].Id;
        editor.AddEffectInstances(layerId, "First", EffectInstanceTestSources.SOURCE);
        editor.AddEffectInstances(layerId, "Second", EffectInstanceTestSources.SOURCE);
        var before = editor.Snapshot;
        var first = before.Layers[0].Effects[0];
        var second = before.Layers[0].Effects[1];

        editor.UpdateEffectInstance(layerId, first.Id, EffectInstanceTestSources.Parameters(1));
        Assert.Equal(1m, editor.Snapshot.Layers[0].Effects[0].Parameters["amount"].Number);
        editor.SetEffectInstanceTiming(layerId, first.Id, new(1), new(3));
        editor.SetEffectInstanceEnabled(layerId, first.Id, false);
        editor.MoveEffectInstance(layerId, first.Id, 1);

        var reordered = editor.Snapshot;
        Assert.Equal(new[] { second.Id, first.Id }, reordered.Layers[0].Effects.Select(instance => instance.Id));
        var changed = reordered.Layers[0].Effects[1];
        Assert.False(changed.IsEnabled);
        Assert.Equal(new MediaTime(1), changed.Start);
        Assert.Equal(new MediaTime(3), changed.End);
        Assert.Equal(first.Source, changed.Source);
        editor.RemoveEffectInstance(layerId, first.Id);
        Assert.Equal(second.Id, Assert.Single(editor.Snapshot.Layers[0].Effects).Id);
        Assert.True(editor.Undo());
        Assert.Same(reordered, editor.Snapshot);
        Assert.Empty(editor.Snapshot.Layers[0].Tracks);
        Assert.Same(before.Subtitles[0], editor.Snapshot.Subtitles[0]);
    }

    [Fact]
    public void EquivalentParametersAndStateAreNoOpsAndPreserveRedo()
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        var layerId = editor.Snapshot.Layers[0].Id;
        editor.AddEffectInstances(layerId, "Test", EffectInstanceTestSources.SOURCE);
        var applied = editor.Snapshot;
        var instance = applied.Layers[0].Effects[0];
        editor.SetEffectInstanceEnabled(layerId, instance.Id, false);
        Assert.True(editor.Undo());
        Assert.Same(applied, editor.Snapshot);

        editor.UpdateEffectInstance(layerId, instance.Id, EffectInstanceTestSources.Parameters());
        editor.SetEffectInstanceTiming(layerId, instance.Id, instance.Start, instance.End);
        editor.SetEffectInstanceEnabled(layerId, instance.Id, true);
        editor.MoveEffectInstance(layerId, instance.Id, 0);

        Assert.Same(applied, editor.Snapshot);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void InvalidParametersUnknownTargetsAndCancelledBatchesPreserveSnapshotAndHistory()
    {
        var document = EffectInstanceTestSources.Document(2);
        var editor = new ProjectEditor(document);
        editor.AddEffectInstances(document.Layers[0].Id, "Test", EffectInstanceTestSources.SOURCE);
        var before = editor.Snapshot;
        var instanceId = before.Layers[0].Effects[0].Id;

        Assert.Throws<EffectScriptException>(() => editor.UpdateEffectInstance(document.Layers[0].Id, instanceId,
            EffectInstanceTestSources.Parameters(0.75m)));
        Assert.Throws<KeyNotFoundException>(() => editor.RemoveEffectInstances(
            [new(document.Layers[0].Id, instanceId), new(document.Layers[1].Id, Guid.NewGuid())]));
        Assert.Throws<KeyNotFoundException>(() => editor.AddEffectInstances([document.Layers[0].Id, Guid.NewGuid()],
            "Test", EffectInstanceTestSources.SOURCE));
        Assert.Throws<OperationCanceledException>(() => ProjectEditingOperations.AddEffectInstances(before,
            document.Layers.Select(layer => layer.Id).ToArray(), "Test", EffectInstanceTestSources.SOURCE,
            cancellationToken: new(true)));

        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ShortClipFailureInLaterBatchTargetIsAtomic()
    {
        var document = EffectInstanceTestSources.Document(2);
        document = document with
        {
            Subtitles = document.Subtitles.SetItem(1, document.Subtitles[1] with { End = new(501, 100) }),
            Layers = document.Layers.SetItem(1, document.Layers[1] with { End = new(501, 100) })
        };
        var editor = new ProjectEditor(document);

        Assert.Throws<EffectScriptException>(() => editor.AddEffectInstances(document.Layers.Select(layer => layer.Id).ToArray(),
            "Reject", EffectInstanceTestSources.SOURCE.Replace("short-clip compress", "short-clip reject", StringComparison.Ordinal)));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void DefaultInstanceUsesTheSignedContentClockAndCanApplyToCurrentManualRange()
    {
        var document = EffectInstanceTestSources.Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        document = document with
        {
            Layers = [document.Layers[0] with { AnimationOffset = new(-2) }],
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }]
        };
        var editor = new ProjectEditor(document);
        editor.AddEffectInstances(document.Layers[0].Id, "Grouped", EffectInstanceTestSources.GROUPED,
            targetContext: new(AnimationProperty.SCALE, TextRangeId: range.Id));

        var instance = Assert.Single(editor.Snapshot.Layers[0].Effects);
        Assert.Equal(new MediaTime(-2), instance.Start);
        Assert.Equal(new MediaTime(2), instance.End);
        Assert.Equal(range.Id, instance.TargetContext?.TextRangeId);
        Assert.Equal(range, Assert.Single(editor.Snapshot.Subtitles[0].AnimationRanges));
    }

    [Fact]
    public void AggregateGeneratedRangeBudgetRejectsTheAddedInstanceBeforeHistoryChanges()
    {
        var document = EffectInstanceTestSources.Document();
        document = document with { Subtitles = [document.Subtitles[0] with { Text = new string('A', 129) }] };
        var editor = new ProjectEditor(document);
        var layerId = document.Layers[0].Id;
        editor.AddEffectInstances(layerId, "First", EffectInstanceTestSources.GROUPED);
        var before = editor.Snapshot;

        Assert.Throws<EffectScriptException>(() => editor.AddEffectInstances(layerId, "Second", EffectInstanceTestSources.GROUPED));

        Assert.Same(before, editor.Snapshot);
        Assert.Single(editor.Snapshot.Layers[0].Effects);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void AdvancedAssTextEditingKeepsInstanceSnapshotsAndDoesNotWriteDerivedRangesBack()
    {
        var editor = new ProjectEditor(EffectInstanceTestSources.Document());
        var layerId = editor.Snapshot.Layers[0].Id;
        editor.AddEffectInstances(layerId, "Grouped", EffectInstanceTestSources.GROUPED);
        var before = editor.Snapshot;
        var layer = before.Layers[0];
        var line = before.Subtitles[0];
        var source = AssTextProjection.Create(line, layer: layer).Source;
        var edited = AssTextProjection.Apply(line, source.Replace("ABCD", "A😀CD", StringComparison.Ordinal), layer: layer);

        editor.ApplyAssTextEdit(line.Id, edited);

        Assert.Equal("A😀CD", editor.Snapshot.Subtitles[0].Text);
        Assert.Equal(layer.Effects, editor.Snapshot.Layers[0].Effects);
        Assert.Empty(editor.Snapshot.Layers[0].Tracks);
        Assert.Empty(editor.Snapshot.Subtitles[0].AnimationRanges);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }
}
