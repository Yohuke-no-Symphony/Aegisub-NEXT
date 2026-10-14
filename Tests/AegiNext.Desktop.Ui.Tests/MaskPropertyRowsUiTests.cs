using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证蒙版的几何与原生定位、特效的变换与动画职责及共享几何草稿。</summary>
public sealed class MaskPropertyRowsUiTests
{
    /// <summary>矩形角点共享草稿，蒙版仅编辑几何与原生定位，特效拥有变换与动画入口。</summary>
    [AvaloniaFact]
    public async Task RectangleGeometrySharesDraftsWhilePlacementAndAnimationActionsBelongToTheirOwningPanels()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        var source = session.DocumentSnapshot;
        context.ViewModel.Effects.MaskExpanded = true;
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var effectsHost = new Window { Width = 650, Height = 1100, Content = effects };
        var masksHost = new Window { Width = 650, Height = 1100, Content = masks };
        effectsHost.Show();
        masksHost.Show();
        try
        {
            Flush(effectsHost);
            Flush(masksHost);
            Assert.Equal(new[]
            {
                AnimationProperty.MASK_RECTANGLE_TOP_LEFT,
                AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT,
                AnimationProperty.MASK_POSITION,
                AnimationProperty.MASK_SCALE,
                AnimationProperty.MASK_ROTATION
            }.Order(), context.ViewModel.Effects.MaskRows.Select(row => row.Target.Property).Order());
            var category = UiTestActions.Find<Expander>(effectsHost, "MaskCategory");
            Assert.True(category.IsExpanded);
            Assert.True(context.ViewModel.Effects.HasMask);
            foreach (var row in context.ViewModel.Effects.MaskRows)
            {
                var effectControl = FindEffectsRow(effects, row);
                Assert.Same(row.X, effectControl.X);
                if (row.IsVector)
                {
                    Assert.Same(row.Y, effectControl.Y);
                }
                Assert.True(effectControl.ShowAnimationActions);
                Assert.Equal(row.FieldKey, effectControl.FieldKey);
                if (row.Target.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT))
                {
                    Assert.DoesNotContain(masks.GetVisualDescendants().OfType<AnimationPropertyRowControl>(),
                        control => control.FieldKey == row.FieldKey);
                    continue;
                }
                var maskControl = FindMaskRow(masks, row);
                Assert.Same(row.X, maskControl.X);
                Assert.Same(row.Y, maskControl.Y);
                Assert.False(maskControl.ShowAnimationActions);
                Assert.Null(maskControl.SelectCommand);
                Assert.DoesNotContain(maskControl.GetVisualDescendants().OfType<Button>(),
                    button => button.IsEffectivelyVisible && button.Classes.Contains("animation-property-action"));
                Assert.Equal(row.FieldKey, maskControl.FieldKey);
            }
            Assert.Equal(2, masks.GetVisualDescendants().OfType<AnimationPropertyRowControl>().Count());
            Assert.Equal(new[] { AnimationProperty.MASK_RECTANGLE_TOP_LEFT, AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT },
                context.ViewModel.Masks.GeometryFields.Select(field => field.Target!.Value.Property));
            var position = UiTestActions.Find<LayerPositionEditor>(masks, "MaskPositionEditor");
            Assert.DoesNotContain(effects.GetVisualDescendants().OfType<LayerPositionEditor>(), editor => editor.Name == "MaskPositionEditor");
            Assert.Null(masks.FindControl<Control>("MaskRotationField"));
            Assert.Null(masks.FindControl<Button>("ClearMaskNodeAnimationButton"));
            Assert.True(UiTestActions.Find<Button>(effects, "ClearMaskNodeAnimationButton").IsEffectivelyVisible);
            Assert.DoesNotContain(masks.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == Localization.Get("Workbench.MaskPlacementHint"));
            Assert.Same(session.MaskEditing.PositionDraft, position.Draft);
            Assert.Equal(3, position.GetVisualDescendants().OfType<VectorDraftInput>().Count());
            Assert.False(position.ShowPositionMode);
            Assert.False(position.ShowAutomaticPositionAction);
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            effectsHost.Close();
            masksHost.Close();
        }
    }

    /// <summary>矢量节点属性只跟随明确节点选择，整体变换行保持身份稳定。</summary>
    [AvaloniaFact]
    public async Task VectorRowsExposeOnlyWholeMaskUntilANodeIsExplicitlySelected()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        var source = session.DocumentSnapshot;
        var wholeRows = context.ViewModel.Effects.MaskRows.ToArray();
        Assert.Null(session.SceneEditing.MaskNodeId);
        Assert.True(context.ViewModel.Effects.NeedsMaskNodeSelection);
        Assert.Equal(new[] { AnimationProperty.MASK_POSITION, AnimationProperty.MASK_SCALE, AnimationProperty.MASK_ROTATION }.Order(),
            wholeRows.Select(row => row.Target.Property).Order());
        Assert.Empty(context.ViewModel.Masks.GeometryFields);
        Assert.Empty(context.ViewModel.Masks.NodeFields);

        session.MaskEditing.SelectNode(first.Id);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.ViewModel.Effects.NeedsMaskNodeSelection);
        Assert.Equal(6, context.ViewModel.Effects.MaskRows.Length);
        var nodeRows = context.ViewModel.Effects.MaskRows.Where(row => AnimationPropertyMetadata.IsNodeProperty(row.Target.Property)).ToArray();
        Assert.Equal(new[] { AnimationProperty.MASK_NODE_POSITION, AnimationProperty.MASK_NODE_IN_HANDLE, AnimationProperty.MASK_NODE_OUT_HANDLE }.Order(),
            nodeRows.Select(row => row.Target.Property).Order());
        Assert.All(nodeRows, row => Assert.Equal(first.Id, row.Target.NodeId));
        foreach (var field in context.ViewModel.Masks.NodeFields)
        {
            var shared = Assert.Single(nodeRows, row => row.Target == field.Target);
            Assert.Same(shared, field.Row);
            Assert.Same(shared.X, field.X.Draft);
            Assert.Same(shared.Y, field.Y.Draft);
        }
        Assert.All(wholeRows, row => Assert.Contains(context.ViewModel.Effects.MaskRows, candidate => ReferenceEquals(candidate, row)));

        session.MaskEditing.SelectNode(second.Id);
        Dispatcher.UIThread.RunJobs();

        Assert.All(context.ViewModel.Effects.MaskRows.Where(row => AnimationPropertyMetadata.IsNodeProperty(row.Target.Property)),
            row => Assert.Equal(second.Id, row.Target.NodeId));
        Assert.Same(source, session.DocumentSnapshot);
    }

    /// <summary>文字片段和启用状态选择不改变蒙版的整字幕普通状态目标。</summary>
    [AvaloniaFact]
    public async Task MaskRowsRemainWholeSubtitleNormalTargetsWhenATextRangeAndActiveAppearanceAreSelected()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(400, 300) });
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        session.Editor.SetSubtitleAnimationRange(session.SelectedCue!.Id, range);
        context.ViewModel.Effects.Target = new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        var source = session.DocumentSnapshot;

        Assert.True(context.ViewModel.Effects.CanEditMask);
        Assert.Equal(5, context.ViewModel.Effects.MaskRows.Length);
        Assert.All(context.ViewModel.Effects.MaskRows, row =>
        {
            Assert.Null(row.Target.TextRangeId);
            Assert.Equal(SubtitleAnimationState.NORMAL, row.Target.State);
            Assert.Equal(id, row.LayerId);
        });
        var rotation = Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_ROTATION);
        rotation.SelectCommand.Execute(null);

        Assert.Equal(rotation.Target, context.ViewModel.Effects.Target);
        Assert.Null(session.SceneEditing.Target.TextRangeId);
        Assert.Equal(SubtitleAnimationState.NORMAL, session.SceneEditing.Target.State);
        Assert.Same(source, session.DocumentSnapshot);
    }

    /// <summary>特效实际按钮创建首帧、添加和重置关键帧，几何草稿同步到无动画入口的蒙版行。</summary>
    [AvaloniaTheory]
    [InlineData(AnimationProperty.MASK_RECTANGLE_TOP_LEFT)]
    [InlineData(AnimationProperty.MASK_POSITION)]
    [InlineData(AnimationProperty.MASK_ROTATION)]
    public async Task EffectsAnimationButtonsAddAndResetKeyframesWhileMaskGeometryStaysSynchronized(AnimationProperty property)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(400, 300) });
        context.ViewModel.Effects.MaskExpanded = true;
        var row = Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == property);
        using var effects = new EffectsPanelView(context.ViewModel.Effects, session);
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var effectsHost = new Window { Width = 650, Height = 1100, Content = effects };
        var masksHost = new Window { Width = 650, Height = 1100, Content = masks };
        effectsHost.Show();
        masksHost.Show();
        try
        {
            Flush(effectsHost);
            Flush(masksHost);
            var effectsRow = FindEffectsRow(effects, row);
            var effectsAdd = FindAction(effectsRow, row.AddKeyframeCommand);
            var effectsReset = FindAction(effectsRow, row.ResetCommand);
            Assert.False(effectsRow.IsAnimated);
            Assert.True(effectsAdd.IsEffectivelyEnabled);
            Assert.DoesNotContain(effectsRow.GetVisualDescendants().OfType<ToggleButton>(), button => button.Command == row.ToggleAnimationCommand);
            Assert.False(effectsReset.IsEnabled);

            await ClickActionAsync(effectsHost, effectsAdd);

            Assert.True(row.IsAnimated);
            Assert.True(effectsRow.IsAnimated);
            Assert.True(effectsReset.IsEnabled);
            Assert.Single(Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes);
            session.ClearKeyframeSelection();
            await session.SeekFromUserAsync(new(1));
            Flush(effectsHost);
            Flush(masksHost);
            await ClickActionAsync(effectsHost, FindAction(effectsRow, row.AddKeyframeCommand));
            Assert.Equal(2, Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes.Length);
            row.BeginEdit("effects");
            row.X.RawText = "45";
            if (row.IsVector)
            {
                row.Y.RawText = "60";
            }
            Assert.True(session.TryCommitDrafts());
            Assert.Equal(45, Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes[1].Value.GetComponent(0));
            if (property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT)
            {
                var maskRow = FindMaskRow(masks, row);
                Assert.Equal("45", maskRow.X!.RawText);
                Assert.Equal("60", maskRow.Y!.RawText);
                Assert.False(maskRow.ShowAnimationActions);
            }

            effectsRow = FindEffectsRow(effects, row);
            await ClickActionAsync(effectsHost, FindAction(effectsRow, row.ResetCommand));

            var reset = Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes[1];
            Assert.Equal(0, reset.Value.GetComponent(0));
            Assert.Equal("0", effectsRow.X!.RawText);
            Assert.Equal(2, Assert.Single(session.SelectedLayer.Tracks, track => track.Target == row.Target).Keyframes.Length);
            if (property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT)
            {
                Assert.Equal("0", FindMaskRow(masks, row).X!.RawText);
            }
        }
        finally
        {
            effectsHost.Close();
            masksHost.Close();
        }
    }

    /// <summary>几何标题仅呈现字段名称，实际点击不改变特效目标、所选关键帧或工程。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickingStaticGeometryTitlesPreservesTheSelectedAnimationTargetAndKeyframe(bool vectorMask)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var node = new MaskNode { Position = new(10, 20) };
        session.Editor.SetClipMask(layerId, vectorMask
            ? new VectorClipMask { Contours = [new() { Nodes = [node] }] }
            : new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        if (vectorMask)
        {
            session.MaskEditing.SelectNode(node.Id);
        }
        var target = new AnimationTrackTarget(AnimationProperty.OPACITY);
        session.Editor.SetKeyframe(layerId, target, new(new(1), 0.5));
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        using var masks = new MaskPanelView(context.ViewModel.Masks, session);
        var host = new Window { Width = 650, Height = 1100, Content = masks };
        host.Show();
        try
        {
            Flush(host);
            var source = session.DocumentSnapshot;
            var keyTime = session.SelectedKeyTime;
            var rows = masks.GetVisualDescendants().OfType<AnimationPropertyRowControl>().ToArray();
            Assert.Equal(vectorMask ? 3 : 2, rows.Length);
            foreach (var row in rows)
            {
                Assert.Null(row.SelectCommand);
                Assert.False(row.ShowAnimationActions);
                var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.IsEffectivelyVisible && text.Text == row.Label);
                Assert.DoesNotContain(title.GetVisualAncestors(), ancestor => ancestor is Button);
                Assert.Contains("field", title.Classes);
                title.BringIntoView();
                Flush(host);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = host.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Flush(host);
                var point = title.TranslatePoint(new Point(Math.Min(title.DesiredSize.Width, title.Bounds.Width) / 2,
                    title.Bounds.Height / 2), host)!.Value;
                var hit = host.InputHitTest(point);
                Assert.True(hit is Visual visual && visual.GetVisualAncestors().Contains(masks),
                    $"Label={row.Label}; Bounds={title.Bounds}; Point={point}; Hit={hit?.GetType().Name}");
                host.MouseDown(point, MouseButton.Left);
                host.MouseUp(point, MouseButton.Left);
                Flush(host);
                Assert.Equal(target, context.ViewModel.Effects.Target);
                Assert.Equal(target, session.SceneEditing.Target);
                Assert.Equal(keyTime, session.SelectedKeyTime);
                Assert.Null(session.AnimationOptionsTarget);
                Assert.Same(source, session.DocumentSnapshot);
            }
        }
        finally
        {
            host.Close();
        }
    }

    private static AnimationPropertyRowControl FindEffectsRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => ReferenceEquals(control.DataContext, row));

    private static AnimationPropertyRowControl FindMaskRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => control.DataContext switch
        {
            MaskVectorField field => ReferenceEquals(field.Row, row),
            MaskNumericField field => ReferenceEquals(field.Row, row),
            _ => false
        });

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
