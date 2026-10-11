using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Styles;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class StyleEditorHostUiTests
{
    [AvaloniaFact]
    public async Task SwitchingClipTypesUsesTypedViewsAndRetainsAnInvalidGraphicFieldUntilEscape()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var cue = new SubtitleLine { Text = "Caption", End = new(4) };
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var subtitle = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, TrackId = shapeTrack.Id, Name = "Shape", Shape = new(ShapeKind.RECTANGLE, 100, 60), End = new(4)
        };
        context.Session.Editor.Reset(new() { Tracks = [ProjectTrack.Default, shapeTrack], Subtitles = [cue], Layers = [subtitle, shape] });
        context.Session.SelectCue(cue.Id);
        var panel = new StylesPanelView(context.ViewModel.Styles, context.Session);
        var window = new Window { Width = 400, Height = 700, Content = panel };
        try
        {
            window.Show();
            var host = UiTestActions.Find<ContentControl>(panel, "StyleEditorHost");
            var subtitleView = Assert.IsType<SubtitleStyleView>(host.Content);
            Assert.Same(context.ViewModel.Styles.Subtitle, subtitleView.DataContext);
            context.Session.SelectLayer(shape.Id, [shape.Id]);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var shapeView = Assert.IsType<ShapeStyleView>(host.Content);
            Assert.Same(context.ViewModel.Styles.Shape, shapeView.DataContext);
            Assert.DoesNotContain(panel.GetVisualDescendants(), control => control is FontFamilyPicker);
            var width = UiTestActions.Find<NumericDraftInput>(panel, "LayerWidthInput");
            Assert.True(width.FocusInput());
            Assert.Single(width.GetVisualDescendants().OfType<TextBox>()).SelectAll();
            window.KeyTextInput("7e-");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("7e-", context.ViewModel.Styles.Shape.Width.RawText);
            context.Session.SelectCue(cue.Id);
            Assert.Equal(shape.Id, context.Session.SelectedLayerId);
            Assert.Same(shapeView, host.Content);
            Assert.True(width.FocusInput());
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("100", width.RawText);
            context.Session.SelectCue(cue.Id);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(subtitleView, host.Content);
            Assert.Same(context.ViewModel.Styles.Subtitle, subtitleView.DataContext);
            Assert.False(context.Session.HasProjectDrafts);
        }
        finally
        {
            context.ViewModel.Styles.Shape.RestoreField("LayerWidthInput");
            window.Close();
            panel.Dispose();
        }
    }
}
