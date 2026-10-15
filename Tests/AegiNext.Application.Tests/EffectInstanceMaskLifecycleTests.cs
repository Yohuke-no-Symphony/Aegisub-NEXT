using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class EffectInstanceMaskLifecycleTests
{
    [Theory]
    [InlineData("clear")]
    [InlineData("node")]
    [InlineData("contour")]
    [InlineData("apply")]
    [InlineData("update")]
    public void RemovingRequiredGeometryRejectsAtomicallyAndPreservesRedo(string action)
    {
        var document = Document();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var editor = new ProjectEditor(document);
        editor.UpdateLayer(layer.Id, current => current with { Opacity = 0.5 });
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        var notifications = 0;
        editor.Changed += (_, _) => notifications++;

        Assert.Throws<EffectScriptException>(() => RemoveMask(editor, layer, mask, action));

        Assert.Same(document, editor.Snapshot);
        Assert.Equal(0, notifications);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal(0.5, editor.Snapshot.Layers[0].Opacity);
    }

    [Fact]
    public void DisabledInstanceAllowsClearButReenablingWithoutRequiredGeometryIsAtomic()
    {
        var document = Document();
        var layer = document.Layers[0];
        var instance = Assert.Single(layer.Effects);
        var editor = new ProjectEditor(document);
        editor.SetEffectInstanceEnabled(layer.Id, instance.Id, false);
        var disabled = editor.Snapshot;

        editor.ClearClipMask(layer.Id);

        var cleared = editor.Snapshot;
        var edited = cleared.Layers[0];
        Assert.Null(edited.Mask);
        Assert.Equal(disabled.Layers[0].Effects, edited.Effects);
        Assert.False(Assert.Single(edited.Effects).IsEnabled);
        Assert.Same(layer.Tracks[0], Assert.Single(edited.Tracks));
        Assert.Null(SceneEvaluator.EvaluateMask(edited, new(1), cleared.Subtitles[0]));
        Assert.Throws<EffectScriptException>(() => editor.SetEffectInstanceEnabled(layer.Id, instance.Id, true));
        Assert.Same(cleared, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(disabled, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void PureInstanceTransactionAlsoRefusesToEnableAMaskEffectWithoutGeometry()
    {
        var document = Document();
        var layer = document.Layers[0];
        var instance = layer.Effects[0] with { IsEnabled = false };
        var withoutMask = document with
        {
            Layers = [layer with { Mask = null, Tracks = [layer.Tracks[0]], Effects = [instance] }]
        };

        Assert.Throws<EffectScriptException>(() => ProjectEditingOperations.SetEffectInstanceEnabled(withoutMask, layer.Id, instance.Id, true));
        Assert.Null(withoutMask.Layers[0].Mask);
        Assert.False(withoutMask.Layers[0].Effects[0].IsEnabled);
    }

    private static void RemoveMask(ProjectEditor editor, ProjectLayer layer, VectorClipMask mask, string action)
    {
        switch (action)
        {
            case "clear":
                editor.ClearClipMask(layer.Id);
                break;
            case "node":
                editor.RemoveClipMaskNode(layer.Id, mask.Contours[0].Nodes[0].Id);
                break;
            case "contour":
                editor.RemoveClipMaskContour(layer.Id, mask.Contours[0].Id);
                break;
            case "apply":
                editor.Apply("Remove mask", source => source with { Layers = [layer with { Mask = null, Tracks = [layer.Tracks[0]] }] });
                break;
            default:
                editor.UpdateLayer(layer.Id, current => current with { Mask = null, Tracks = [layer.Tracks[0]] });
                break;
        }
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { Text = "Mask", End = new(2) };
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Mask = new VectorClipMask { Contours = [new() { Nodes = [new() { Position = new(20, 30) }] }] },
                Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 1d)]), new(AnimationProperty.MASK_POSITION, [new(new(0), new ScenePoint(3, 4))])],
                Effects = [new()
                {
                    Name = "Mask motion", End = new(2),
                    Source = "effect \"mask-required\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
                        "at 0 mask-position offset(10, 20)\nat 1 mask-position offset(10, 20)\nend\nend"
                }]
            }]
        };
    }
}
