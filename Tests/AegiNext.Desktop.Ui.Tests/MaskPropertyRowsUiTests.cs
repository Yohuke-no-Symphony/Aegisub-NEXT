using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
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

/// <summary>验证特效和蒙版面板共享蒙版属性及动画操作入口。</summary>
public sealed class MaskPropertyRowsUiTests
{
    /// <summary>矩形角点和整体变换在两处使用同一属性行与草稿，枢轴保持静态编辑。</summary>
    [AvaloniaFact]
    public async Task RectanglePropertyRowsShareTheirDraftsAcrossBothPanelsAndPivotHasNoAnimationActions()
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
                var maskControl = FindMaskRow(masks, row);
                Assert.Same(row.X, effectControl.X);
                Assert.Same(row.X, maskControl.X);
                if (row.IsVector)
                {
                    Assert.Same(row.Y, effectControl.Y);
                    Assert.Same(row.Y, maskControl.Y);
                }
                Assert.True(effectControl.ShowAnimationActions);
                Assert.True(maskControl.ShowAnimationActions);
                Assert.Equal(row.FieldKey, effectControl.FieldKey);
                Assert.Equal(row.FieldKey, maskControl.FieldKey);
            }
            var position = UiTestActions.Find<LayerPositionEditor>(masks, "MaskPositionEditor");
            var effectPosition = UiTestActions.Find<LayerPositionEditor>(effects, "MaskPositionEditor");
            Assert.Same(position.Draft, effectPosition.Draft);
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

    /// <summary>两面板实际按钮同步创建首帧、添加关键帧和重置当前帧的结果。</summary>
    [AvaloniaTheory]
    [InlineData(AnimationProperty.MASK_POSITION)]
    [InlineData(AnimationProperty.MASK_ROTATION)]
    public async Task ActualAnimationButtonsSynchronizeFirstKeyframeAddAndResetAcrossBothPanels(AnimationProperty property)
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
            var maskRow = FindMaskRow(masks, row);
            var effectsAdd = FindAction(effectsRow, row.AddKeyframeCommand);
            var maskAdd = FindAction(maskRow, row.AddKeyframeCommand);
            var effectsReset = FindAction(effectsRow, row.ResetCommand);
            Assert.False(effectsRow.IsAnimated);
            Assert.False(maskRow.IsAnimated);
            Assert.True(effectsAdd.IsEffectivelyEnabled);
            Assert.True(maskAdd.IsEffectivelyEnabled);
            Assert.DoesNotContain(effectsRow.GetVisualDescendants().OfType<ToggleButton>(), button => button.Command == row.ToggleAnimationCommand);
            Assert.DoesNotContain(maskRow.GetVisualDescendants().OfType<ToggleButton>(), button => button.Command == row.ToggleAnimationCommand);
            Assert.False(effectsReset.IsEnabled);

            await ClickActionAsync(masksHost, maskAdd);

            Assert.True(row.IsAnimated);
            Assert.True(effectsRow.IsAnimated);
            Assert.True(FindMaskRow(masks, row).IsAnimated);
            Assert.True(effectsReset.IsEnabled);
            Assert.Single(Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes);
            session.ClearKeyframeSelection();
            await session.SeekFromUserAsync(new(1));
            Flush(effectsHost);
            Flush(masksHost);
            await ClickActionAsync(effectsHost, FindAction(effectsRow, row.AddKeyframeCommand));
            Assert.Equal(2, Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes.Length);
            row.BeginEdit("masks");
            row.X.RawText = "45";
            if (row.IsVector)
            {
                row.Y.RawText = "60";
            }
            Assert.True(session.TryCommitDrafts());
            Assert.Equal(45, Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes[1].Value.GetComponent(0));

            effectsRow = FindEffectsRow(effects, row);
            await ClickActionAsync(effectsHost, FindAction(effectsRow, row.ResetCommand));

            var reset = Assert.Single(session.SelectedLayer!.Tracks, track => track.Target == row.Target).Keyframes[1];
            Assert.Equal(0, reset.Value.GetComponent(0));
            Assert.Equal("0", effectsRow.X!.RawText);
            Assert.Equal("0", FindMaskRow(masks, row).X!.RawText);
            Assert.Equal(2, Assert.Single(session.SelectedLayer.Tracks, track => track.Target == row.Target).Keyframes.Length);
            Assert.True(FindMaskRow(masks, row).IsAnimated);
        }
        finally
        {
            effectsHost.Close();
            masksHost.Close();
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
