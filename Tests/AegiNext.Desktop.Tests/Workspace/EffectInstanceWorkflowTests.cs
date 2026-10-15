using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class EffectInstanceWorkflowTests
{
    private const string SOURCE = """
        effect "instance-workflow" version 3
        short-clip compress
        var amount = 0.5 suggest ["Full": 1]
        scope clip current
            segment stay flex 1
                at 0 opacity factor(amount)
                at 1 opacity factor(amount)
            end
        end
        """;

    [Fact]
    public async Task AddingTwiceCreatesIndependentSnapshotsAndDoesNotRewriteManualAnimation()
    {
        var source = Document();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(source.Layers[0].Id, [source.Layers[0].Id]);
        var parameters = ImmutableDictionary<string, EffectScriptParameterValue>.Empty.Add("amount", EffectScriptParameterValue.FromNumber(0.75m));
        await context.Session.EffectScripts.ApplyAsync(SOURCE, "Independent", parameters);
        var first = context.Editor.Snapshot;
        await context.Session.EffectScripts.ApplyAsync(SOURCE, "Independent");
        var layer = context.Editor.Snapshot.Layers[0];
        Assert.Same(source.Layers[0].Tracks[0], layer.Tracks[0]);
        Assert.Same(source.Subtitles[0], context.Editor.Snapshot.Subtitles[0]);
        Assert.Equal(2, layer.Effects.Length);
        Assert.NotEqual(layer.Effects[0].Id, layer.Effects[1].Id);
        Assert.All(layer.Effects, instance => Assert.Equal(SOURCE, instance.Source));
        Assert.Equal(0.75m, layer.Effects[0].Parameters["amount"].Number);
        Assert.Equal(0.5m, layer.Effects[1].Parameters["amount"].Number);
        Assert.True(context.Editor.Undo());
        Assert.Same(first, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }

    [Fact]
    public async Task InspectorDraftParticipatesInAtomicValidationAndInvalidInputBlocksSelection()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var layerId = document.Layers[0].Id;
        session.SelectLayer(layerId, [layerId]);
        await session.EffectScripts.ApplyAsync(SOURCE);
        var added = context.Editor.Snapshot;
        var instance = added.Layers[0].Effects[0];
        Assert.True(session.ViewModel.Timeline.SelectEffectClip(new(new(layerId, new(AnimationProperty.OPACITY), instance.Id))));
        var drafts = session.ViewModel.Effects.InstanceParameters!;
        drafts.Rows[0].RawText = "unfinished";
        Assert.True(session.HasProjectDrafts);
        Assert.False(session.TryCommitDrafts(false));
        Assert.Same(added, context.Editor.Snapshot);
        session.SelectLayer(layerId, [layerId]);
        Assert.Equal(instance.Id, session.ViewModel.Effects.SelectedEffectInstance!.Id);
        Assert.Equal("unfinished", drafts.Rows[0].RawText);
        drafts.Rows[0].RawText = "0.25";
        Assert.True(session.TryCommitDrafts(false));
        Assert.False(session.HasProjectDrafts);
        Assert.Equal(0.25m, context.Editor.Snapshot.Layers[0].Effects[0].Parameters["amount"].Number);
        Assert.Same(document.Layers[0].Tracks[0], context.Editor.Snapshot.Layers[0].Tracks[0]);
        Assert.True(context.Editor.Undo());
        Assert.Same(added, context.Editor.Snapshot);
        Assert.Equal(0.5m, session.ViewModel.Effects.SelectedEffectInstance!.Parameters["amount"].Number);
    }

    [Fact]
    public async Task InstanceTimeGestureRejectsStaleDocumentAndDeleteCannotFallThroughToTheParent()
    {
        var document = Document();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var layerId = document.Layers[0].Id;
        session.SelectLayer(layerId, [layerId]);
        await session.EffectScripts.ApplyAsync(SOURCE);
        var added = context.Editor.Snapshot;
        var id = new TimelineEffectClipId(layerId, new(AnimationProperty.OPACITY), added.Layers[0].Effects[0].Id);
        Assert.True(session.ViewModel.Timeline.SelectEffectClip(new(id)));
        await session.ViewModel.Timeline.CommitEffectInstanceTimingAsync(new(id, new(1), new(3), added));
        var moved = context.Editor.Snapshot;
        Assert.Equal(new MediaTime(1), moved.Layers[0].Effects[0].Start);
        await session.ViewModel.Timeline.CommitEffectInstanceTimingAsync(new(id, new(2), new(4), added));
        Assert.Same(moved, context.Editor.Snapshot);
        Assert.True(session.ViewModel.Timeline.SelectEffectClip(new(id)));
        await session.ViewModel.Timeline.DeleteTimelineSelectionAsync();
        var deleted = context.Editor.Snapshot;
        Assert.Empty(deleted.Layers[0].Effects);
        Assert.Same(document.Layers[0].Tracks[0], deleted.Layers[0].Tracks[0]);
        await session.ViewModel.Timeline.DeleteTimelineSelectionAsync();
        Assert.Same(deleted, context.Editor.Snapshot);
    }

    [Fact]
    public async Task ConditionalSubtitleBranchDoesNotDisableAValidGraphicBranch()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 20), End = new(4) };
        await using var context = new WorkspaceSessionTestContext(new() { Layers = [layer] });
        await context.InitializeAsync();
        await context.Session.EffectScripts.Completion;
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        var preset = new EffectScriptPreset(Guid.NewGuid(), "Conditional", """
            effect "conditional-target" version 3
            short-clip compress
            var letters = false
            if letters
                scope letters current
                    unit grapheme
                    segment hold flex 1
                        at 0 scale base
                        at 1 scale base
                    end
                end
            else
                scope whole current
                    segment hold flex 1
                        at 0 opacity base
                        at 1 opacity base
                    end
                end
            end
            """);
        await context.Session.EffectScripts.UpsertAsync(preset);
        var panel = context.Session.ViewModel.EffectPresets;
        panel.SelectedPreset = panel.Items.Single(item => item.Id == preset.Id.ToString("D"));
        Assert.True(panel.ApplyCommand.CanExecute(null));
        panel.Parameters!.Rows[0].RawText = "true";
        panel.RefreshAvailability();
        Assert.False(panel.ApplyCommand.CanExecute(null));
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { Text = "AB", End = new(4) };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End,
                Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.5), new(new(4), 1)])] }]
        };
    }
}
