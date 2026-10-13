using System.ComponentModel;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.EffectPresets;

internal sealed partial class EffectPresetsPanelView : UserControl, IWorkbenchPanelView, IAsyncDisposable, IWorkbenchLocalKeyboardTarget
{
    private readonly WorkbenchSession session;
    private readonly EffectPresetsPanelViewModel viewModel;
    private readonly ListBox presetList;
    private readonly EffectPresetPreviewScheduler scheduler;
    private readonly Action<Action> postDrain;
    private readonly HashSet<EffectPresetCardView> cards = [];
    private readonly Dictionary<EffectPresetCardView, long> hovered = [];
    private readonly Dictionary<string, EffectPresetPreviewResult> pending = new(StringComparer.Ordinal);
    private readonly Lock mailboxGate = new();
    private EffectPresetCardView? focused;
    private long focusedSequence;
    private long triggerSequence;
    private TopLevel? scalingHost;
    private ScrollViewer? scroller;
    private bool attached;
    private bool drainQueued;
    private bool disposed;

    internal EffectPresetsPanelView(EffectPresetsPanelViewModel viewModel, WorkbenchSession session,
        Func<IEffectPresetPreviewRenderer>? createRenderer = null, Action<Action>? postDrain = null)
    {
        this.session = session;
        this.viewModel = viewModel;
        this.postDrain = postDrain ?? (action => Dispatcher.UIThread.Post(action));
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        presetList = this.FindControl<ListBox>("EffectPresetList")!;
        scheduler = new(QueueResult, createRenderer: createRenderer, deferLoopUntilPresented: true);
        presetList.ItemTemplate = new FuncDataTemplate<EffectPresetCardViewModel>((item, _) => CreateCard(item));
        presetList.DoubleTapped += OnDoubleTapped;
        AddHandler(GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        AddHandler(LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        viewModel.PropertyChanged += OnViewModelChanged;
        session.Fonts.Changed += OnFontsChanged;
    }

    public string PanelId => WorkbenchPanelIds.EFFECT_PRESETS;

    /// <inheritdoc />
    public bool OwnsLocalKey(Key key, KeyModifiers modifiers, IInputElement focusedElement) =>
        modifiers == KeyModifiers.None &&
        key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown or
            Key.Space or Key.Enter or Key.Tab && focusedElement is Visual focusedVisual &&
        (ReferenceEquals(focusedVisual, presetList) || focusedVisual.GetVisualAncestors().Contains(presetList));

    internal string? ActivePreviewId { get; private set; }
    internal Task PreviewCompletion => scheduler.Completion;
    internal int PendingPreviewCount
    {
        get
        {
            lock (mailboxGate)
            {
                return pending.Count;
            }
        }
    }

    /// <inheritdoc />
    public void CancelGestures()
    {
        hovered.Clear();
        focused = null;
        SetActive(null);
    }

    /// <inheritdoc />
    public void FocusInvalidField(string? fieldKey) => presetList.Focus();

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (disposed)
        {
            return;
        }
        attached = true;
        scalingHost = TopLevel.GetTopLevel(this);
        if (scalingHost is not null)
        {
            scalingHost.ScalingChanged += OnScalingChanged;
        }
        LayoutUpdated += OnLayoutUpdated;
        RefreshVisibility();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        LayoutUpdated -= OnLayoutUpdated;
        if (scalingHost is not null)
        {
            scalingHost.ScalingChanged -= OnScalingChanged;
        }
        scalingHost = null;
        SetScroller(null);
        if (!disposed)
        {
            StopPreview();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private EffectPresetCardView CreateCard(EffectPresetCardViewModel? item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var previous in cards.Where(card => card.ViewModel.Id == item.Id).ToArray())
        {
            RemoveCard(previous);
        }
        var card = new EffectPresetCardView(item);
        cards.Add(card);
        card.PointerEntered += OnCardEntered;
        card.PointerExited += OnCardExited;
        card.EffectiveViewportChanged += OnEffectiveViewportChanged;
        return card;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CardFromSource(e.Source) is not { } card)
        {
            return;
        }
        viewModel.SelectedPreset = card.ViewModel;
        if (viewModel.ApplyCommand.CanExecute(null))
        {
            viewModel.ApplyCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnCardEntered(object? sender, PointerEventArgs e)
    {
        if (sender is EffectPresetCardView card && IsInViewport(card))
        {
            hovered[card] = ++triggerSequence;
            RefreshActive();
        }
    }

    private void OnCardExited(object? sender, PointerEventArgs e)
    {
        if (sender is EffectPresetCardView card)
        {
            hovered.Remove(card);
            RefreshActive();
        }
    }

    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        focused = CardFromSource(e.Source);
        focusedSequence = ++triggerSequence;
        RefreshActive();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        focused = null;
        RefreshActive();
    }

    private EffectPresetCardView? CardFromSource(object? source)
    {
        if (source is not Control control)
        {
            return null;
        }
        var ancestry = control.GetSelfAndVisualAncestors().OfType<Control>().ToArray();
        return ancestry.OfType<EffectPresetCardView>().FirstOrDefault() ??
            cards.FirstOrDefault(card => ancestry.Any(parent => ReferenceEquals(parent.DataContext, card.ViewModel)) &&
                card.IsAttachedToVisualTree());
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EffectPresetsPanelViewModel.Items))
        {
            foreach (var card in cards.Where(card => !viewModel.Items.Contains(card.ViewModel)).ToArray())
            {
                RemoveCard(card);
            }
            RefreshVisibility();
        }
    }

