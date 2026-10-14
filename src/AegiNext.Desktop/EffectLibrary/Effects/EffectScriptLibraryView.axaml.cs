using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AegiNext.Desktop.EffectLibrary.Effects;

/// <summary>特效页面的列表多选与编辑草稿切换适配。</summary>
public sealed partial class EffectScriptLibraryView : UserControl, IDisposable, IAsyncDisposable, IWorkbenchLocalKeyboardTarget
{
    private EffectScriptLibraryViewModel? model;
    private ImmutableArray<EffectScriptLibraryItem> displayedItems;
    private bool synchronizingSelection;

    /// <summary>隔离父级上下文，并以稳定标识同步本地列表选择。</summary>
    public EffectScriptLibraryView() : this(null)
    {
    }

    internal EffectScriptLibraryView(Func<IEffectScriptPreviewRenderer>? createRenderer,
        Action<Action>? postDrain = null, TimeProvider? timeProvider = null, TimeSpan? debounce = null)
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        previewPresenter = this.FindControl<VideoFramePresenter>("EffectPreviewFrame")!;
        previewViewport = this.FindControl<PreviewViewportControl>("EffectPreviewViewport")!;
        previewSlider = this.FindControl<Slider>("EffectPreviewPositionSlider")!;
        previewStylePicker = this.FindControl<ComboBox>("EffectPreviewStyleInput")!;
        postPreviewDrain = postDrain ?? (action => Dispatcher.UIThread.Post(action));
        preview = new(QueuePreviewUpdate, createRenderer, timeProvider, debounce);
        InitializePreviewEvents();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (!previewDisposed)
        {
            ChangeModel();
        }
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
        }

        model = DataContext as EffectScriptLibraryViewModel;
        displayedItems = default;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
        }
        SynchronizeSelection();
        ChangePreviewModel();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectScriptLibraryViewModel.Effects) or nameof(EffectScriptLibraryViewModel.SelectedIds) or
            nameof(EffectScriptLibraryViewModel.SelectedEffect))
        {
            SynchronizeSelection();
        }
        PreviewModelChanged(e);
    }

    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || model is null)
        {
            return;
        }

        var list = this.FindControl<ListBox>("EffectScriptList")!;
        var ids = list.Selection.SelectedItems.OfType<EffectScriptLibraryItem>().Select(item => item.Id).ToArray();
        var primary = e.AddedItems.OfType<EffectScriptLibraryItem>().LastOrDefault()?.Id ??
            (model.SelectedEffect is { } current && ids.Contains(current.Id) ? current.Id : ids.Cast<Guid?>().FirstOrDefault());
        var currentModel = model;
        SynchronizeSelection();
        try
        {
            await currentModel.SelectEffectsAsync(primary, ids);
        }
        finally
        {
            if (ReferenceEquals(model, currentModel))
            {
                SynchronizeSelection();
            }
        }
    }

    private void SynchronizeSelection()
    {
        if (synchronizingSelection)
        {
            return;
        }

        var list = this.FindControl<ListBox>("EffectScriptList")!;
        var nextItems = model?.Effects ?? [];
        var ids = model?.SelectedIds ?? [];
        var selected = list.Selection.SelectedItems.OfType<EffectScriptLibraryItem>().Select(item => item.Id).ToHashSet();
        if (displayedItems == nextItems && selected.SetEquals(ids))
        {
            return;
        }

        var previousItems = list.Items.OfType<EffectScriptLibraryItem>().ToArray();
        var anchorId = list.Selection.AnchorIndex >= 0 && list.Selection.AnchorIndex < previousItems.Length
            ? previousItems[list.Selection.AnchorIndex].Id : (Guid?)null;
        synchronizingSelection = true;
        try
        {
            if (displayedItems != nextItems)
            {
                displayedItems = nextItems;
                list.ItemsSource = nextItems;
            }
            using var update = list.Selection.BatchUpdate();
            list.Selection.Clear();
            var primary = Array.FindIndex(nextItems.ToArray(), item => item.Id == model?.SelectedEffect?.Id);
            if (primary >= 0 && ids.Contains(nextItems[primary].Id))
            {
                list.Selection.Select(primary);
            }
            for (var index = 0; index < nextItems.Length; index++)
            {
                if (index != primary && ids.Contains(nextItems[index].Id))
                {
                    list.Selection.Select(index);
                }
            }
            var anchor = Array.FindIndex(nextItems.ToArray(), item => item.Id == anchorId && ids.Contains(item.Id));
            list.Selection.AnchorIndex = anchor >= 0 ? anchor : primary;
        }
        finally
        {
            synchronizingSelection = false;
        }
    }
}
