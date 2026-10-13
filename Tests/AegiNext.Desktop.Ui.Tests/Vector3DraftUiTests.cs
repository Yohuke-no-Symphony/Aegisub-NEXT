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

public sealed class Vector3DraftUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 270)]
    [InlineData(true, 270)]
    [InlineData(false, 650)]
    [InlineData(true, 650)]
    public void OptionalThirdComponentSharesTheVectorRowWithoutExpandingLongNumbers(bool hasZ, double width)
    {
        const string precise = "123.1234567890123456789012345678";
        var vector = new VectorDraftInput
        {
            HasZ = hasZ,
            PreserveDoublePrecision = true,
            XFieldKey = "First", YFieldKey = "Second", ZFieldKey = "Third",
            XText = precise, YText = "7e-", ZText = "1e-100"
        };
        var other = new Button { Content = "Other" };
        var window = new Window
        {
            Width = width, Height = 160,
            Content = new StackPanel { Margin = new(12), Spacing = 8, Children = { vector, other } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var inputs = vector.GetVisualDescendants().OfType<NumericDraftInput>().ToArray();
            Assert.Equal(hasZ ? 3 : 2, inputs.Length);
            Assert.All(inputs, input => Assert.Equal(48, input.MinWidth));
            var points = inputs.Select(input => input.TranslatePoint(default, window)!.Value).ToArray();
            Assert.All(points, point => Assert.Equal(points[0].Y, point.Y));
            Assert.True(points[^1].X + inputs[^1].Bounds.Width <= window.ClientSize.Width - 12);
            var first = inputs.Single(input => input.Name == "First");
            var box = Assert.Single(first.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(precise, box.Text);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.End);
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(precise, first.RawText);
            Assert.Equal(precise, vector.XText);
            Assert.Equal(precise, box.Text);
            var scroll = Assert.Single(box.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(0, scroll.Offset.X);
            Assert.Equal("7e-", vector.YText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ThirdComponentRetainsBindingsNamesAndDraftsAcrossReattachmentAndTitleDragCancellation()
    {
        var x = new NumericValueDraft { PreserveDoublePrecision = true };
        var y = new NumericValueDraft { PreserveDoublePrecision = true };
        var z = new NumericValueDraft { PreserveDoublePrecision = true };
        x.Load(10.123456789012345);
        y.Load(20);
        z.Load(1e-100);
        var vector = new VectorDraftInput
        {
            HasZ = true, PreserveDoublePrecision = true,
            XFieldKey = "First", YFieldKey = "Second", ZFieldKey = "Third", ZInputName = "LegacyThirdInput",
            XBinding = ValueBinding(x), YBinding = ValueBinding(y), ZBinding = ValueBinding(z),
            XTextBinding = TextBinding(x), YTextBinding = TextBinding(y), ZTextBinding = TextBinding(z)
        };
        var first = new Window { Width = 650, Height = 160, Content = vector };
        var second = new Window { Width = 270, Height = 160 };
        first.Show();
        try
        {
            first.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            x.RawText = "invalid X";
            first.Content = null;
            second.Content = vector;
            second.Show();
            second.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var input = vector.GetVisualDescendants().OfType<NumericDraftInput>().Single(input => input.Name == "LegacyThirdInput");
            Assert.Equal("Third", vector.GetInputField(input));
            Assert.Equal("1E-100", input.RawText);
            Assert.True(vector.FocusField("Third"));
            var label = vector.GetVisualDescendants().OfType<NumericDragLabel>().Single(label => ReferenceEquals(label.Input, input));
            var point = label.TranslatePoint(new(label.Bounds.Width / 2, label.Bounds.Height / 2), second)!.Value;
            second.MouseDown(point, MouseButton.Left);
            second.MouseMove(point + new Vector(12, 0));
            Assert.True(input.IsTitleDragging);
            Assert.Equal(12, double.Parse(z.RawText, CultureInfo.CurrentCulture));
            UiTestActions.Press(second, Key.Escape);
            Assert.False(input.IsTitleDragging);
            Assert.Equal("1E-100", z.RawText);
            Assert.Equal("invalid X", x.RawText);
            Assert.Equal("20", y.RawText);
            vector.ZFieldKey = "RelocatedThird";
            Assert.Equal("LegacyThirdInput", input.Name);
            Assert.False(vector.FocusField("Third"));
            Assert.True(vector.FocusField("RelocatedThird"));
            vector.ZIsEnabled = false;
            Assert.False(input.IsEffectivelyEnabled);
            vector.HasZ = false;
            Assert.Equal(2, vector.GetVisualDescendants().OfType<NumericDraftInput>().Count());
            vector.HasZ = true;
            Assert.Equal("1E-100", input.RawText);
            Assert.Equal("invalid X", vector.XText);
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    [AvaloniaFact]
    public void SharedAnimationRowFocusesAndMarksOnlyTheThirdComponent()
    {
        var x = new NumericValueDraft { PreserveDoublePrecision = true };
        var y = new NumericValueDraft { PreserveDoublePrecision = true };
        var z = new NumericValueDraft { PreserveDoublePrecision = true };
        x.Load(10.123456789012345);
        y.Load(20);
        z.Load(30);
        var row = new AnimationPropertyRowControl
        {
            IsVector = true, HasZ = true, X = x, Y = y, Z = z, Label = "Rotation",
            FieldKey = "Rotation", XFieldKey = "First", YFieldKey = "Second", ZFieldKey = "Third",
            ZInputName = "LegacyThirdInput", Error = "Invalid third component", InvalidFieldKey = "Third"
        };
        var window = new Window { Width = 270, Height = 200, Content = row };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var vector = Assert.Single(row.GetVisualDescendants().OfType<VectorDraftInput>());
            var inputs = vector.GetVisualDescendants().OfType<NumericDraftInput>().ToArray();
            var third = inputs.Single(input => input.Name == "LegacyThirdInput");
            Assert.Equal(3, inputs.Length);
            Assert.True(third.PreserveDoublePrecision);
            Assert.True(row.OwnsField("Third"));
            Assert.True(row.FocusField("Third"));
            Assert.Equal("Third", row.GetInputField(third));
            Assert.True(Assert.Single(third.GetVisualDescendants().OfType<TextBox>()).IsFocused);
            Assert.Equal("Invalid third component", Assert.Single(DataValidationErrors.GetErrors(third)!));
            Assert.All(inputs.Where(input => !ReferenceEquals(input, third)), input => Assert.False(DataValidationErrors.GetHasErrors(input)));
            Assert.Equal(10.123456789012345, double.Parse(inputs[0].RawText, CultureInfo.CurrentCulture));
        }
        finally
        {
            window.Close();
        }
    }

    private static Binding ValueBinding(NumericValueDraft draft)
    {
        return new(nameof(NumericValueDraft.Value)) { Source = draft, Mode = BindingMode.TwoWay };
    }

    private static Binding TextBinding(NumericValueDraft draft)
    {
        return new(nameof(NumericValueDraft.RawText)) { Source = draft, Mode = BindingMode.TwoWay };
    }
}
