using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskSnapToolbarUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerToggleUpdatesPreviewAndPreferencesAndKeepsLiveLocalizedText(bool dark)
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        try
        {
            Localization.SetLanguage("en-US");
            context.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await context.OpenMediaAsync();
            await UiTestActions.CreateSubtitleAsync(context);
            await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_MASKS);
            var session = context.Session;
            var document = session.DocumentSnapshot;
            var button = UiTestActions.Find<ToolbarToggleButton>(context.Window, "MaskSnapButton");
            var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
            Assert.True(button.IsChecked);
            Assert.True(canvas.IsMaskSnapEnabled);
            Assert.Equal(MaterialIconKind.Magnet, Assert.IsType<MaterialIcon>(button.Content).Kind);
            Assert.Equal(32, button.Bounds.Width, 6);
            Assert.Equal(button.Bounds.Width, button.Bounds.Height, 6);
            Assert.Equal(Localization.Get("Workbench.MaskSnapHint"), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("Workbench.MaskSnap"), AutomationProperties.GetName(button));

            UiTestActions.Click(context.Window, "MaskSnapButton");
            await session.ApplicationContext.Completion;

            Assert.False(button.IsChecked);
            Assert.False(context.ViewModel.Masks.IsSnapEnabled);
            Assert.False(context.ViewModel.Preview.IsMaskSnapEnabled);
            Assert.False(canvas.IsMaskSnapEnabled);
            Assert.False(session.Preferences.MaskSnapEnabled);
            Assert.Same(document, session.DocumentSnapshot);
            Assert.False(session.PreferencesStore.Load().MaskSnapEnabled);

            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localization.Get("Workbench.MaskSnapHint"), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("Workbench.MaskSnap"), AutomationProperties.GetName(button));
            Assert.False(button.IsChecked);
            Assert.False(canvas.IsMaskSnapEnabled);
            Assert.Same(document, session.DocumentSnapshot);

            UiTestActions.Click(context.Window, "MaskSnapButton");
            await session.ApplicationContext.Completion;

            Assert.True(button.IsChecked);
            Assert.True(context.ViewModel.Masks.IsSnapEnabled);
            Assert.True(context.ViewModel.Preview.IsMaskSnapEnabled);
            Assert.True(canvas.IsMaskSnapEnabled);
            Assert.True(session.Preferences.MaskSnapEnabled);
            Assert.Same(document, session.DocumentSnapshot);
            Assert.True(session.PreferencesStore.Load().MaskSnapEnabled);
        }
        finally
        {
            Localization.SetLanguage(originalLanguage);
        }
    }
}
