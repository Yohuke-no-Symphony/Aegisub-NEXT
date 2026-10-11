using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ProjectPersistenceUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoSaveWithLegacyMediaKeepsFocusedDraftAndItsLastValidPreview(bool invalid)
    {
        using var environment = new UiTestEnvironment();
        var clock = new ManualPlaybackTimeProvider();
        await using var session = new WorkbenchSession(new StartupTestDialogService(),
            preferencesStore: new(environment.DirectoryPath),
            initialPreferences: new()
            {
                Language = "en-US", Projects = new() { WorkspaceRoot = environment.DirectoryPath }
            }, persistenceTimeProvider: clock);
        await session.Styles.Completion;
        Assert.Equal(ProjectOpenStatus.OPENED,
            (await session.CreateProjectAsync(new ProjectCreationRequest("中文 ABC 123", environment.DirectoryPath))).Status);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(session.ProjectDirectory, "视频 01.mkv"));
        session.Editor.Apply("Legacy media", document => document with { Assets = [asset] });
        var id = session.Editor.AddSubtitle(MediaTime.Zero, new(2), "字幕 ABC 123");
        session.SelectCue(id);
        await session.WaitForProjectIdleAsync();
        var window = new MainWindow(session);
        try
        {
            window.Show();
            window.Layouts.Activate(WorkbenchPanelIds.STYLES);
            var input = UiTestActions.Find<NumericDraftInput>(window, "FontSizeInput");
            input.BringIntoView();
            window.UpdateLayout();
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            session.ViewModel.Styles.Subtitle.FontSizeText = "72.50";
            Dispatcher.UIThread.RunJobs();
            var committed = session.DocumentSnapshot;
            var preview = session.PreviewDocument;
            var undo = session.Editor.UndoLabel;
            Assert.Equal(72.5, preview.Subtitles[0].Style.FontSize);
            Assert.NotEqual(72.5, committed.Subtitles[0].Style.FontSize);
            if (invalid)
            {
                session.ViewModel.Styles.Subtitle.FontSizeText = "7e-";
                Dispatcher.UIThread.RunJobs();
                Assert.Same(preview, session.PreviewDocument);
            }

            clock.Advance(TimeSpan.FromMinutes(2));
            await session.Persistence.Completion;
            Dispatcher.UIThread.RunJobs();

            Assert.False(session.Editor.HasUnsavedChanges);
            Assert.Same(committed, session.DocumentSnapshot);
            Assert.Same(preview, session.PreviewDocument);
            Assert.Same(preview, session.ViewModel.Preview.Scene.Document);
            Assert.True(box.IsFocused);
            Assert.Equal(invalid ? "7e-" : "72.50", box.Text);
            Assert.Equal(undo, session.Editor.UndoLabel);
            var persisted = await ProjectStore.LoadAsync(session.ProjectPath!);
            Assert.Equal(asset, Assert.Single(persisted.Assets));
            Assert.Equal(committed.Subtitles[0].Style.FontSize, persisted.Subtitles[0].Style.FontSize);
        }
        finally
        {
            session.ViewModel.Styles.Subtitle.FontSizeText = session.DocumentSnapshot.Subtitles[0].Style.FontSize
                .ToString(System.Globalization.CultureInfo.CurrentCulture);
            await window.DisposeAsync();
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        }
    }
}
