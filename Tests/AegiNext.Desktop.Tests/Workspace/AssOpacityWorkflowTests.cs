using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssOpacityWorkflowTests
{
    [Theory]
    [InlineData("\\fad(500,1000)", 1)]
    [InlineData("\\fade(255,64,255,0,500,2000,3000)", 191d / 255)]
    public async Task ImportPreservesComponentAlphaAndEditableOpacityAnimationInOneUndo(string fade, double peak)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "opacity.ass");
        await File.WriteAllTextAsync(path,
            $"[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,{{\\1a&H80&{fade}}}Fade 中文\n");
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);

        Assert.Null(context.Session.LastError);
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        var layer = Assert.Single(context.Editor.Snapshot.Layers);
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.OPACITY);
        Assert.Empty(track.Transforms);
        Assert.Equal(peak / 2, SceneEvaluator.EvaluateScalarTrack(track, new MediaTime(1, 4)), 10);
        Assert.Equal(peak, SceneEvaluator.EvaluateScalarTrack(track, new MediaTime(1)), 10);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new MediaTime(3)), 10);
        var styled = line.InlineSpans.FirstOrDefault()?.Style.ApplyTo(line.Style) ?? line.Style;
        Assert.Equal(127d / 255, styled.Fill.Alpha, 10);
        Assert.Equal(1, layer.Opacity);
        Assert.Equal(line.Id, context.Session.SelectedCueId);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, value => value.StartsWith("Ass.OpacityComposition:", StringComparison.Ordinal));
        Assert.DoesNotContain(context.Dialogs.ConversionDiagnostics,
            value => value.StartsWith("Ass.Fade", StringComparison.Ordinal) || value.StartsWith("Ass.DuplicateFade:", StringComparison.Ordinal));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
