using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证共享蒙版草稿的实际输入来源和静态枢轴编辑。</summary>
public sealed class SharedMaskDraftOriginUiTests
{
    /// <summary>仅聚焦另一面板不能改变无效输入来源，全局校验仍定位实际输入面板。</summary>
    [AvaloniaTheory]
    [InlineData("effects")]
    [InlineData("masks")]
    public async Task FocusingTheOtherProjectionWithoutTypingKeepsTheActualDraftOriginForGlobalValidation(string origin)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        MountBothPanels(context);
        var row = Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var effectsRow = FindEffectsRow(context.Window.Panels["effects"], row);
        var masksRow = FindMaskRow(context.Window.Panels["masks"], row);
        var originRow = origin == "effects" ? effectsRow : masksRow;
        var otherRow = origin == "effects" ? masksRow : effectsRow;
        var host = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(originRow));
        var otherHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(otherRow));
        var source = session.DocumentSnapshot;
        try
        {
            var text = FocusComponent(host, originRow, false);
            UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
            host.KeyTextInput("unfinished");
            var otherText = FocusComponent(otherHost, otherRow, false);
            Assert.True(otherText.IsFocused);
            Assert.Equal("unfinished", otherText.Text);

            Assert.False(session.TryCommitDrafts());
            Flush(host);
            Flush(otherHost);

            Assert.Equal(origin, context.ViewModel.InvalidPanelId);
            Assert.Equal(row.XFieldKey, context.ViewModel.InvalidFieldKey);
            Assert.True(text.IsFocused);
            Assert.False(otherText.IsFocused);
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            row.Restore(row.XFieldKey);
            row.Restore(row.YFieldKey);
            session.TryCommitDrafts(false);
        }
    }

    /// <summary>六项原生定位仅在蒙版编辑，聚焦特效仍将错误返回蒙版，Esc 只恢复当前字段。</summary>
    [AvaloniaTheory]
    [InlineData("AnchorXInput")]
    [InlineData("AnchorYInput")]
    [InlineData("PivotXInput")]
    [InlineData("PivotYInput")]
    [InlineData("OffsetXInput")]
    [InlineData("OffsetYInput")]
    public async Task StaticPlacementErrorsReturnOnlyToMasksAfterEffectsFocusAndEscapeRestoresOnlyTheCurrentField(string localKey)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask
        {
            TopLeft = new(10, 20), BottomRight = new(400, 300),
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(390, 280) },
            Placement = new() { Anchor = new(0.5, 0.5), Pivot = new(-2, 3), Offset = new(17, 19) }
        });
        MountBothPanels(context);
        var effectsPanel = context.Window.Panels["effects"];
        Assert.DoesNotContain(effectsPanel.GetVisualDescendants().OfType<LayerPositionEditor>(), editor => editor.Name == "MaskPositionEditor");
        var masksPosition = UiTestActions.Find<LayerPositionEditor>(context.Window.Panels["masks"], "MaskPositionEditor");
        Assert.Same(session.MaskEditing.PositionDraft, masksPosition.Draft);
        var host = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(masksPosition));
        var row = Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var effectsRow = FindEffectsRow(effectsPanel, row);
        var otherHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(effectsRow));
        var source = session.DocumentSnapshot;
        var originalText = PositionField(session.MaskEditing.PositionDraft, localKey).RawText;
        var otherKey = localKey.Contains("XInput", StringComparison.Ordinal)
            ? localKey.Replace("XInput", "YInput", StringComparison.Ordinal)
            : localKey.Replace("YInput", "XInput", StringComparison.Ordinal);
        var otherValue = otherKey.StartsWith("Anchor", StringComparison.Ordinal) ? "0.75" : "55";
        try
        {
            var text = FocusPositionField(host, masksPosition, localKey);
            PositionField(session.MaskEditing.PositionDraft, otherKey).RawText = otherValue;
            UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
            host.KeyTextInput("unfinished");
            var otherText = FocusComponent(otherHost, effectsRow, false);
            Assert.True(otherText.IsFocused);
            Assert.Equal("10", otherText.Text);

            Assert.False(session.TryCommitDrafts());
            Flush(host);
            Flush(otherHost);

            Assert.Equal("masks", context.ViewModel.InvalidPanelId);
            Assert.Equal(ClipMaskPositionEditingCoordinator.FieldKey(localKey), context.ViewModel.InvalidFieldKey);
            Assert.True(text.IsFocused);
            Assert.False(otherText.IsFocused);
            Assert.Null(context.ViewModel.Effects.InvalidFieldKey);
            Assert.Null(context.ViewModel.Effects.ValidationError);
            Assert.False(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.True(DataValidationErrors.GetHasErrors(FindPositionField(masksPosition, localKey)));
            Assert.Equal("unfinished", PositionField(session.MaskEditing.PositionDraft, localKey).RawText);
            Assert.Same(source, session.DocumentSnapshot);

            UiTestActions.Press(host, Key.Escape);
            Flush(host);
            Flush(otherHost);

            Assert.Equal(originalText, PositionField(session.MaskEditing.PositionDraft, localKey).RawText);
            Assert.Equal(otherValue, PositionField(session.MaskEditing.PositionDraft, otherKey).RawText);
            Assert.False(DataValidationErrors.GetHasErrors(FindPositionField(masksPosition, localKey)));
            Assert.Null(context.ViewModel.Effects.ValidationError);
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            foreach (var key in new[] { "AnchorXInput", "AnchorYInput", "PivotXInput", "PivotYInput", "OffsetXInput", "OffsetYInput" })
            {
                session.MaskEditing.RestorePositionField(ClipMaskPositionEditingCoordinator.FieldKey(key));
            }
            session.TryCommitDrafts(false);
        }
    }

    private static AegiNext.Desktop.Editing.NumericValueDraft PositionField(AegiNext.Desktop.Editing.LayerPositionDraft draft, string field) => field switch
    {
        "AnchorXInput" => draft.AnchorX,
        "AnchorYInput" => draft.AnchorY,
        "PivotXInput" => draft.PivotX,
        "PivotYInput" => draft.PivotY,
        "OffsetXInput" => draft.OffsetX,
        _ => draft.OffsetY
    };

    private static NumericDraftInput FindPositionField(LayerPositionEditor editor, string field) =>
        Assert.Single(editor.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == field);

    private static TextBox FocusPositionField(Window host, LayerPositionEditor editor, string field)
    {
        var input = FindPositionField(editor, field);
        input.BringIntoView();
        Flush(host);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        Flush(host);
        return text;
    }

    private static void MountBothPanels(MainWindowTestContext context)
    {
        context.ViewModel.Effects.MaskExpanded = true;
        context.Window.Layouts.Activate("masks");
        context.Window.Layouts.Float("masks");
        context.Window.Layouts.Activate("effects");
        Flush(context.Window);
        var masksHost = Assert.Single(context.Window.Layouts.FloatingWindows);
        masksHost.Width = 650;
        masksHost.Height = 1000;
        Flush(masksHost);
    }

    private static AnimationPropertyRowControl FindEffectsRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => ReferenceEquals(control.DataContext, row));

    private static AnimationPropertyRowControl FindMaskRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => control.DataContext is MaskVectorField field && ReferenceEquals(field.Row, row));

    private static NumericDraftInput FindComponent(AnimationPropertyRowControl row, bool y) =>
        Assert.Single(row.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == (y ? row.YInputName : row.XInputName));

    private static TextBox FocusComponent(Window host, AnimationPropertyRowControl row, bool y)
    {
        var input = FindComponent(row, y);
        input.BringIntoView();
        Flush(host);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        Flush(host);
        return text;
    }

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
