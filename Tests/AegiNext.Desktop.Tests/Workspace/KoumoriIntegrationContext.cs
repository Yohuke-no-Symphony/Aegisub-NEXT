using AegiNext.Desktop.Settings.Koumori;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class KoumoriIntegrationContext : IAsyncDisposable
{
    private readonly int audioStream;
    private readonly MediaTime mediaOrigin;

    internal KoumoriIntegrationContext(MediaTime duration)
    {
        var tempRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("KOUMORI_INTEGRATION_TEMP_ROOT")!);
        var relativeTemp = Path.GetRelativePath(tempRoot, Path.GetFullPath(Path.GetTempPath()));
        Assert.False(Path.IsPathRooted(relativeTemp) || relativeTemp == ".." || relativeTemp.StartsWith(".." + Path.DirectorySeparatorChar),
            "Set TMPDIR (or TEMP on Windows) inside KOUMORI_INTEGRATION_TEMP_ROOT before starting the test process.");
        MediaPath = Path.GetFullPath(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_MEDIA")!);
        PcmPath = Path.GetFullPath(Environment.GetEnvironmentVariable("KOUMORI_TRAINING_PCM")!);
        PackagePath = Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("KOUMORI_PARITY_FIXTURE")!), "package");
        Assert.True(File.Exists(MediaPath), "Explicit native media does not exist.");
        Assert.True(File.Exists(PcmPath), "Explicit training PCM does not exist.");
        Assert.True(File.Exists(Path.Combine(PackagePath, "manifest.json")), "Parity package manifest does not exist.");
        audioStream = int.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_STREAM") ?? "1", CultureInfo.InvariantCulture);
        var originSeconds = decimal.Parse(Environment.GetEnvironmentVariable("KOUMORI_NATIVE_ORIGIN") ?? "0", CultureInfo.InvariantCulture);
        mediaOrigin = new(checked((long)Math.Round(originSeconds * TimeSpan.TicksPerSecond)), TimeSpan.TicksPerSecond);
        PreviewDuration = duration;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: MediaPath);
        var document = ProjectEditingOperations.CreateSubtitleClips(new()
        {
            Width = 1,
            Height = 1,
            Assets = [asset],
            Media = new(asset.Id, 0, audioStream, mediaOrigin)
        }, [new() { Start = new(1, 10), End = new(9, 5), Text = "Keep original" }], ProjectTrack.DEFAULT_TRACK_ID);
        Workspace = new(document, controllerFactory: update => new VideoPreviewController(ProbeAsync,
            (_, _) => new(_ => new PreviewTestSource(7, 0, 100, 200)), () => new PreviewTestConverter(), DispatchAsync, update));
    }

    internal WorkspaceSessionTestContext Workspace { get; }
    internal WorkbenchSession Session => Workspace.Session;
    internal ProjectEditor Editor => Workspace.Editor;
    internal string MediaPath { get; }
    internal string PcmPath { get; }
    internal string PackagePath { get; }
    internal MediaTime PreviewDuration { get; set; }

    internal async Task InitializeAsync()
    {
        await Workspace.InitializeAsync();
        Session.ViewModel.Timeline.IsClassicTimingEnabled = false;
        SetPreferences(true, PackagePath);
        await OpenAsync(MediaPath);
        Assert.True(Session.CanAnalyzeKoumori);
    }

    internal async Task OpenAsync(string path)
    {
        await Session.Controller.OpenAsync(path);
        Session.Tick();
        Assert.Null(Session.Controller.Snapshot.Error);
    }

    internal void SetPreferences(bool enabled, string? package = null)
    {
        Session.ApplicationContext.UpdatePreferences(value => value with
        {
            Koumori = value.Koumori with
            {
                Enabled = enabled,
                SnapEnabled = false,
                ModelSource = KoumoriModelSource.MANUAL,
                ModelDirectory = package ?? value.Koumori.ModelDirectory
            }
        });
    }

    internal string CopyPackage()
    {
        var destination = Path.Combine(Workspace.DirectoryPath, "alternate-model");
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(PackagePath))
        {
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)));
        }
        return destination;
    }

    public ValueTask DisposeAsync()
    {
        return Workspace.DisposeAsync();
    }

    private Task<VideoPreviewMedia> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pcm = string.Equals(path, PcmPath, StringComparison.Ordinal);
        return Task.FromResult(new VideoPreviewMedia(0, pcm ? MediaTime.Zero : mediaOrigin, PreviewDuration,
            pcm ? 0 : audioStream, 1, 1));
    }

    private static Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
