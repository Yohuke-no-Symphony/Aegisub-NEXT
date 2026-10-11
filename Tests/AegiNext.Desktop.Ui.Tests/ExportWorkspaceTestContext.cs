using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels;
using AegiNext.Desktop.Panels.Assets;
using AegiNext.Desktop.Panels.DrawingTools;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Panels.Preview;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Panels.Subtitles;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class ExportWorkspaceTestContext : IAsyncDisposable
{
    private readonly UiTestEnvironment environment = new();
    private readonly PreviewTestSource source = new(1, 0, 5000, 10000, 15000, 20000);
    internal ExportWorkspaceTestContext()
    {
        try
        {
            var assetId = Guid.NewGuid();
            Editor = new(new ProjectDocument
            {
                Name = "Export boundary",
                Assets = [new(assetId, ProjectAssetKind.MEDIA, string.Empty, ExternalPath: Path.Combine(environment.DirectoryPath, "controlled.mkv"))],
                Media = new(assetId, 0, null, MediaTime.Zero)
            });
            Session = new(Dialogs,
                update => new VideoPreviewController((_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), VideoWidth: 1, VideoHeight: 1)),
                    (_, _) => new(_ => source), () => new UiPreviewConverter(), DispatchAsync, update), DispatchAsync,
                Editor, new WorkbenchPreferencesStore(environment.DirectoryPath), ExportService);
            var viewModel = Session.ViewModel;
            Panels = new(StringComparer.Ordinal)
            {
                [WorkbenchPanelIds.PREVIEW] = new PreviewPanelView(viewModel.Preview, Session),
                [WorkbenchPanelIds.TIMELINE] = new TimelinePanelView(viewModel.Timeline, Session),
                [WorkbenchPanelIds.SUBTITLES] = new SubtitlesPanelView(viewModel.Subtitles, Session),
                [WorkbenchPanelIds.STYLES] = new StylesPanelView(viewModel.Styles, Session),
                [WorkbenchPanelIds.ASSETS] = new AssetsPanelView(viewModel.Assets, Session),
                [WorkbenchPanelIds.DRAWING_TOOLS] = new DrawingToolsPanelView(viewModel.DrawingTools),
                [WorkbenchPanelIds.EFFECTS] = new EffectsPanelView(viewModel.Effects, Session),
                [WorkbenchPanelIds.MASKS] = new MaskPanelView(viewModel.Masks, Session),
                [WorkbenchPanelIds.EXPORT] = new ExportPanelView(viewModel.Export, Session),
                [WorkbenchPanelIds.LOG] = new LogPanelView(viewModel.Log, Session.Journal),
                [WorkbenchPanelIds.SUBTITLE_DETAILS] = new AegiNext.Desktop.Panels.SubtitleDetails.SubtitleDetailsPanelView(Session)
            };
            Owner = new() { Width = 1280, Height = 820 };
            Layouts = new(Owner, Panels, environment.DirectoryPath, viewModel.TryCommitDrafts,
                viewModel.CancelGestures, _ => { });
            Owner.Content = Layouts.Host;
            Owner.Show();
            Flush();
        }
        catch
        {
            environment.Dispose();
            throw;
        }
    }

    internal ProjectEditor Editor { get; }
    internal WorkbenchSession Session { get; }
    internal Dictionary<string, Control> Panels { get; }
    internal WorkbenchLayoutController Layouts { get; }
    internal Window Owner { get; }
    internal ControlledWorkbenchExportService ExportService { get; } = new();
    internal ControlledExportDialogService Dialogs { get; } = new();
    internal int SourceDisposeCount => source.DisposeCount;
    internal string OutputPath => Path.Combine(environment.DirectoryPath, "output.mp4");

    internal async Task InitializeAsync()
    {
        await Session.ApplicationContext.Initialization;
        await Session.Styles.Completion;
        await Session.Controller.OpenAsync("controlled.mkv");
        Flush();
    }

    internal void Flush()
    {
        Owner.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Owner.UpdateLayout();
    }

    /// <summary>排空测试边界并关闭全部窗口和固定面板。</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            Dialogs.ResolveOutput(null);
            ExportService.Release();
            await Session.DisposeAsync();
            await Layouts.FlushAsync();
        }
        finally
        {
            try
            {
                Layouts.Dispose();
                foreach (var panel in Panels.Values.Cast<IWorkbenchPanelView>())
                {
                    panel.Dispose();
                }
                Owner.Close();
                Assert.Equal(1, ExportService.DisposeCount);
                Assert.All(source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
            }
            finally
            {
                environment.Dispose();
            }
        }
    }

    private static async Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken);
    }
}
