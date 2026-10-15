using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Export.Presets;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Export;

/// <summary>压制预设页面的局部多选、选项刷新和错误字段适配。</summary>
public sealed partial class ExportSettingsView : UserControl
{
    private readonly ListBox presetList;
    private readonly ComboBox[] choices;
    private ExportSettingsViewModel? model;
    private bool synchronizingSelection;

    /// <summary>隔离父级上下文，加载参数编辑与批量列表，并连接语义选择。</summary>
    public ExportSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        presetList = this.FindControl<ListBox>("ExportPresetList")!;
        choices = [this.FindControl<ComboBox>("CodecCombo")!, this.FindControl<ComboBox>("ContainerCombo")!, this.FindControl<ComboBox>("SpeedCombo")!,
            this.FindControl<ComboBox>("AudioModeCombo")!, this.FindControl<ComboBox>("QualityModeCombo")!,
            this.FindControl<ComboBox>("BitrateModeCombo")!];
        DataContextChanged += (_, _) => ChangeModel();
        DetachedFromVisualTree += (_, _) => CancelNumericDrags();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                CancelNumericDrags();
            }
        };
        presetList.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>聚焦当前错误所在字段；只查找本页的控件。</summary>
    public void FocusInvalidField()
    {
        if (model is { Error: not null, InvalidFieldKey: { } fieldKey })
        {
            this.FindControl<Control>(fieldKey)?.Focus();
        }
    }

    private void ChangeModel()
    {
        CancelNumericDrags();
        if (model is not null)
        {
            model.PropertyChanged -= OnModelChanged;
            model.DraftChanging -= OnDraftChanging;
            model.ChoicesRefreshing -= OnChoicesRefreshing;
            model.ChoicesRefreshed -= OnChoicesRefreshed;
        }
        model = DataContext as ExportSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
            model.DraftChanging += OnDraftChanging;
            model.ChoicesRefreshing += OnChoicesRefreshing;
            model.ChoicesRefreshed += OnChoicesRefreshed;
            SynchronizeSelection();
        }
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || model is null)
        {
            return;
        }
        CancelNumericDrags();
        var ids = presetList.Selection.SelectedItems.OfType<VideoExportPreset>().Select(value => value.Id).ToArray();
        var primary = e.AddedItems.OfType<VideoExportPreset>().LastOrDefault()?.Id ??
            (model.SelectedPreset is { } current && ids.Contains(current.Id) ? current.Id : ids.Cast<Guid?>().FirstOrDefault());
        var selectedModel = model;
        SynchronizeSelection();
        try
        {
            await selectedModel.SelectPresetsAsync(primary, ids);
        }
        finally
        {
            SynchronizeSelection();
        }
    }

    private void SynchronizeSelection()
    {
        if (model is null || synchronizingSelection)
        {
            return;
        }
        var values = presetList.Items.OfType<VideoExportPreset>().ToArray();
        if (presetList.Selection.SelectedItems.OfType<VideoExportPreset>().Select(value => value.Id)
            .ToHashSet().SetEquals(model.SelectedIds))
        {
            return;
        }
        synchronizingSelection = true;
        try
        {
            using var update = presetList.Selection.BatchUpdate();
            presetList.Selection.Clear();
            var primary = Array.FindIndex(values, value => value.Id == model.SelectedPreset?.Id);
            if (primary >= 0 && model.SelectedIds.Contains(values[primary].Id))
            {
                presetList.Selection.Select(primary);
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (index != primary && model.SelectedIds.Contains(values[index].Id))
                {
                    presetList.Selection.Select(index);
                }
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExportSettingsViewModel.SelectedPreset) or nameof(ExportSettingsViewModel.SelectedIds))
        {
            CancelNumericDrags();
        }
        if (e.PropertyName is nameof(ExportSettingsViewModel.ExportPresets) or nameof(ExportSettingsViewModel.SelectedIds))
        {
            SynchronizeSelection();
        }
        else if (e.PropertyName == nameof(ExportSettingsViewModel.Error))
        {
            FocusInvalidField();
        }
    }

    private void OnDraftChanging(object? sender, EventArgs e) => CancelNumericDrags();

    private void CancelNumericDrags()
    {
        foreach (var title in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            title.CancelDrag();
        }
    }

    private void OnChoicesRefreshing(object? sender, EventArgs e)
    {
        foreach (var choice in choices)
        {
            var selectedIndex = choice.SelectedIndex;
            choice.BeginInit();
            choice.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, selectedIndex);
        }
    }

    private void OnChoicesRefreshed(object? sender, EventArgs e)
    {
        var selection = new[] { model!.Codec, model.Container, model.Speed, model.AudioMode, model.QualityMode, model.BitrateMode };
        for (var index = 0; index < choices.Length; index++)
        {
            choices[index].EndInit();
            choices[index].SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, selection[index]);
        }
    }
}
