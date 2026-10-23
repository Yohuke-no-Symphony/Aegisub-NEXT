using System.Diagnostics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Koumori;
using Xunit.Abstractions;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class KoumoriIntegrationTests(ITestOutputHelper output)
{
    [KoumoriIntegrationFact]
    public async Task LoadedModelRemainsUsableAfterSaveFailureCancelsClose()
    {
        await using var context = new KoumoriIntegrationContext(new(2));
        await context.InitializeAsync();
        var first = await AnalyzeAsync(context);
        Assert.True(context.Session.IsKoumoriPreviewCurrent(first));
        var firstCache = CachePath(context);
        var firstPrefix = ReadCachePrefix(firstCache);
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        await context.Session.WaitForProjectIdleAsync();
        context.Workspace.Dialogs.UnsavedChoice = 1;
        context.Workspace.Dialogs.SavePath = Path.Combine(context.Workspace.DirectoryPath, "blocked.aeginext");
        Directory.CreateDirectory(context.Workspace.Dialogs.SavePath);

        await Assert.ThrowsAnyAsync<IOException>(() => context.Session.RequestCloseAsync());

        Assert.False(context.Session.IsClosing);
        Assert.True(context.Editor.HasUnsavedChanges);
        Assert.True(context.Session.CanAnalyzeKoumori);
        Assert.False(File.Exists(firstCache));
        var restored = await AnalyzeAsync(context);
        Assert.Equal(first.Result.ModelId, restored.Result.ModelId);
        Assert.Equal(first.Result.Range, restored.Result.Range);
        Assert.True(context.Session.IsKoumoriPreviewCurrent(restored));
        Assert.False(context.Session.IsProjectBusy);
        Assert.Equal(firstPrefix, ReadCachePrefix(CachePath(context)));
    }

    [KoumoriIntegrationFact]
    public Task SwitchingMediaCancelsNativeInferenceAndAllowsNewAnalysis()
    {
        return AssertContextChangeCancelsAsync(context => context.OpenAsync(context.PcmPath));
    }

    [KoumoriIntegrationFact]
    public Task SwitchingModelCancelsNativeInferenceAndAllowsNewAnalysis()
    {
        return AssertContextChangeCancelsAsync(context =>
        {
            context.SetPreferences(true, context.CopyPackage());
            return Task.CompletedTask;
        });
    }

    [KoumoriIntegrationFact]
    public Task DisablingKoumoriCancelsNativeInferenceAndAllowsReenabledAnalysis()
    {
        return AssertContextChangeCancelsAsync(context =>
        {
            context.SetPreferences(false);
            Assert.False(context.Session.CanAnalyzeKoumori);
            return Task.CompletedTask;
        });
    }

    [KoumoriIntegrationFact]
    public async Task RunCancellationAndModelSwitchReuseTheSessionPcmPrefix()
    {
        await using var context = new KoumoriIntegrationContext(new(36000));
        await context.InitializeAsync();
        await CancelAfterFirstWindowAsync(context);
        var path = CachePath(context);
        var created = File.GetCreationTimeUtc(path);
        var length = new FileInfo(path).Length;
        var prefix = ReadCachePrefix(path);
        var document = context.Editor.Snapshot;

        await CancelAfterFirstWindowAsync(context);

        Assert.Equal(path, CachePath(context));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.True(new FileInfo(path).Length >= length);
        Assert.Equal(prefix, ReadCachePrefix(path));
        context.SetPreferences(true, context.CopyPackage());

        await CancelAfterFirstWindowAsync(context);

        Assert.Equal(path, CachePath(context));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.True(new FileInfo(path).Length >= length);
        Assert.Equal(prefix, ReadCachePrefix(path));
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.IsProjectBusy);
        output.WriteLine("Run retry and model switch retained the same PCM cache file and {0} prefix bytes; cached length {1} -> {2} bytes.",
            prefix.Length, length, new FileInfo(path).Length);
    }

    [KoumoriIntegrationFact]
    public async Task SessionAppliesOverlapWithOneUndoAndRejectsStalePreviewWithoutEdits()
    {
        await using var context = new KoumoriIntegrationContext(new(2));
        await context.InitializeAsync();
        var preview = await AnalyzeAsync(context);
        var original = context.Editor.Snapshot;
        Assert.False(context.Editor.CanUndo);
        MediaTimeRange[] intervals = [new(new(1, 5), new(4, 5)), new(new(2, 5), new(6, 5))];

        Assert.Equal(2, context.Session.ApplyKoumoriPreview(preview, intervals));
        await context.Session.WaitForProjectIdleAsync();

        var applied = context.Editor.Snapshot;
        ProjectValidator.Validate(applied);
        Assert.Equal(original.Tracks.Length + 2, applied.Tracks.Length);
        Assert.Equal(original.Subtitles.Length + 2, applied.Subtitles.Length);
        Assert.Same(original.Subtitles[0], applied.Subtitles.Single(value => value.Id == original.Subtitles[0].Id));
        Assert.Same(original.Layers[0], applied.Layers.Single(value => value.Id == original.Layers[0].Id));
        var addedLines = applied.Subtitles.Where(value => value.Id != original.Subtitles[0].Id).OrderBy(value => value.Start).ToArray();
        Assert.All(addedLines, value => Assert.Equal(string.Empty, value.Text));
        Assert.Equal(intervals.Select(value => (value.Start, value.End)), addedLines.Select(value => (value.Start, value.End)));
        Assert.Equal(2, applied.Layers.Where(value => value.Id != original.Layers[0].Id).Select(value => value.TrackId).Distinct().Count());
        var undoLabel = context.Editor.UndoLabel;
        Assert.False(context.Session.IsKoumoriPreviewCurrent(preview));
        Assert.Throws<InvalidOperationException>(() => context.Session.ApplyKoumoriPreview(preview, intervals));
        Assert.Same(applied, context.Editor.Snapshot);
        Assert.Equal(undoLabel, context.Editor.UndoLabel);

        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
        var fresh = await AnalyzeAsync(context);
        await context.OpenAsync(context.PcmPath);
        Assert.False(context.Session.IsKoumoriPreviewCurrent(fresh));
        Assert.Throws<InvalidOperationException>(() => context.Session.ApplyKoumoriPreview(fresh, intervals));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
    }

    private async Task AssertContextChangeCancelsAsync(Func<KoumoriIntegrationContext, Task> change)
    {
        await using var context = new KoumoriIntegrationContext(new(36000));
        await context.InitializeAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var progress = new KoumoriIntegrationProgress();
        var analysis = context.Session.AnalyzeKoumoriAsync(cancellation.Token, progress);
        try
        {
            var fraction = await progress.FirstWindow.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.InRange(fraction, double.Epsilon, 0.999);
            Assert.False(analysis.IsCompleted);
            var elapsed = Stopwatch.StartNew();

            await change(context);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.WaitAsync(TimeSpan.FromSeconds(5)));
            elapsed.Stop();
            output.WriteLine("Context change canceled native + ORT inference after {0:F1} ms (first-window fraction {1:G6}).",
                elapsed.Elapsed.TotalMilliseconds, fraction);
            Assert.False(cancellation.IsCancellationRequested);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await analysis;
            }
            catch (OperationCanceledException)
            {
            }
        }

        var previousCache = CachePath(context);
        context.PreviewDuration = new(2);
        context.SetPreferences(true);
        await context.OpenAsync(context.MediaPath);
        var restored = await AnalyzeAsync(context);
        Assert.True(context.Session.IsKoumoriPreviewCurrent(restored));
        Assert.False(context.Session.IsProjectBusy);
        Assert.NotEqual(previousCache, CachePath(context));
        Assert.False(File.Exists(previousCache));
    }

    private static async Task CancelAfterFirstWindowAsync(KoumoriIntegrationContext context)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var progress = new KoumoriIntegrationProgress();
        var analysis = context.Session.AnalyzeKoumoriAsync(cancellation.Token, progress);
        try
        {
            await progress.FirstWindow.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(analysis.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await analysis;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static string CachePath(KoumoriIntegrationContext context)
    {
        return Assert.Single(Directory.EnumerateFiles(Path.Combine(context.Session.ScratchDirectory, "koumori"), "*.f32"));
    }

    private static byte[] ReadCachePrefix(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[checked((int)Math.Min(16000 * sizeof(float), stream.Length))];
        Assert.NotEmpty(bytes);
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static async Task<KoumoriPreview> AnalyzeAsync(KoumoriIntegrationContext context)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await context.Session.AnalyzeKoumoriAsync(cancellation.Token, null);
    }
}
