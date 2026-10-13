using AegiNext.Desktop.Shortcuts;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using AegiNext.SubtitleFormats;
using System.Text;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleFormatWorkflowTests
{
    [Fact]
    public async Task LargeSubtitleBodiesCanBeImportedEditedSavedAndReopened()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var content = new string('中', 1_000_001);
        var lines = Enumerable.Range(0, 9).Select(index => new SubtitleLine
        {
            Start = new(index), End = new(index + 1), Text = content
        }).ToArray();
        var path = Path.Combine(context.DirectoryPath, "large-body.srt");
        await File.WriteAllTextAsync(path, SubtitleTextFormat.WriteSrt(lines), new UTF8Encoding(false));
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);

        Assert.Null(context.Session.LastError);
        Assert.Equal(lines.Length, context.Editor.Snapshot.Subtitles.Length);
        Assert.All(context.Editor.Snapshot.Subtitles, line => Assert.Equal(content, line.Text));
        var imported = context.Editor.Snapshot.Subtitles[0];
        context.Editor.ReplaceSubtitleTextRange(imported.Id, 0, 1, "文");
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "large-body.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.Null(context.Session.LastError);
        Assert.True(new FileInfo(context.Dialogs.SavePath).Length > 32 * 1024 * 1024);
        Assert.False(context.Session.HasUnsavedChanges);
        context.Dialogs.OpenPath = context.Dialogs.SavePath;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(lines.Length, context.Editor.Snapshot.Subtitles.Length);
        Assert.Equal("文" + content[1..], context.Editor.Snapshot.Subtitles[0].Text);
        Assert.All(context.Editor.Snapshot.Subtitles.Skip(1), line => Assert.Equal(content, line.Text));
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(WorkbenchCommand.IMPORT_SUBTITLES)]
    [InlineData(WorkbenchCommand.IMPORT_ASS)]
    public async Task SubtitleFilesLargerThanSixteenMiBAreImportedAtomically(WorkbenchCommand command)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var ass = command == WorkbenchCommand.IMPORT_ASS;
        var path = Path.Combine(context.DirectoryPath, ass ? "large.ass" : "large.srt");
        var source = ass
            ? "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,中文 😀\n"
            : "1\n00:00:00,000 --> 00:00:02,000\n中文 😀\n";
        await File.WriteAllTextAsync(path, new string(' ', 16 * 1024 * 1024) + "\n" + source,
            new UTF8Encoding(true));
        Assert.True(new FileInfo(path).Length > 16 * 1024 * 1024);
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(command);

        Assert.Null(context.Session.LastError);
        Assert.Equal("中文 😀", Assert.Single(context.Editor.Snapshot.Subtitles).Text);
        Assert.Single(context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SrtImportCreatesIndependentTracksForOverlapsAndOneUndoRestoresEverything()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "overlap.srt");
        await File.WriteAllTextAsync(path, "1\n00:00:00,000 --> 00:00:02,000\none\n\n2\n00:00:01,000 --> 00:00:03,000\ntwo\n");
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.Null(context.Session.LastError);
        Assert.Equal(3, context.Editor.Snapshot.Tracks.Length);
        Assert.Equal(2, context.Editor.Snapshot.Subtitles.Length);
        Assert.Equal(2, context.Editor.Snapshot.Layers.Length);
        Assert.DoesNotContain(context.Editor.Snapshot.Layers, clip => clip.TrackId == original.Tracks[0].Id);
        Assert.NotNull(context.Session.SelectedCueId);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task AssUnsupportedShearRequiresExplicitConversionAndCancelLeavesSnapshotUntouched()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "shear.ass");
        await File.WriteAllTextAsync(path, "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\fax30}hello\n");
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);
        Assert.Null(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, message => message.Contains("fax", StringComparison.OrdinalIgnoreCase));
        var review = Assert.IsType<SubtitleConversionReview>(context.Dialogs.ConversionReview);
        var importedLine = Assert.Single(review.Subtitles);
        Assert.Equal("hello", importedLine.Text);
        Assert.Contains("00:00:00.000 → 00:00:02.000", review.FormatDetails());
        Assert.DoesNotContain(importedLine.Id.ToString(), review.FormatDetails());
        context.Dialogs.ConversionChoice = true;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);
        Assert.Equal("hello", Assert.Single(context.Editor.Snapshot.Subtitles).Text);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task AssExportReviewIdentifiesTheOriginalSubtitleAndCancelPreservesTheDestination()
    {
        var line = new SubtitleLine
        {
            Start = new(1), End = new(3), Text = "Export 中文",
            Style = new() { ShadowBlur = 5 }
        };
        await using var context = new WorkspaceSessionTestContext(new()
        {
            Subtitles = [line], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        });
        await context.InitializeAsync();
        var snapshot = context.Editor.Snapshot;
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "review.ass");
        await File.WriteAllTextAsync(context.Dialogs.SavePath, "Original destination");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.EXPORT_ASS);

        var review = Assert.IsType<SubtitleConversionReview>(context.Dialogs.ConversionReview);
        Assert.Equal(line.Id, Assert.Single(review.Subtitles).Id);
        Assert.Contains("Export 中文", review.FormatDetails());
        Assert.Contains("00:00:01.000 → 00:00:03.000", review.FormatDetails());
        Assert.DoesNotContain(line.Id.ToString(), review.FormatDetails());
        Assert.Equal("Original destination", await File.ReadAllTextAsync(context.Dialogs.SavePath));
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task Utf16BomIsRejectedWhileUtf8BomIsAccepted()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "encoding.srt");
        const string TEXT = "1\n00:00:00,000 --> 00:00:02,000\n中文\n";
        await File.WriteAllTextAsync(path, TEXT, Encoding.Unicode);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.NotNull(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        await File.WriteAllTextAsync(path, TEXT, new UTF8Encoding(true));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.Null(context.Session.LastError);
        Assert.Equal("中文", Assert.Single(context.Editor.Snapshot.Subtitles).Text);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }

    [Fact]
    public async Task InvalidUtf8RejectsImportAtomically()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "invalid.srt");
        await File.WriteAllBytesAsync(path, [0xC3, 0x28]);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_SUBTITLES);
        Assert.NotNull(context.Session.LastError);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
