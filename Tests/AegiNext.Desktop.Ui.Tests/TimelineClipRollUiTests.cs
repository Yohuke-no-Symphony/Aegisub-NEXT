using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipRollUiTests
{
    [AvaloniaTheory]
    [InlineData("end")]
    [InlineData("start")]
    public void DraggingTheSharedBoundaryOfTwoSelectedAdjacentClipsAdjustsBothClips(string edge)
    {
        var (timeline, document, first, second) = Create(selectBoth: true);
        using var _ = timeline;
        TimelineClipRollEventArgs? rolled = null;
        timeline.ClipRollCompleted += (_, e) => rolled = e;
        timeline.TimingChanged += (_, _) => Assert.Fail("A roll must commit one shared boundary change.");
        var window = OpenWindow(timeline);
        try
        {
            var grabbed = edge == "end" ? first : second;
            var clip = timeline.GetClipRectangle(grabbed.Id)!.Value;
            var origin = edge == "end" ? new Point(clip.Right - 1, clip.Center.Y) : new Point(clip.Left + 1, clip.Center.Y);
            var destination = origin + new Vector(24, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            var movedFirst = timeline.GetClipRectangle(first.Id)!.Value;
            var movedSecond = timeline.GetClipRectangle(second.Id)!.Value;
            Assert.Equal(movedFirst.Right, movedSecond.Left, 3);
            Assert.True(movedFirst.Right > clip.Right - 1.5 || edge == "start");
            window.MouseUp(destination, MouseButton.Left);

            Assert.NotNull(rolled);
            Assert.Equal(first.Id, rolled.LeftId);
            Assert.Equal(second.Id, rolled.RightId);
            Assert.Equal(new MediaTime(312, 1000), rolled.Boundary);
            Assert.Same(document, rolled.ExpectedDocument);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ARollCannotShrinkEitherClipBelowOneMillisecond()
    {
        var (timeline, _, first, second) = Create(selectBoth: true);
        using var _ = timeline;
        TimelineClipRollEventArgs? rolled = null;
        timeline.ClipRollCompleted += (_, e) => rolled = e;
        var window = OpenWindow(timeline);
        try
        {
            var clip = timeline.GetClipRectangle(first.Id)!.Value;
            var origin = new Point(clip.Right - 1, clip.Center.Y);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Vector(2000, 0));
            window.MouseUp(origin + new Vector(2000, 0), MouseButton.Left);

            Assert.NotNull(rolled);
            Assert.Equal(second.End - new MediaTime(1, 1000), rolled.Boundary);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnUnselectedNeighbourKeepsTheNormalSingleClipTrim()
    {
        var (timeline, _, first, _) = Create(selectBoth: false);
        using var _ = timeline;
        TimelineTimingEventArgs? committed = null;
        timeline.ClipRollCompleted += (_, _) => Assert.Fail("Only one clip is selected.");
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var clip = timeline.GetClipRectangle(first.Id)!.Value;
            var origin = new Point(clip.Right - 1, clip.Center.Y);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin - new Vector(24, 0));
            window.MouseUp(origin - new Vector(24, 0), MouseButton.Left);

            Assert.NotNull(committed);
            Assert.Equal(first.Id, committed.Id);
        }
        finally
        {
            window.Close();
        }
    }

    private static (SubtitleTimelineControl Timeline, ProjectDocument Document, ProjectLayer First, ProjectLayer Second) Create(bool selectBoth)
    {
        var a = new SubtitleLine { Start = new(1, 10), End = new(3, 10), Text = "First" };
        var b = new SubtitleLine { Start = new(3, 10), End = new(5, 10), Text = "Second" };
        var first = Layer(a);
        var second = Layer(b);
        var document = new ProjectDocument { Subtitles = [a, b], Layers = [first, second] };
        var timeline = new SubtitleTimelineControl { PixelsPerSecond = 2000, IsSnapEnabled = false };
        timeline.SetDocument(document, a.Id, first, selectBoth ? [first.Id, second.Id] : [first.Id]);
        return (timeline, document, first, second);
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static Window OpenWindow(SubtitleTimelineControl timeline)
    {
        var window = new Window { Width = 1100, Height = 340, Content = timeline };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
