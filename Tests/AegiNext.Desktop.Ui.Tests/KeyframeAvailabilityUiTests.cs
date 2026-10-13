using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Media.Playback;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证播放时钟持续推进时关键帧编辑入口的稳定性和边界。</summary>
public sealed class KeyframeAvailabilityUiTests
{
    /// <summary>播放与暂停均保持片段内的关键帧按钮启用，刷新不修改工程。</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task PlaybackWithinClipKeepsThePropertyKeyframeButtonEnabled(int animationOffset)
    {
        await using var context = new MainWindowTestContext(playbackTimeProvider: new AdvancingPlaybackTimeProvider());
        await context.OpenMediaAsync();
        SelectSubtitle(context, animationOffset);
        await context.Session.SeekForEditingAsync(new(2));
        var button = GetKeyframeButton(context);
        Assert.True(button.IsEnabled);
        var source = context.Session.DocumentSnapshot;
        var transitions = new List<bool>();
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty)
            {
                transitions.Add(button.IsEnabled);
            }
        };

        await context.Controller.PlayAsync();
        for (var tick = 0; tick < 20; tick++)
        {
            context.Session.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.True(button.IsEnabled);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        Assert.Empty(transitions);

        await context.Controller.PauseAsync();
        context.Session.Tick();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.True(button.IsEnabled);
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    /// <summary>播放头越过片段边界时属性按钮切换一次，端点仍可保存关键帧。</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-3)]
    public async Task PlaybackCrossingClipBoundsChangesThePropertyButtonOnlyAtTheBoundary(int animationOffset)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        SelectSubtitle(context, animationOffset);
        await context.Session.SeekForEditingAsync(new(1, 2));
        var button = GetKeyframeButton(context);
        Assert.False(button.IsEnabled);
        var source = context.Session.DocumentSnapshot;
        var transitions = new List<bool>();
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty)
            {
                transitions.Add(button.IsEnabled);
            }
        };

        await context.Controller.PlayAsync();
        foreach (var (elapsed, enabled) in new (TimeSpan Elapsed, bool Enabled)[]
                 {
                     (TimeSpan.Zero, false), (TimeSpan.FromMilliseconds(500), true),
                     (TimeSpan.FromSeconds(1), true), (TimeSpan.FromSeconds(6), true),
                     (TimeSpan.FromMilliseconds(1), false)
                 })
        {
            context.Clock.Advance(elapsed);
            context.Session.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.Equal(enabled, button.IsEnabled);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        Assert.Equal([true, false], transitions);
    }

    /// <summary>按钮遵守片段边界和动画偏移，并拒绝有序变换的关键帧编辑。</summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-3)]
    public async Task KeyframeButtonRespectsClipBoundsAndOrderedTransforms(int animationOffset)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        SelectSubtitle(context, animationOffset);
        var button = GetKeyframeButton(context);
        var source = context.Session.DocumentSnapshot;
        foreach (var (time, enabled) in new (MediaTime Time, bool Enabled)[]
                 {
                     (new(1, 2), false), (new(1), true), (new(2), true), (new(8), true), (new(8001, 1000), false)
                 })
        {
            await context.Session.SeekForEditingAsync(time);
            context.Session.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(enabled, button.IsEnabled);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }

        await context.Session.SeekForEditingAsync(new(2));
        var target = new AnimationTrackTarget(AnimationProperty.FONT_SIZE);
        context.Session.Editor.SetAnimationTransform(context.Session.SelectedLayer!.Id, target,
            context.Session.SelectedCue!.Style.FontSize,
            new(Guid.NewGuid(), new(animationOffset), new(animationOffset + 1), 64));
        context.Session.Tick();
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEnabled);
        Assert.True(context.ViewModel.Effects.IsOrderedTransform);
    }

    /// <summary>没有主选片段时禁用原属性按钮，并移除逐属性入口。</summary>
    [AvaloniaFact]
    public async Task KeyframeButtonIsDisabledWithoutSelectedClip()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        SelectSubtitle(context, 0);
        await context.Session.SeekForEditingAsync(new(2));
        var button = GetKeyframeButton(context);
        Assert.True(button.IsEnabled);
        var source = context.Session.DocumentSnapshot;

        Assert.True(context.Session.SelectTrack(context.Session.SelectedLayer!.TrackId));
        context.Session.Tick();
        Dispatcher.UIThread.RunJobs();

        Assert.False(button.IsEnabled);
        Assert.Empty(context.ViewModel.Effects.TypographyRows);
        Assert.Same(source, context.Session.DocumentSnapshot);
    }

    private static void SelectSubtitle(MainWindowTestContext context, int animationOffset)
    {
        var id = context.Session.Editor.AddSubtitle(new(1), new(8), "Keyframe availability 中文");
        context.Session.SelectCue(id);
        context.Session.Editor.UpdateLayer(context.Session.SelectedLayer!.Id, layer => layer with { AnimationOffset = new(animationOffset) });
        context.Window.Layouts.Activate("effects");
        context.ViewModel.Effects.Target = new(AnimationProperty.FONT_SIZE);
        context.ViewModel.Effects.TypographyExpanded = true;
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static Button GetKeyframeButton(MainWindowTestContext context)
    {
        var row = context.ViewModel.Effects.TypographyRows.Single(row => row.Target.Property == AnimationProperty.FONT_SIZE);
        var view = context.Window.Panels["effects"].GetVisualDescendants().OfType<EffectPropertyRowView>()
            .Single(view => ReferenceEquals(view.DataContext, row));
        return view.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, row.AddKeyframeCommand));
    }
}
