using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using AegiNext.SubtitleFormats;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleConversionDialogUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", WorkbenchTheme.LIGHT, true)]
    [InlineData("zh-CN", WorkbenchTheme.DARK, false)]
    public async Task RegisteredDialogShowsReadableNotesAndReturnsTheClickedDecision(string language, WorkbenchTheme theme, bool proceed)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(value => value with { Theme = theme });
        Localization.SetLanguage(language);
        var line = new SubtitleLine { Start = new(1), End = new(2), Text = "Subtitle 中文 ABC 123" };
        var review = new SubtitleConversionReview(
            Enumerable.Range(0, 200).Select(index => new SubtitleFormatDiagnostic(
                "Ass.Note" + index, "Conversion note " + index, SubtitleId: line.Id)).ToImmutableArray(), [line]);
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var answer = service.ConfirmSubtitleConversionAsync(review);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        try
        {
            UiTestCapture.CaptureExportPanel(dialog, $"subtitle-conversion-{theme.ToString().ToLowerInvariant()}-{language}");
            dialog.Width = dialog.MinWidth;
            dialog.Height = dialog.MinHeight;
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            Assert.Contains("business-surface", dialog.Classes);
            Assert.Equal(Localization.Get("Workbench.SubtitleConversionTitle"), dialog.Title);
            Assert.Equal(review.FormatSummary(), UiTestActions.Find<TextBlock>(dialog, "ConversionSummary").Text);
            var details = UiTestActions.Find<TextBox>(dialog, "ConversionDetails");
            Assert.True(details.IsReadOnly);
            Assert.Contains("Subtitle 中文 ABC 123", details.Text);
            Assert.Contains("00:00:01.000 → 00:00:02.000", details.Text);
            Assert.Contains("Conversion note 199", details.Text);
            Assert.DoesNotContain(line.Id.ToString(), details.Text);
            Assert.True(details.Bounds.Height > 30);
            var scroller = Assert.Single(details.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            scroller.Offset = new(0, scroller.Extent.Height);
            dialog.UpdateLayout();
            Assert.True(scroller.Offset.Y > 0);
            var button = UiTestActions.Find<Button>(dialog, "ContinueButton");
            var bottom = button.TranslatePoint(new(0, button.Bounds.Height), dialog)!.Value.Y;
            Assert.InRange(bottom, 1, dialog.ClientSize.Height);

            UiTestActions.Click(dialog, proceed ? "ContinueButton" : "CancelButton");

            Assert.Equal(proceed, await answer);
            Assert.Empty(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        }
        finally
        {
            dialog.Close(false);
        }
    }

    [AvaloniaFact]
    public void LanguageChangesRefreshVisiblePositionAndSummaryAndReleaseOnClose()
    {
        using var environment = new UiTestEnvironment();
        var line = new SubtitleLine { Start = new(1), End = new(2), Text = "Business 中文" };
        var review = new SubtitleConversionReview([new("Ass.Note", "Business diagnostic", SubtitleId: line.Id)], [line]);
        var owner = new Window();
        var dialog = new SubtitleConversionDialog(review);
        try
        {
            owner.Show();
            dialog.Show(owner);
            var details = UiTestActions.Find<TextBox>(dialog, "ConversionDetails");
            var summary = UiTestActions.Find<TextBlock>(dialog, "ConversionSummary");
            Localization.SetLanguage("zh-CN");
            Assert.Contains("字幕 1", details.Text);
            Assert.Equal("1 条转换提示，涉及 1 条字幕", summary.Text);
            Localization.SetLanguage("en-US");
            Assert.Contains("Subtitle 1", details.Text);
            Assert.Equal("Conversion notes: 1 · Subtitles: 1", summary.Text);
            Assert.Contains("Business diagnostic", details.Text);
            dialog.Close();
            var previousDetails = details.Text;
            var previousSummary = summary.Text;
            Localization.SetLanguage("zh-CN");
            Assert.Equal(previousDetails, details.Text);
            Assert.Equal(previousSummary, summary.Text);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    /// <summary>工程生命周期取消关闭转换确认，已取消的请求不会创建或注册窗口。</summary>
    [AvaloniaFact]
    public async Task CancellationClosesPendingReviewAndPrecancelledRequestDoesNotCreateWindow()
    {
        await using var context = new MainWindowTestContext();
        using var cancellation = new CancellationTokenSource();
        var registeredWindows = new List<Window>();
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: window =>
        {
            registeredWindows.Add(window);
            context.WindowRegistry.RegisterAuxiliary(window);
        });
        var review = new SubtitleConversionReview([new("Ass.Note", "Note")], []);
        var answer = service.ConfirmSubtitleConversionAsync(review, cancellation.Token);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        try
        {
            await cancellation.CancelAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(dialog.IsVisible);
            Assert.Empty(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.ConfirmSubtitleConversionAsync(review, cancellation.Token));
            Assert.Same(dialog, Assert.Single(registeredWindows));
            Assert.Empty(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        }
        finally
        {
            dialog.Close(false);
        }
    }

    [AvaloniaFact]
    public async Task EscapeCancelsWithoutLeavingAWindow()
    {
        await using var context = new MainWindowTestContext();
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var answer = service.ConfirmSubtitleConversionAsync(new([new("Ass.Note", "Note")], []));
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        try
        {
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(await answer);
            Assert.Empty(context.Window.OwnedWindows.OfType<SubtitleConversionDialog>());
        }
        finally
        {
            dialog.Close(false);
        }
    }
}
