using System.Text;
using AegiNext.Desktop.Shortcuts;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class LrcWorkflowTests
{
    [Fact]
    public async Task EnhancedLrcImportKeepsKaraokeAndExportsBothVariants()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "song.lrc");
        await File.WriteAllTextAsync(path, "[ar:Artist]\n[00:01.00]<00:01.00>你好 <00:01.50>世界<00:02.00>\n[00:03.00]\n", new UTF8Encoding(true));
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_LRC);

        Assert.Null(context.Session.LastError);
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        Assert.Equal("你好 世界", line.Text);
        Assert.Equal(2, line.Karaoke.Length);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, message => message.Contains("[ar:]", StringComparison.Ordinal));

        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "enhanced.lrc");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.EXPORT_ENHANCED_LRC);
        Assert.Null(context.Session.LastError);
        var bytes = await File.ReadAllBytesAsync(context.Dialogs.SavePath);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("[00:01.00]<00:01.00>你好 <00:01.50>世界<00:02.00>\n", Encoding.UTF8.GetString(bytes));

        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "standard.lrc");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.EXPORT_LRC);
        Assert.Null(context.Session.LastError);
        Assert.Equal("[00:01.00]你好 世界\n[00:03.00]\n", await File.ReadAllTextAsync(context.Dialogs.SavePath));

        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
    }
}
