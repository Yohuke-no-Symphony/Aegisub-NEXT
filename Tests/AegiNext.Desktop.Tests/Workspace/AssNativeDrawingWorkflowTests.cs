using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssNativeDrawingWorkflowTests
{
    [Fact]
    public async Task PureDrawingImportsAsSelectedShapeAndOneUndoRestoresEverything()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var source = Path.Combine(context.DirectoryPath, "drawing.ass");
        await File.WriteAllTextAsync(source, Script("0"));
        context.Dialogs.OpenPath = source;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);

        Assert.Null(context.Session.LastError);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
        var layer = Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(LayerKind.SHAPE, layer.Kind);
        Assert.Equal(layer.Id, context.Session.SelectedLayerId);
        Assert.Null(context.Session.SelectedCueId);
        Assert.Equal((new MediaTime(1), new MediaTime(3)), (layer.Start, layer.End));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DrawingStrokeReviewShowsGeneratedGraphicAndDecliningDoesNotEditProject()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var source = Path.Combine(context.DirectoryPath, "drawing.ass");
        await File.WriteAllTextAsync(source, Script("2"));
        context.Dialogs.OpenPath = source;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);

        Assert.Null(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        var review = Assert.IsType<SubtitleConversionReview>(context.Dialogs.ConversionReview);
        Assert.Empty(review.Subtitles);
        var layer = Assert.Single(review.Layers);
        var diagnostic = Assert.Single(review.Diagnostics, item => item.Code == "Ass.ShapeStrokePlacement");
        Assert.Equal(layer.Id, diagnostic.LayerId);
        Assert.Null(diagnostic.SubtitleId);
        Assert.Equal(1, review.AffectedClipCount);
        Assert.Contains(layer.Name, review.FormatDetails(), StringComparison.Ordinal);
        Assert.DoesNotContain(layer.Id.ToString(), review.FormatDetails(), StringComparison.Ordinal);
    }

    private static string Script(string outline)
    {
        return $$"""
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            WrapStyle: 2
            ScaledBorderAndShadow: yes
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,{{outline}},0,7,10,10,10,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:01.00,0:00:03.00,Default,,0,0,0,,{\pos(80,60)\p1}m 0 0 l 30 0 30 30 0 30{\p0}
            """;
    }
}
