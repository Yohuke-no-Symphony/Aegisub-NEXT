using AegiNext.Desktop.Settings.Koumori;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class KoumoriWorkspaceTests
{
    [Fact]
    public async Task EmptyManualDirectoryReportsConfigurationErrorWithoutUsingWorkingDirectory()
    {
        await using var context = new WorkspaceSessionTestContext(hasAudio: true);
        await context.InitializeAsync();
        var path = Path.Combine(context.DirectoryPath, "controlled.mkv");
        await File.WriteAllBytesAsync(path, [0]);
        await context.Session.Controller.OpenAsync(path);
        context.Session.ApplicationContext.UpdatePreferences(value => value with
        {
            Koumori = value.Koumori with { Enabled = true, ModelSource = KoumoriModelSource.MANUAL }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Session.AnalyzeKoumoriAsync(CancellationToken.None, null));

        Assert.Equal(Localization.Get("Koumori.ManualDirectoryRequired"), error.Message);
    }

    [Fact]
    public async Task ClassicTimingPreparesCandidatesWhileModelSnapToggleIsOff()
    {
        await using var context = new WorkspaceSessionTestContext(hasAudio: true);
        await context.InitializeAsync();
        var mediaPath = Path.Combine(context.DirectoryPath, "controlled.mkv");
        await File.WriteAllBytesAsync(mediaPath, [0]);
        await context.Session.Controller.OpenAsync(mediaPath);
        context.Session.ApplicationContext.UpdatePreferences(value => value with
        {
            Koumori = value.Koumori with
            {
                Enabled = true,
                SnapEnabled = false,
                ModelSource = KoumoriModelSource.MANUAL,
                ModelDirectory = Path.Combine(context.DirectoryPath, "missing-model")
            }
        });
        var timeline = context.Session.ViewModel.Timeline;
        Assert.Equal(Localization.Get("Koumori.Snap"), timeline.KoumoriStatus);

        timeline.IsClassicTimingEnabled = true;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!timeline.KoumoriStatus.Contains("missing-model", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Contains("missing-model", timeline.KoumoriStatus, StringComparison.Ordinal);
        Assert.Equal(KoumoriSnapState.Failed, timeline.KoumoriSnapState);
        Assert.False(timeline.IsKoumoriSnapEnabled);
        timeline.IsClassicTimingEnabled = false;
        Assert.Equal(Localization.Get("Koumori.Snap"), timeline.KoumoriStatus);
        Assert.Empty(timeline.KoumoriBoundaries);
    }

    [Fact]
    public async Task ContinuousViewportJitterDoesNotRestartPendingAnalysis()
    {
        await using var context = new WorkspaceSessionTestContext(hasAudio: true);
        await context.InitializeAsync();
        var mediaPath = Path.Combine(context.DirectoryPath, "controlled.mkv");
        await File.WriteAllBytesAsync(mediaPath, [0]);
        await context.Session.Controller.OpenAsync(mediaPath);
        var timeline = context.Session.ViewModel.Timeline;
        timeline.VisibleDuration = 0.1;
        context.Session.ApplicationContext.UpdatePreferences(value => value with
        {
            Koumori = value.Koumori with
            {
                Enabled = true,
                SnapEnabled = true,
                ModelSource = KoumoriModelSource.MANUAL,
                ModelDirectory = Path.Combine(context.DirectoryPath, "missing-model")
            }
        });
        Assert.Equal(KoumoriSnapState.Analyzing, timeline.KoumoriSnapState);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var index = 0;
        while (timeline.KoumoriSnapState != KoumoriSnapState.Failed && DateTime.UtcNow < deadline)
        {
            timeline.ViewStart = ++index % 100 / 1000d;
            timeline.VisibleDuration = 0.1 + index % 2 / 1000d;
            await Task.Delay(10);
        }

        Assert.Equal(KoumoriSnapState.Failed, timeline.KoumoriSnapState);
        Assert.Contains("missing-model", timeline.KoumoriStatus, StringComparison.Ordinal);
        Assert.True(index > 0);
        Assert.Empty(timeline.KoumoriBoundaries);
        timeline.IsKoumoriSnapEnabled = false;
        Assert.Equal(KoumoriSnapState.Idle, timeline.KoumoriSnapState);
    }
}