    private void OnFontsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshVisibility);
    private void OnLayoutUpdated(object? sender, EventArgs e) => RefreshVisibility();
    private void OnScalingChanged(object? sender, EventArgs e) => RefreshVisibility();
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => RefreshVisibility();
    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e) => RefreshVisibility();

    private void SetScroller(ScrollViewer? replacement)
    {
        if (ReferenceEquals(scroller, replacement))
        {
            return;
        }
        if (scroller is not null)
        {
            scroller.ScrollChanged -= OnScrollChanged;
        }
        scroller = replacement;
        if (scroller is not null)
        {
            scroller.ScrollChanged += OnScrollChanged;
        }
    }

    private void RefreshVisibility()
    {
        if (disposed)
        {
            return;
        }
        if (!attached || !IsEffectivelyVisible)
        {
            StopPreview();
            return;
        }
        SetScroller(presetList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
        var requests = new List<EffectPresetPreviewRequest>();
        foreach (var card in cards)
        {
            if (!IsInViewport(card))
            {
                hovered.Remove(card);
                if (ReferenceEquals(focused, card))
                {
                    focused = null;
                }
                if (card.Request is not null)
                {
                    card.Clear();
                }
                continue;
            }
            var model = card.ViewModel;
            var scaling = scalingHost?.RenderScaling ?? 1;
            if (card.Request is not { } request || request.Revision != model.Revision ||
                !request.RenderScaling.Equals(scaling) || !ReferenceEquals(request.FontCatalog, session.Fonts.Catalog))
            {
                card.Clear();
                card.Request = new(model.Id, model.Source, model.Revision, scaling, session.Fonts.Catalog);
            }
            requests.Add(card.Request!);
        }
        scheduler.UpdateVisible(requests);
        lock (mailboxGate)
        {
            var visibleIds = requests.Select(request => request.PresetId).ToHashSet(StringComparer.Ordinal);
            foreach (var id in pending.Keys.Where(id => !visibleIds.Contains(id)).ToArray())
            {
                pending.Remove(id);
            }
        }
        RefreshActive();
    }

    private bool IsInViewport(EffectPresetCardView card)
    {
        if (!attached || !IsEffectivelyVisible || !card.IsAttachedToVisualTree() || !card.IsEffectivelyVisible ||
            !viewModel.Items.Contains(card.ViewModel) || card.Bounds.Width <= 0 || card.Bounds.Height <= 0)
        {
            return false;
        }
        return scroller is null || card.TranslatePoint(default, scroller) is { } point &&
            new Rect(point, card.Bounds.Size).Intersects(new Rect(scroller.Bounds.Size));
    }

    private void RefreshActive()
    {
        var candidate = focused is { } focusedCard && IsInViewport(focusedCard) ? focusedCard : null;
        var latest = candidate is not null ? focusedSequence : 0;
        foreach (var (card, sequence) in hovered)
        {
            if (sequence > latest && IsInViewport(card))
            {
                candidate = card;
                latest = sequence;
            }
        }
        SetActive(candidate?.ViewModel.Id);
    }

    private void SetActive(string? id)
    {
        ActivePreviewId = id;
        scheduler.SetActive(id);
    }

    private void QueueResult(EffectPresetPreviewResult result)
    {
        if (!scheduler.IsCurrent(result))
        {
            return;
        }
        lock (mailboxGate)
        {
            if (disposed || !scheduler.IsCurrent(result))
            {
                return;
            }
            pending[result.Request.PresetId] = result;
            if (drainQueued)
            {
                return;
            }
            drainQueued = true;
        }
        postDrain(DrainResults);
    }

    private void DrainResults()
    {
        EffectPresetPreviewResult[] results;
        lock (mailboxGate)
        {
            results = pending.Values.ToArray();
            pending.Clear();
            drainQueued = false;
        }
        if (disposed || !attached || !IsEffectivelyVisible)
        {
            return;
        }
        foreach (var result in results)
        {
            if (!scheduler.IsCurrent(result))
            {
                continue;
            }
            var card = cards.FirstOrDefault(card => ReferenceEquals(card.Request, result.Request) && IsInViewport(card));
            if (card is not null)
            {
                card.Present(result);
                if (result.CompletesCycle && result.Frame is not null && result.Error is null)
                {
                    Dispatcher.UIThread.Post(() => scheduler.CompleteCycle(result), DispatcherPriority.Background);
                }
            }
        }
    }

    private void StopPreview()
    {
        CancelGestures();
        scheduler.Invalidate();
        lock (mailboxGate)
        {
            pending.Clear();
        }
        foreach (var card in cards)
        {
            card.Clear();
        }
    }

    private void RemoveCard(EffectPresetCardView card)
    {
        hovered.Remove(card);
        if (ReferenceEquals(focused, card))
        {
            focused = null;
        }
        cards.Remove(card);
        lock (mailboxGate)
        {
            pending.Remove(card.ViewModel.Id);
        }
        card.PointerEntered -= OnCardEntered;
        card.PointerExited -= OnCardExited;
        card.EffectiveViewportChanged -= OnEffectiveViewportChanged;
        card.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        lock (mailboxGate)
        {
            disposed = true;
            pending.Clear();
        }
        scheduler.Dispose();
        LayoutUpdated -= OnLayoutUpdated;
        if (scalingHost is not null)
        {
            scalingHost.ScalingChanged -= OnScalingChanged;
        }
        SetScroller(null);
        viewModel.PropertyChanged -= OnViewModelChanged;
        session.Fonts.Changed -= OnFontsChanged;
        presetList.DoubleTapped -= OnDoubleTapped;
        RemoveHandler(GotFocusEvent, OnGotFocus);
        RemoveHandler(LostFocusEvent, OnLostFocus);
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        foreach (var card in cards.ToArray())
        {
            RemoveCard(card);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await scheduler.DisposeAsync();
    }
}
