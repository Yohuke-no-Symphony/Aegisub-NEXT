using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskTopologyLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task TopologyCommandsExplainTheLockAndNodeAnimationClearRestoresEditingInBothThemesAndLanguages(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        try
        {
            Localization.SetLanguage(language);
            context.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await context.OpenMediaAsync();
            await UiTestActions.CreateSubtitleAsync(context, text: "蒙版 Clip 123");
            var session = context.Session;
            var first = new MaskNode { Position = new(100, 100) };
            var second = new MaskNode { Position = new(300, 100) };
            var third = new MaskNode { Position = new(200, 300) };
            session.Editor.SetClipMask(session.SelectedLayer!.Id, new VectorClipMask { Contours = [new() { Nodes = [first, second, third] }] });
            session.Editor.SetKeyframe(session.SelectedLayer!.Id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), new(new(0), first.Position));
            session.Editor.SetKeyframe(session.SelectedLayer!.Id, AnimationProperty.MASK_POSITION, new(new(0), new ScenePoint(1, 2)));
            var source = session.DocumentSnapshot;
            var section = UiTestActions.Find<StackPanel>(context.Window, "ClipMaskSection");
            context.ViewModel.Masks.SelectedPoint = context.ViewModel.Masks.Points[0];
            section.BringIntoView();
            context.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var subdivision = UiTestActions.Find<Button>(context.Window, "SubdivideMaskButton");
            var contour = UiTestActions.Find<Button>(context.Window, "AddMaskContourButton");
            var rectangle = UiTestActions.Find<Button>(context.Window, "RectangleMaskButton");
            Assert.False(subdivision.IsEffectivelyEnabled);
            Assert.False(contour.IsEffectivelyEnabled);
            Assert.False(rectangle.IsEffectivelyEnabled);
            Assert.Equal(Localization.Get("Workbench.MaskTopologyLocked"), ToolTip.GetTip(subdivision));
            Assert.True(section.Bounds.Width > 0);
            Assert.All(section.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible), text =>
                Assert.DoesNotContain("Workbench.", text.Text ?? string.Empty, StringComparison.Ordinal));
            Assert.Null(context.Window.Panels["masks"].FindControl<Button>("ClearMaskNodeAnimationButton"));
            context.ViewModel.Effects.MaskExpanded = true;
            context.Window.Layouts.Activate("effects");
            var clear = UiTestActions.Find<Button>(context.Window, "ClearMaskNodeAnimationButton");
            Assert.True(clear.IsEffectivelyEnabled);
            Assert.Contains(clear.GetVisualAncestors(), ancestor => ReferenceEquals(ancestor, context.Window.Panels["effects"]));
            UiTestActions.Click(context.Window, "ClearMaskNodeAnimationButton");
            Assert.True(subdivision.IsEffectivelyEnabled);
            Assert.True(contour.IsEffectivelyEnabled);
            Assert.Equal(AnimationProperty.MASK_POSITION, Assert.Single(session.SelectedLayer!.Tracks).Property);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            Localization.SetLanguage(originalLanguage);
        }
    }
}
