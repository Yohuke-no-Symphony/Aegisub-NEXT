using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleListAuditionUiTests
{
    [AvaloniaTheory]
    [InlineData(Key.Q, 1500, 2000)]
    [InlineData(Key.W, 3000, 3500)]
    [InlineData(Key.E, 2000, 2500)]
    [InlineData(Key.R, 2000, 3000)]
    public async Task SelectedRowHeaderUsesQwerForExactAudioSamplesAndLinkedVideo(Key key, int startMs, int endMs)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        var focused = Assert.IsType<ListBoxItem>(context.Window.FocusManager?.GetFocusedElement());

        Press(context.Window, key);

        await AssertPlaybackAsync(context, source, output, startMs, endMs);
        Assert.Same(focused, context.Window.FocusManager?.GetFocusedElement());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task MultipleSelectedRowsAuditionOnlyThePrimaryRowWithoutChangingTheSelection()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        SelectRow(context.Window, document.Subtitles[1].Id, RawInputModifiers.Shift);
        var selection = context.ViewModel.Subtitles.SelectedIds.ToArray();
        Assert.Equal(2, selection.Length);
        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);

        Press(context.Window, Key.R);

        await AssertPlaybackAsync(context, source, output, 4000, 6000);
        Assert.Equal(selection.Order(), context.ViewModel.Subtitles.SelectedIds.Order());
        Assert.Equal(document.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q, WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, 1250, 2000)]
    [InlineData(Key.W, WorkbenchCommand.AUDITION_AFTER_SUBTITLE, 3000, 3750)]
    [InlineData(Key.E, WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, 2000, 2750)]
    [InlineData(Key.R, WorkbenchCommand.AUDITION_SUBTITLE, 2000, 3000)]
    public async Task ListAuditionUsesReboundAndDisabledBindingsAndConfiguredMilliseconds(Key originalKey,
        WorkbenchCommand command, int startMs, int endMs)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        var bindings = context.Session.Preferences.ShortcutBindings.Select(binding =>
            binding.Command == command ? binding with { Gesture = "F2" } : binding).ToImmutableArray();
        context.Session.UpdatePreferences(current => current with
        {
            SubtitleAuditionMilliseconds = 750, ShortcutBindings = bindings
        });

        Press(context.Window, originalKey);
        Assert.Equal(0, output.PlaybackStartCount);
        Press(context.Window, Key.F2);
        await AssertPlaybackAsync(context, source, output, startMs, endMs);
        bindings = bindings.Select(binding => binding.Command == command ? binding with { Gesture = "" } : binding).ToImmutableArray();
        context.Session.UpdatePreferences(current => current with { ShortcutBindings = bindings });
        Press(context.Window, Key.F2);
        Press(context.Window, originalKey);

        Assert.Equal(1, output.PlaybackStartCount);
        Assert.False(context.Controller.IsRangePlaybackActive);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task FloatingSubtitleListRoutesTheSameSelectedCueThroughItsRegisteredWindow()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        context.Window.Layouts.Float("subtitles");
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            Flush(floating);
            Assert.Contains(floating, context.WindowRegistry.Windows);
            SelectRow(floating, document.Subtitles[0].Id);

            Press(floating, Key.R);

            await AssertPlaybackAsync(context, source, output, 2000, 3000);
            Assert.IsType<ListBoxItem>(floating.FocusManager?.GetFocusedElement());
            Assert.Equal(document.Subtitles[0].Id, context.Session.SelectedCueId);
            Assert.Same(document, context.Session.DocumentSnapshot);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
            Assert.DoesNotContain(floating, context.WindowRegistry.Windows);
        }
    }

    [AvaloniaTheory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task RowTextAndTimeInputsKeepLettersAndImeLocalEvenWhenAuditionIsReboundToAFunctionKey(int column, bool ime)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        var cue = document.Subtitles[0];
        SelectRow(context.Window, cue.Id);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == cue.Id && Grid.GetColumn(control) == column);
        Assert.True(input.Focus());
        input.CaretIndex = input.Text!.Length;
        var originalText = input.Text;
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
        var seekCount = source.SeekCount;
        try
        {
            if (ime)
            {
                presenter.PreeditText = "字幕候选";
            }

            foreach (var key in new[] { Key.Q, Key.W, Key.E, Key.R })
            {
                context.Window.KeyPress(key, RawInputModifiers.None, Enum.Parse<PhysicalKey>(key.ToString()), key.ToString().ToLowerInvariant());
                context.Window.KeyTextInput(key.ToString().ToLowerInvariant());
                context.Window.KeyRelease(key, RawInputModifiers.None, Enum.Parse<PhysicalKey>(key.ToString()), key.ToString().ToLowerInvariant());
            }

            Assert.Equal(originalText + "qwer", input.Text);
            var bindings = context.Session.Preferences.ShortcutBindings.Select(binding =>
                binding.Command == WorkbenchCommand.AUDITION_SUBTITLE ? binding with { Gesture = "F2" } : binding).ToImmutableArray();
            context.Session.UpdatePreferences(current => current with { ShortcutBindings = bindings });
            if (ime)
            {
                presenter.PreeditText = "字幕候选";
            }
            Press(context.Window, Key.F2);

            Assert.Same(input, context.Window.FocusManager?.GetFocusedElement());
            Assert.Equal(originalText + "qwer", input.Text);
            Assert.Equal(0, output.PlaybackStartCount);
            Assert.Equal(seekCount, source.SeekCount);
            Assert.Equal(new MediaTime(10), context.Controller.Snapshot.Position);
            Assert.False(context.Controller.IsRangePlaybackActive);
            Assert.Same(document, context.Session.DocumentSnapshot);
        }
        finally
        {
            presenter.PreeditText = null;
            context.ViewModel.Subtitles.Rows.Single(row => row.Id == cue.Id).Accept(cue);
        }
    }

    [AvaloniaFact]
    public async Task ListFocusWithoutASelectedCueDoesNotStartPlaybackOrCreateASubtitle()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        context.Session.SelectTrack(context.Session.ClipIndex.GetSubtitleTrackId(document.Subtitles[0].Id));
        Assert.Null(context.Session.SelectedCueId);
        Assert.Empty(context.ViewModel.Subtitles.SelectedIds);

        foreach (var key in new[] { Key.Q, Key.W, Key.E, Key.R })
        {
            Press(context.Window, key);
        }

        Assert.Equal(0, output.PlaybackStartCount);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Controller.IsRangePlaybackActive);
    }

    [AvaloniaFact]
    public async Task OpenListContextMenuKeepsQwerOutOfAudition()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var item = RowItem(context.Window, document.Subtitles[0].Id);
        var header = RowHeader(item);
        var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Right);
        context.Window.MouseUp(point, MouseButton.Right);
        Flush(context.Window);
        var menu = Assert.IsType<ContextMenu>(list.ContextMenu);
        try
        {
            Assert.True(menu.IsOpen);
            foreach (var key in new[] { Key.Q, Key.W, Key.E, Key.R })
            {
                Press(context.Window, key);
            }

            Assert.Equal(0, output.PlaybackStartCount);
            Assert.False(context.Controller.IsRangePlaybackActive);
            Assert.Same(document, context.Session.DocumentSnapshot);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaFact]
    public async Task ShortcutRecordingCapturesTheAuditionKeyWithoutStartingListPlayback()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var document = await PrepareAsync(context);
        await FreezeAsync(context);
        SelectRow(context.Window, document.Subtitles[0].Id);
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        try
        {
            settings.SelectPage(SettingsPage.SHORTCUTS);
            UiTestActions.Click(settings, "RecordShortcutButton");
            Assert.True(UiTestActions.Find<Control>(settings, "GestureInput").Focus());
            Press(settings, Key.R);

            Assert.Equal("R", settings.ViewModel.Shortcuts.Gesture);
            Assert.Equal(0, output.PlaybackStartCount);
            Assert.False(context.Controller.IsRangePlaybackActive);
            Assert.Same(document, context.Session.DocumentSnapshot);
        }
        finally
        {
            settings.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.Q)]
    [InlineData(Key.W)]
    [InlineData(Key.E)]
    [InlineData(Key.R)]
    public async Task SelectedRowWithoutAudioDoesNotStartAudition(Key key)
    {
        await using var context = new MainWindowTestContext();
        var document = await PrepareAsync(context);
        await FreezeAsync(context, false);
        SelectRow(context.Window, document.Subtitles[0].Id);
        Assert.False(context.Session.CanAuditionSubtitle);

        Press(context.Window, key);

        Assert.False(context.Controller.IsRangePlaybackActive);
        Assert.Equal(new MediaTime(10), context.Controller.Snapshot.Position);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.Null(context.Controller.Snapshot.Error);
    }

    private static MainWindowTestContext CreateContext(UiAuditionAudioSource source, UiAuditionAudioOutput output) =>
        new((_, _, position, _) => Task.FromResult(new AudioPlaybackSession(source, output, position)),
            () => new(1, Enumerable.Range(0, 401).Select(index => (long)index * 50).ToArray()));

    private static async Task<ProjectDocument> PrepareAsync(MainWindowTestContext context)
    {
        await context.OpenMediaAsync();
        var first = new SubtitleLine { Start = new(2), End = new(3), Text = "First subtitle 中文 ABC 123" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Primary subtitle" };
        var document = context.Session.DocumentSnapshot with
        {
            Subtitles = [first, second], Layers = [Layer(first), Layer(second)]
        };
        context.Session.Editor.Reset(document);
        context.Window.Layouts.Activate("subtitles");
        Flush(context.Window);
        return document;
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static ListBoxItem RowItem(Window window, Guid cueId)
    {
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == cueId);
        list.ScrollIntoView(row);
        Flush(window);
        return list.GetVisualDescendants().OfType<ListBoxItem>().Single(item =>
            item.DataContext is SubtitleRow value && value.Id == cueId);
    }

    private static TextBlock RowHeader(ListBoxItem item) => item.GetVisualDescendants().OfType<TextBlock>().Single(block =>
        block.DataContext is SubtitleRow row && block.Text == row.ContentType && Grid.GetColumn(block) == 0 &&
        block.GetVisualParent() is Grid { ColumnDefinitions.Count: 5 });

    private static void SelectRow(Window window, Guid cueId, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var item = RowItem(window, cueId);
        var header = RowHeader(item);
        header.BringIntoView();
        Flush(window);
        item = RowItem(window, cueId);
        header = RowHeader(item);
        Assert.True(item.IsEffectivelyVisible);
        var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, item) || hit.GetVisualAncestors().Contains(item));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(window);
        Assert.Same(item, window.FocusManager?.GetFocusedElement());
    }

    private static void Press(Window window, Key key)
    {
        var physical = Enum.Parse<PhysicalKey>(key.ToString());
        var symbol = key is >= Key.A and <= Key.Z ? key.ToString().ToLowerInvariant() : null;
        window.KeyPress(key, RawInputModifiers.None, physical, symbol);
        window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        Flush(window);
    }

    private static async Task FreezeAsync(MainWindowTestContext context, bool expectAudio = true)
    {
        await context.Session.SeekProjectTimeAsync(new(10));
        await EventuallyAsync(() => context.Controller.Snapshot.PresentedFrameTime == new MediaTime(10) &&
            context.Controller.Snapshot.Position == new MediaTime(10) &&
            (!expectAudio || context.Controller.Snapshot.AudioAvailable));
    }

    private static async Task AssertPlaybackAsync(MainWindowTestContext context, UiAuditionAudioSource source,
        UiAuditionAudioOutput output, int startMs, int endMs)
    {
        var start = new MediaTime(startMs, 1000);
        var end = new MediaTime(endMs, 1000);
        await EventuallyAsync(() => output.PlaybackStartCount == 1 && !output.Paused && source.LastSeek == start);
        await EventuallyAsync(() =>
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(5));
            return context.Controller.Snapshot.PresentedFrameTime == start &&
                context.Controller.Snapshot.Position == start && context.ViewModel.Timeline.Position == start;
        });
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
        Assert.False(context.Controller.Snapshot.AudioAuditionActive);
        Assert.True(context.ViewModel.Preview.IsPlaying);
        await EventuallyAsync(() =>
        {
            output.ConsumeAvailable();
            context.Clock.Advance(TimeSpan.FromMilliseconds(5));
            return !context.Controller.IsRangePlaybackActive && output.Paused;
        });
        await EventuallyAsync(() => context.Controller.Snapshot.Position == end &&
            context.ViewModel.Timeline.Position == end && !context.ViewModel.Preview.IsPlaying);
        Assert.Equal(VideoPlaybackState.ENDED, context.Controller.Snapshot.State);
        Assert.Equal((endMs - startMs) * 48 * 2, output.Written.Length);
        Assert.Equal(startMs * 48, output.Written[0]);
        Assert.Equal(endMs * 48 - 1, output.Written[^1]);
        Assert.Null(context.Controller.Snapshot.Error);
        Assert.Null(context.Controller.Snapshot.AudioError);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Subtitle list audition did not reach the expected state.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
