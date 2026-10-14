using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LayerPositionEditorPivotRangeUiTests
{
    [AvaloniaFact]
    public void ExtendedPivotInputKeepsOutsideTextAndEscapeRestoresOnlyItsComponent()
    {
        using var environment = new UiTestEnvironment();
        var draft = new LayerPositionDraft { PivotMinimum = -4000000000m, PivotMaximum = 4000000000m };
        draft.Load(new() { Pivot = new(-2, 3), Offset = new(15, 20) },
            new(new(800, 600), new(), new(100, 50), new()));
        var editor = new LayerPositionEditor { Draft = draft, ShowPositionMode = false, ShowAutomaticPositionAction = false };
        var window = new Window { Width = 440, Height = 760, Content = editor };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var inputs = editor.GetLogicalDescendants().OfType<NumericDraftInput>().ToArray();
            var pivot = Assert.Single(inputs, input => input.Name == "PivotXInput");
            var anchor = Assert.Single(inputs, input => input.Name == "AnchorXInput");
            Assert.Equal(draft.PivotMinimum, pivot.Minimum);
            Assert.Equal(draft.PivotMaximum, pivot.Maximum);
            Assert.Equal(-2m, pivot.Value);
            Assert.Equal(0m, anchor.Minimum);
            Assert.Equal(1m, anchor.Maximum);
            var text = Assert.Single(pivot.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            text.SelectAll();
            window.KeyTextInput("-4");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("-4", draft.PivotX.RawText);
            Assert.Null(draft.Validate());
            text.SelectAll();
            window.KeyTextInput("7e-");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("PivotXInput", draft.Validate());

            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(draft.Validate());
            Assert.Equal("-2", draft.PivotX.RawText);
            Assert.Equal("3", draft.PivotY.RawText);
            Assert.Equal(new ScenePoint(15, 20), draft.CreatePosition()!.Offset);
        }
        finally
        {
            window.Close();
        }
    }
}
