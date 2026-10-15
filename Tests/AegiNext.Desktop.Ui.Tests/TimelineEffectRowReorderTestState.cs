using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class TimelineEffectRowReorderTestState : IDisposable
{
    internal TimelineEffectRowReorderTestState(ProjectDocument document, double height = 620)
    {
        Editor = new(document);
        Timeline = new() { IsSnapEnabled = false, IsStepEnabled = false, PixelsPerSecond = 60 };
        var layer = document.Layers[0];
        Timeline.SetDocument(document, layer.SubtitleId, layer);
        Timeline.EffectClipSelected += (_, args) =>
        {
            SelectionRequests++;
            args.SelectionAccepted = AcceptSelection;
        };
        Timeline.EffectClipReorderCompleted += (_, args) =>
        {
            ReorderRequests++;
            LastReorder = args;
            Assert.Same(Editor.Snapshot, args.ExpectedDocument);
            Editor.MoveTimelineEffectRow(args.Id.LayerId, Identity(args.Id), args.BeforeId is { } before ? Identity(before) : null);
            Bind(Editor.Snapshot);
            Timeline.SelectedEffectClipId = args.Id;
        };
        Timeline.EffectInstanceTimingChanged += (_, args) =>
        {
            TimingRequests++;
            LastTiming = args;
            Assert.Same(Editor.Snapshot, args.ExpectedDocument);
            Editor.SetEffectInstanceTiming(args.Id.LayerId, args.Id.InstanceId!.Value, args.Start, args.End);
            Bind(Editor.Snapshot);
            Timeline.SelectedEffectClipId = args.Id;
        };
        Timeline.KeyframeSelected += (_, args) =>
        {
            KeyframeSelections++;
            args.SelectionAccepted = AcceptSelection;
        };
        Timeline.KeyframeMoved += (_, _) => KeyframeMoves++;
        Timeline.EffectClipCollapseRequested += (_, args) =>
        {
            CollapseRequests++;
            var expanded = Timeline.TimelineViewState.ExpandedAnimationClips;
            Timeline.TimelineViewState = Timeline.TimelineViewState with
            {
                ExpandedAnimationClips = args.IsCollapsed ? expanded.Remove(args.Id) : expanded.Add(args.Id)
            };
        };
        Timeline.AddHandler(InputElement.PointerPressedEvent, (_, args) => Pointer = args.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        Window = new() { Width = 1000, Height = height, Content = Timeline };
        Window.Show();
        Flush();
    }

    internal ProjectEditor Editor { get; }
    internal SubtitleTimelineControl Timeline { get; }
    internal Window Window { get; }
    internal IPointer? Pointer { get; private set; }
    internal bool AcceptSelection { get; set; } = true;
    internal int SelectionRequests { get; private set; }
    internal int ReorderRequests { get; private set; }
    internal int TimingRequests { get; private set; }
    internal int KeyframeSelections { get; private set; }
    internal int KeyframeMoves { get; private set; }
    internal int CollapseRequests { get; private set; }
    internal TimelineEffectClipReorderEventArgs? LastReorder { get; private set; }
    internal TimelineEffectInstanceTimingEventArgs? LastTiming { get; private set; }

    internal Rect Block(TimelineEffectClipId id) => Timeline.GetEffectClipRectangle(id)!.Value;
    internal Point Body(TimelineEffectClipId id, bool curveArea = false)
    {
        var block = Block(id);
        var left = Math.Max(block.Left, Timeline.HeaderWidth);
        var right = Math.Min(block.Right, Timeline.Bounds.Width);
        return new((left + right) / 2, block.Top + (curveArea ? block.Height * 0.65 : 10));
    }

    internal TimelineEffectClipId[] VisualOrder(IEnumerable<TimelineEffectClipId> ids) => ids.OrderBy(id => Block(id).Top).ToArray();

    internal static TimelineEffectRowIdentity Identity(TimelineEffectClipId id) =>
        id.InstanceId is { } instanceId ? new(InstanceId: instanceId) : new(Target: id.Target);

    internal void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        using var frame = Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    internal void Bind(ProjectDocument document)
    {
        var layer = document.Layers[0];
        Timeline.SetDocument(document, layer.SubtitleId, layer);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Window.Close();
        Timeline.Dispose();
    }
}
