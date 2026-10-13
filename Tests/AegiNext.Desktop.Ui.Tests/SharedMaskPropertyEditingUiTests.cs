using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证两处共享蒙版编辑的错误定位、事务、目标身份和拖拽生命周期。</summary>
public sealed class SharedMaskPropertyEditingUiTests
{
    /// <summary>键盘无效输入在两面板同步错误，定位到输入来源，Esc仅恢复当前分量。</summary>
    [AvaloniaTheory]
    [InlineData("effects")]
    [InlineData("masks")]
    public async Task InvalidKeyboardDraftSharesErrorsFocusesItsOriginAndEscapeRestoresOnlyTheCurrentComponent(string origin)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        context.ViewModel.Effects.MaskExpanded = true;
        context.Window.Layouts.Activate("masks");
        context.Window.Layouts.Float("masks");
        context.Window.Layouts.Activate("effects");
        Flush(context.Window);
        var masksHost = Assert.Single(context.Window.Layouts.FloatingWindows);
        masksHost.Width = 600;
        masksHost.Height = 1000;
        Flush(masksHost);
        var effects = context.Window.Panels["effects"];
        var masks = context.Window.Panels["masks"];
        var row = GetRow(context, AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var effectsRow = FindEffectsRow(effects, row);
        var masksRow = FindMaskRow(masks, row);
        var source = session.DocumentSnapshot;
        try
        {
            row.Y.RawText = "55";
            var originRow = origin == "effects" ? effectsRow : masksRow;
            var host = origin == "effects" ? context.Window : masksHost;
            var input = FindComponent(originRow, false);
            input.BringIntoView();
            Flush(host);
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
            host.KeyTextInput("unfinished");
            UiTestActions.Press(host, Key.Enter);
            Flush(host);

            Assert.Equal("unfinished", effectsRow.X!.RawText);
            Assert.Equal("unfinished", masksRow.X!.RawText);
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Equal(origin, context.ViewModel.InvalidPanelId);
            Assert.Equal(row.XFieldKey, context.ViewModel.InvalidFieldKey);
            Assert.False(session.TryCommitDrafts());
            Flush(host);
            Assert.True(text.IsFocused);
            Assert.Same(source, session.DocumentSnapshot);

            UiTestActions.Press(host, Key.Escape);
            Flush(host);

            Assert.Equal("10", row.X.RawText);
            Assert.Equal("55", row.Y.RawText);
            Assert.Equal("10", effectsRow.X.RawText);
            Assert.Equal("10", masksRow.X.RawText);
            Assert.False(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.False(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            row.Restore(row.XFieldKey);
            row.Restore(row.YFieldKey);
            session.TryCommitDrafts(false);
        }
    }

    /// <summary>两面板编辑不同分量时预览合并，提交一次，撤销一次即可恢复。</summary>
    [AvaloniaFact]
    public async Task EditingBothPanelProjectionsCommitsOneMaskChangeAndOneUndoRestoresTheSource()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var effectsHost = new Window { Width = 650, Height = 1000, Content = effects };
        var masksHost = new Window { Width = 650, Height = 1000, Content = masks };
        effectsHost.Show();
        masksHost.Show();
        var row = GetRow(context, AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        try
        {
            Flush(effectsHost);
            Flush(masksHost);
            var effectsRow = FindEffectsRow(effects, row);
            var masksRow = FindMaskRow(masks, row);
            var source = session.DocumentSnapshot;
            row.BeginEdit("effects");
            effectsRow.X!.RawText = "35";
            row.BeginEdit("masks");
            masksRow.Y!.RawText = "65";

            Assert.Equal(new ScenePoint(35, 65), Assert.IsType<RectangleClipMask>(session.PreviewDocument.Layers.Single(layer => layer.Id == row.LayerId).Mask).TopLeft);
            Assert.Same(source, session.DocumentSnapshot);
            Assert.True(session.TryCommitDrafts());
            Assert.Equal(new ScenePoint(35, 65), Assert.IsType<RectangleClipMask>(session.SelectedLayer!.Mask).TopLeft);
            Assert.False(session.PropertyEditing.HasDrafts);
            Assert.Equal("Commit workspace drafts", session.Editor.UndoLabel);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
            Flush(effectsHost);
            Flush(masksHost);
            Assert.Equal("10", effectsRow.X.RawText);
            Assert.Equal("20", FindMaskRow(masks, row).Y!.RawText);
        }
        finally
        {
            row.Restore(row.XFieldKey);
            row.Restore(row.YFieldKey);
            effectsHost.Close();
            masksHost.Close();
        }
    }

    /// <summary>无效节点草稿阻止选择切换，合法切换提交原节点而不污染新节点。</summary>
    [AvaloniaFact]
    public async Task InvalidNodeDraftBlocksListSelectionAndAValidSwitchCommitsOnlyTheOriginalNode()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        session.MaskEditing.SelectNode(first.Id);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var host = new Window { Width = 650, Height = 1100, Content = masks };
        host.Show();
        var firstRow = GetRow(context, AnimationProperty.MASK_NODE_POSITION);
        try
        {
            Flush(host);
            var list = UiTestActions.Find<ListBox>(host, "MaskPointList");
            var source = session.DocumentSnapshot;
            firstRow.BeginEdit("masks");
            firstRow.X.RawText = "unfinished";
            list.SelectedItem = context.ViewModel.Masks.Points.Single(point => point.Id == second.Id);
            Flush(host);

            Assert.Equal(first.Id, session.SceneEditing.MaskNodeId);
            Assert.Equal(first.Id, context.ViewModel.Masks.SelectedPoint!.Id);
            Assert.Equal("unfinished", firstRow.X.RawText);
            Assert.Same(source, session.DocumentSnapshot);
            firstRow.Restore(firstRow.XFieldKey);
            firstRow.BeginEdit("masks");
            firstRow.X.RawText = "75";
            list.SelectedItem = context.ViewModel.Masks.Points.Single(point => point.Id == second.Id);
            Flush(host);

            Assert.Equal(second.Id, session.SceneEditing.MaskNodeId);
            var secondRow = GetRow(context, AnimationProperty.MASK_NODE_POSITION);
            Assert.NotEqual(firstRow.FieldKey, secondRow.FieldKey);
            Assert.Equal("30", secondRow.X.RawText);
            Assert.Equal("40", secondRow.Y.RawText);
            var nodes = Assert.IsType<VectorClipMask>(session.SelectedLayer!.Mask).Contours[0].Nodes;
            Assert.Equal(new ScenePoint(75, 20), nodes[0].Position);
            Assert.Equal(second.Position, nodes[1].Position);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            firstRow.Restore(firstRow.XFieldKey);
            firstRow.Restore(firstRow.YFieldKey);
            host.Close();
        }
    }

    /// <summary>有序变换属性禁用直接修改并通过属性弹窗保留原操作身份。</summary>
    [AvaloniaFact]
    public async Task OrderedMaskRowsDisableDirectValueAddAndResetAndPopupPreservesOperationIdentity()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(400, 300) });
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), new ScenePoint(50, 60), 2);
        session.Editor.SetAnimationTransform(id, target, new ScenePoint(0, 0), operation);
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var effectsHost = new Window { Width = 650, Height = 1000, Content = effects };
        var masksHost = new Window { Width = 650, Height = 1000, Content = masks };
        effectsHost.Show();
        masksHost.Show();
        try
        {
            Flush(effectsHost);
            Flush(masksHost);
            var row = GetRow(context, target.Property);
            var effectsRow = FindEffectsRow(effects, row);
            var masksRow = FindMaskRow(masks, row);
            var source = session.DocumentSnapshot;
            foreach (var control in new[] { effectsRow, masksRow })
            {
                Assert.False(control.CanEdit);
                Assert.False(FindComponent(control, false).IsEffectivelyEnabled);
                Assert.False(FindAction(control, row.AddKeyframeCommand).IsEffectivelyEnabled);
                Assert.False(FindAction(control, row.ResetCommand).IsEffectivelyEnabled);
                Assert.True(control.FindControl<Button>("AnimationOptionsButton")!.IsEffectivelyVisible);
            }

            var button = masksRow.FindControl<Button>("AnimationOptionsButton")!;
            await ClickActionAsync(masksHost, button);

            Assert.Equal(target, context.ViewModel.Effects.Target);
            Assert.True(button.Flyout!.IsOpen);
            var popup = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(button.Flyout).Content);
            Assert.True(UiTestActions.Find<VectorDraftInput>(popup, "OperationVectorInput").IsEffectivelyVisible);
            Assert.True(UiTestActions.Find<ComboBox>(popup, "TransformOperationCombo").IsEffectivelyVisible);
            Assert.Equal("masks", session.AnimationOptionsPanelId);
            Assert.Equal(operation.Id, context.ViewModel.Effects.SelectedOperation!.Id);
            var track = Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == target);
            Assert.Empty(track.Keyframes);
            Assert.Equal(operation, Assert.Single(track.Transforms));
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            effectsHost.Close();
            masksHost.Close();
        }
    }

    /// <summary>蒙版缩放标题以百分之一步长拖拽，预览不写工程，提交一次并支持取消。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaskScaleTitleUsesOneHundredthStepAndSupportsSingleUndoAndEscapeCancellation(bool fromMasks)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask { BottomRight = new(400, 300) });
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var panel = fromMasks ? (Control)masks : effects;
        var host = new Window { Width = 650, Height = 1100, Content = panel };
        host.Show();
        try
        {
            Flush(host);
            var row = GetRow(context, AnimationProperty.MASK_SCALE);
            var control = fromMasks ? FindMaskRow(panel, row) : FindEffectsRow(panel, row);
            var vector = Assert.Single(control.GetVisualDescendants().OfType<VectorDraftInput>());
            vector.BringIntoView();
            Flush(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var title = Assert.Single(vector.GetVisualDescendants().OfType<NumericDragLabel>(), title => title.Text == "X");
            Assert.Equal(0.01m, title.Input!.Increment);
            var source = session.DocumentSnapshot;
            var point = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(8, 0));
            Assert.Same(source, session.DocumentSnapshot);
            Assert.Equal(1.08m, vector.X);
            host.MouseUp(point + new Vector(8, 0), MouseButton.Left);
            Flush(host);
            Assert.Equal(1.08, session.SelectedLayer!.Mask!.Transform.Scale.X, 5);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
            Flush(host);
            control = fromMasks ? FindMaskRow(panel, row) : FindEffectsRow(panel, row);
            vector = Assert.Single(control.GetVisualDescendants().OfType<VectorDraftInput>());
            vector.BringIntoView();
            Flush(host);
            title = Assert.Single(vector.GetVisualDescendants().OfType<NumericDragLabel>(), title => title.Text == "X");
            point = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(15, 0));
            UiTestActions.Press(host, Key.Escape);
            host.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            Flush(host);
            Assert.Equal("1", row.X.RawText);
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            host.MouseUp(default, MouseButton.Left);
            effects.CancelGestures();
            masks.CancelGestures();
            host.Close();
        }
    }

    /// <summary>标题拖拽尚未释放时切换节点会取消原手势，释放鼠标不会写入任一节点。</summary>
    [AvaloniaFact]
    public async Task SwitchingNodesDuringAHeldTitleDragCancelsTheGestureBeforeChangingTheTarget()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        session.MaskEditing.SelectNode(first.Id);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var host = new Window { Width = 650, Height = 1100, Content = masks };
        host.Show();
        try
        {
            Flush(host);
            var firstRow = GetRow(context, AnimationProperty.MASK_NODE_POSITION);
            var control = FindMaskRow(masks, firstRow);
            var vector = Assert.Single(control.GetVisualDescendants().OfType<VectorDraftInput>());
            vector.BringIntoView();
            Flush(host);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var title = Assert.Single(vector.GetVisualDescendants().OfType<NumericDragLabel>(), title => title.Text == "X");
            var point = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
            var source = session.DocumentSnapshot;
            host.MouseDown(point, MouseButton.Left);
            host.MouseMove(point + new Vector(15, 0));
            Assert.True(title.Input!.IsTitleDragging);
            Assert.Equal("25", firstRow.X.RawText);
            Assert.Same(source, session.DocumentSnapshot);

            session.MaskEditing.SelectNode(second.Id);
            Flush(host);
            host.MouseUp(point + new Vector(15, 0), MouseButton.Left);
            Flush(host);

            Assert.False(title.Input.IsTitleDragging);
            Assert.Equal(second.Id, session.SceneEditing.MaskNodeId);
            Assert.Equal("10", firstRow.X.RawText);
            var selectedRow = GetRow(context, AnimationProperty.MASK_NODE_POSITION);
            Assert.Equal(second.Id, selectedRow.Target.NodeId);
            Assert.Equal("30", selectedRow.X.RawText);
            Assert.False(session.PropertyEditing.HasDrafts);
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            host.MouseUp(default, MouseButton.Left);
            masks.CancelGestures();
            host.Close();
        }
    }

    /// <summary>折叠、语言及主题切换保留原始草稿、共享身份和工程快照。</summary>
    [AvaloniaFact]
    public async Task CollapseLanguageAndThemeChangesPreserveSharedInvalidDraftWithoutWritingTheProject()
    {
        await using var context = new MainWindowTestContext();
        var originalLanguage = Localization.SelectedLanguageID;
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask { BottomRight = new(400, 300) });
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var effectsHost = new Window { Width = 384, Height = 1000, Content = effects };
        var masksHost = new Window { Width = 384, Height = 1000, Content = masks };
        effectsHost.Show();
        masksHost.Show();
        var row = GetRow(context, AnimationProperty.MASK_ROTATION);
        var source = session.DocumentSnapshot;
        try
        {
            row.BeginEdit("effects");
            row.X.RawText = "unfinished";
            foreach (var language in new[] { "en-US", "zh-CN", "ja-JP" })
            {
                Localization.SetLanguage(language);
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    context.ViewModel.Effects.MaskExpanded = false;
                    effectsHost.RequestedThemeVariant = theme;
                    masksHost.RequestedThemeVariant = theme;
                    Flush(effectsHost);
                    Flush(masksHost);
                    context.ViewModel.Effects.MaskExpanded = true;
                    Flush(effectsHost);
                    Assert.Same(row, GetRow(context, AnimationProperty.MASK_ROTATION));
                    Assert.Same(row.X, FindEffectsRow(effects, row).X);
                    Assert.Same(row.X, FindMaskRow(masks, row).X);
                    Assert.Equal("unfinished", row.X.RawText);
                    Assert.Equal(Localization.Get("Workbench.MASK_ROTATION"), FindEffectsRow(effects, row).Label);
                    Assert.Same(source, session.DocumentSnapshot);
                }
            }
        }
        finally
        {
            row.Restore(row.XFieldKey);
            Localization.SetLanguage(originalLanguage);
            effectsHost.Close();
            masksHost.Close();
        }
    }

    /// <summary>深浅主题的宽窄面板渲染共享蒙版行，按既有环境变量输出验收截图。</summary>
    [AvaloniaTheory]
    [InlineData(260, false)]
    [InlineData(260, true)]
    [InlineData(384, false)]
    [InlineData(384, true)]
    public async Task MaskPropertyRowsFitNarrowAndNormalPanelsAndCaptureBothThemes(int width, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var node = new MaskNode { Position = new(10, 20) };
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new VectorClipMask { Contours = [new() { Nodes = [node] }] });
        session.MaskEditing.SelectNode(node.Id);
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var effectsHost = new Window { Width = width, Height = 1100, Content = effects, RequestedThemeVariant = theme };
        var masksHost = new Window { Width = width, Height = 1100, Content = masks, RequestedThemeVariant = theme };
        effectsHost.Show();
        masksHost.Show();
        var source = session.DocumentSnapshot;
        try
        {
            foreach (var (host, panel, name) in new[] { (effectsHost, (Control)effects, "effects"), (masksHost, (Control)masks, "masks") })
            {
                Flush(host);
                var row = GetRow(context, AnimationProperty.MASK_NODE_POSITION);
                var control = name == "effects" ? FindEffectsRow(panel, row) : FindMaskRow(panel, row);
                control.BringIntoView();
                Flush(host);
                Assert.True(control.Bounds.Width > 0);
                Assert.True(control.Bounds.Width <= panel.Bounds.Width);
                var input = FindComponent(control, false);
                Assert.True(input.Bounds.Width >= 32);
                var position = input.TranslatePoint(default, control)!.Value;
                Assert.True(position.X >= 0);
                Assert.True(position.X + input.Bounds.Width <= control.Bounds.Width + 1);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                if (Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY") is { Length: > 0 } directory)
                {
                    Assert.True(Path.IsPathFullyQualified(directory));
                    Directory.CreateDirectory(directory);
                    frame.Save(Path.Combine(directory, $"mask-property-rows-{name}-{width}-{theme.Key}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            effectsHost.Close();
            masksHost.Close();
        }
    }

    private static AnimationPropertyRowViewModel GetRow(MainWindowTestContext context, AnimationProperty property) =>
        Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == property);

    private static AnimationPropertyRowControl FindEffectsRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => ReferenceEquals(control.DataContext, row));

    private static AnimationPropertyRowControl FindMaskRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => control.DataContext switch
        {
            MaskVectorField field => ReferenceEquals(field.Row, row),
            MaskNumericField field => ReferenceEquals(field.Row, row),
            _ => false
        });

    private static NumericDraftInput FindComponent(AnimationPropertyRowControl row, bool y) =>
        Assert.Single(row.GetVisualDescendants().OfType<NumericDraftInput>(), input =>
            input.Name == (row.IsScalar ? row.ScalarInputName : y ? row.YInputName : row.XInputName));

    private static Button FindAction(AnimationPropertyRowControl row, System.Windows.Input.ICommand command) =>
        Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => button.Command == command);

    private static async Task ClickActionAsync(Window host, Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.Same(host, TopLevel.GetTopLevel(button));
        button.BringIntoView();
        Flush(host);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = host.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Flush(host);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), host)!.Value;
        var hit = host.InputHitTest(point);
        Assert.True(hit is Visual visual && (ReferenceEquals(visual, button) || visual.GetVisualAncestors().Contains(button)),
            $"Button={button.Name}; Bounds={button.Bounds}; Point={point}; Hit={hit?.GetType().Name}; Attached={button.IsAttachedToVisualTree()}; Host={host.Bounds}; Visible={button.IsEffectivelyVisible}");
        host.MouseDown(point, MouseButton.Left);
        host.MouseUp(point, MouseButton.Left);
        if (button.Command is IAsyncRelayCommand { ExecutionTask: { } execution })
        {
            await execution;
        }
        Flush(host);
    }

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
