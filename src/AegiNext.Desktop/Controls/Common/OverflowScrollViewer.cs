using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>为直接包含的 <see cref="OverflowGrid"/> 提供有限视口，并允许低于最小布局高度时整体纵向滚动。</summary>
public sealed class OverflowScrollViewer : ScrollViewer
{
    private ScrollContentPresenter? presenter;
    private long bringIntoViewRevision;

    static OverflowScrollViewer()
    {
        HorizontalScrollBarVisibilityProperty.OverrideDefaultValue<OverflowScrollViewer>(ScrollBarVisibility.Disabled);
        VerticalScrollBarVisibilityProperty.OverrideDefaultValue<OverflowScrollViewer>(ScrollBarVisibility.Auto);
    }

    /// <summary>创建有限视口滚动容器，并协调内层滚动后向外层传播的定位请求。</summary>
    public OverflowScrollViewer()
    {
        AddHandler(RequestBringIntoViewEvent, OnBringIntoViewRequested, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        ++bringIntoViewRevision;
        presenter = e.NameScope.Find<ScrollContentPresenter>("PART_ContentPresenter");
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Content is OverflowGrid grid)
        {
            var inset = Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;
            grid.SetViewportHeight(Math.Max(0, availableSize.Height - inset));
        }

        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++bringIntoViewRevision;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnBringIntoViewRequested(object? sender, RequestBringIntoViewEventArgs e)
    {
        var revision = ++bringIntoViewRevision;
        if (presenter is not { } currentPresenter || e.TargetObject is not { } target ||
            !target.GetVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, this)).OfType<ScrollViewer>().Any())
        {
            return;
        }

        var rectangle = e.TargetRect;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == bringIntoViewRevision && ReferenceEquals(presenter, currentPresenter) &&
                this.IsAttachedToVisualTree() && this.IsVisualAncestorOf(target))
            {
                currentPresenter.BringDescendantIntoView(target, rectangle);
            }
        }, DispatcherPriority.Loaded);
    }
}
