using System.Globalization;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NumericDraftPrecisionUiTests
{
    [AvaloniaTheory]
    [InlineData("unfinished")]
    [InlineData("1e100")]
    [InlineData("1e-100")]
    public void ChangingThePrecisionProjectionPreservesActualKeyboardDraftInsteadOfSavingTheLastValidValue(string rawText)
    {
        var draft = new NumericValueDraft { PreserveDoublePrecision = true };
        draft.Load(10);
        var input = new NumericDraftInput
        {
            PreserveDoublePrecision = true,
            [!NumericUpDown.ValueProperty] = new Binding(nameof(NumericValueDraft.Value)) { Source = draft, Mode = BindingMode.TwoWay },
            [!NumericDraftInput.RawTextProperty] = new Binding(nameof(NumericValueDraft.RawText)) { Source = draft, Mode = BindingMode.TwoWay }
        };
        var other = new Button { Content = "Other" };
        var window = new Window
        {
            Width = 420, Height = 180,
            Content = new StackPanel { Children = { input, other } }
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            text.SelectAll();
            window.KeyTextInput(rawText);
            foreach (var preserve in new[] { false, true, false, true })
            {
                input.PreserveDoublePrecision = preserve;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(rawText, draft.RawText);
                Assert.Equal(rawText, input.RawText);
                Assert.Equal(rawText, text.Text);
                Assert.Null(draft.Parse());
            }
            Assert.True(other.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rawText, draft.RawText);
            Assert.Equal(rawText, text.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("1e-100")]
    [InlineData("1e100")]
    public void NonDecimalFiniteDraftIgnoresSpinAndPreservesRawText(string rawText)
    {
        var draft = new NumericValueDraft { PreserveDoublePrecision = true };
        draft.Load(1);
        var input = new NumericDraftInput
        {
            PreserveDoublePrecision = true,
            [!NumericUpDown.ValueProperty] = new Binding(nameof(NumericValueDraft.Value)) { Source = draft, Mode = BindingMode.TwoWay },
            [!NumericDraftInput.RawTextProperty] = new Binding(nameof(NumericValueDraft.RawText)) { Source = draft, Mode = BindingMode.TwoWay }
        };
        var other = new Button { Content = "Other" };
        var window = new Window
        {
            Width = 420,
            Height = 180,
            Content = new StackPanel { Margin = new(12), Spacing = 12, Children = { input, other } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var spinner = Assert.Single(input.GetVisualDescendants().OfType<ButtonSpinner>());
            var increase = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_IncreaseButton");
            var decrease = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_DecreaseButton");
            Assert.True(text.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput(rawText);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rawText, draft.RawText);
            Assert.Equal(rawText, input.RawText);
            Assert.Equal(1, input.Increment);
            Assert.False(input.ShowButtonSpinner);
            Assert.False(increase.IsEffectivelyVisible);
            Assert.False(decrease.IsEffectivelyVisible);
            UiTestActions.Press(window, Key.Up);
            UiTestActions.Press(window, Key.Down);
            Assert.True(other.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rawText, draft.RawText);
            Assert.Equal(rawText, input.RawText);
            Assert.Equal(rawText, text.Text);
            Assert.Equal(1, input.Value);
            Assert.True(text.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("1");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, input.Increment);
            Assert.False(input.ShowButtonSpinner);
            UiTestActions.Press(window, Key.Up);
            UiTestActions.Press(window, Key.Down);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, decimal.Parse(draft.RawText, CultureInfo.CurrentCulture));
            Assert.Equal(draft.RawText, input.RawText);
            Assert.Equal(input.RawText, text.Text);
            Assert.Equal(1, input.Value);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0.01, 0.12)]
    [InlineData(0.5, 6)]
    public void NonDecimalFiniteTitleDragUsesConfiguredStepAndEscapeRestoresRawText(double increment, double expected)
    {
        var input = new NumericDraftInput
        {
            PreserveDoublePrecision = true,
            RawText = "1e-100",
            Increment = (decimal)increment
        };
        var title = new NumericDragLabel { Input = input, Text = "Value" };
        var window = new Window
        {
            Width = 420,
            Height = 180,
            Content = new StackPanel { Margin = new(12), Spacing = 12, Children = { title, input } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = title.TranslatePoint(new(20, title.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(12, 0));
            Assert.True(input.IsTitleDragging);
            Assert.Equal(expected, double.Parse(input.RawText, CultureInfo.CurrentCulture));
            UiTestActions.Press(window, Key.Escape);
            Assert.False(input.IsTitleDragging);
            Assert.Equal("1e-100", input.RawText);
            Assert.Equal((decimal)increment, input.Increment);
        }
        finally
        {
            window.Close();
        }
    }
}
