using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Fonts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Styles;

/// <summary>样式页面局部字体、对齐确认和焦点适配。</summary>
public sealed partial class StyleSettingsView : UserControl, IDisposable
{
    private StyleSettingsViewModel? model;
    private bool synchronizingSelection;
    private readonly SubtitleStylePreviewScheduler preview;
    private SubtitleFontSelectionService? observedFonts;
    private long previewRevision;
    private Guid? previewPresetId;
    private bool disposed;
    internal Task PreviewCompletion => preview.Completion;

    /// <summary>先隔离父级上下文，再加载编译绑定和本地控件的语义提交。</summary>
    public StyleSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        preview = new(SubtitleStylePreviewRenderer.Render, PresentPreview);
        DataContextChanged += (_, _) => ChangeModel();
        AttachedToVisualTree += (_, _) =>
        {
            RefreshPreview();
            _ = LoadFontCandidatesAsync();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            CancelNumericDrags();
            preview.Invalidate(++previewRevision);
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                CancelNumericDrags();
            }
        };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is Control source &&
                source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault()?.Name is { } field &&
                model?.RestoreAppearanceField(field) == true)
            {
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        this.FindControl<ListBox>("StyleList")!.SelectionChanged += SelectionChanged;
        this.FindControl<FontFamilyPicker>("FontInput")!.FamilyCommitted +=
            (_, value) => model?.CommitFont(value.Selection);
        this.FindControl<FontFamilyPicker>("FontInput")!.PropertyChanged += OnFontPickerChanged;
        this.FindControl<SubtitleAlignmentPicker>("AlignmentPicker")!.AlignmentCommitted +=
            (_, value) => model?.CommitAlignment(value.Alignment);
        BindScalingSlider();
    }

    private void ChangeModel()
    {
        if (disposed)
        {
            return;
        }
        CancelNumericDrags();
        preview.Invalidate(++previewRevision);
        previewPresetId = null;
        if (observedFonts is not null)
        {
            observedFonts.Changed -= OnFontsChanged;
            observedFonts = null;
        }
        this.FindControl<PreviewViewportControl>("StylePreviewViewport")!.ResetView();
        this.FindControl<VideoFramePresenter>("StylePreviewFrame")!.Clear();
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
            model.DraftChanging -= OnDraftChanging;
            model.CommitPendingInputs = null;
            model.HasPendingInputs = null;
        }

        model = DataContext as StyleSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
            model.DraftChanging += OnDraftChanging;
            model.CommitPendingInputs = CommitFontInput;
            model.HasPendingInputs = () =>
            {
                var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
                return picker.Text != picker.CurrentFont.DisplayName;
            };
            ObserveFonts();
            SynchronizeSelection();
            RefreshFontFamilies();
            LoadFont();
        }
        RefreshPreview();
    }

    private async void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (synchronizingSelection || model is null)
        {
            return;
        }
        CancelNumericDrags();
        var list = this.FindControl<ListBox>("StyleList")!;
        var ids = list.Selection.SelectedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>()
            .Select(value => value.Id).ToArray();
        var primary = e.AddedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().LastOrDefault()?.Id ??
            (model.SelectedStyle is { } current && ids.Contains(current.Id) ? current.Id : ids.Cast<Guid?>().FirstOrDefault());
        var currentModel = model;
        SynchronizeSelection();
        try
        {
            await currentModel.SelectStylesAsync(primary, ids);
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
        var list = this.FindControl<ListBox>("StyleList")!;
        var values = list.Items.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().ToArray();
        if (list.Selection.SelectedItems.OfType<AegiNext.Core.Presets.SubtitleStylePreset>().Select(value => value.Id)
            .ToHashSet().SetEquals(model.SelectedIds))
        {
            return;
        }
        synchronizingSelection = true;
        try
        {
            using var update = list.Selection.BatchUpdate();
            list.Selection.Clear();
            var primary = Array.FindIndex(values, value => value.Id == model.SelectedStyle?.Id);
            if (primary >= 0 && model.SelectedIds.Contains(values[primary].Id))
            {
                list.Selection.Select(primary);
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (index != primary && model.SelectedIds.Contains(values[index].Id))
                {
                    list.Selection.Select(index);
                }
            }
        }
        finally
        {
            synchronizingSelection = false;
        }
    }

    private bool CommitFontInput()
    {
        var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
        if (picker.CommitText())
        {
            return true;
        }
        model?.RejectFont();
        picker.Focus();
        return false;
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StyleSettingsViewModel.SelectedStyle) or nameof(StyleSettingsViewModel.SelectedIds))
        {
            CancelNumericDrags();
        }
        if (e.PropertyName == nameof(StyleSettingsViewModel.PreviewRevision))
        {
            RefreshPreview();
        }
        if (e.PropertyName is nameof(StyleSettingsViewModel.SelectedIds) or nameof(StyleSettingsViewModel.Styles))
        {
            SynchronizeSelection();
        }
        if (e.PropertyName is nameof(StyleSettingsViewModel.Styles) or nameof(StyleSettingsViewModel.Fonts))
        {
            ObserveFonts();
            RefreshFontFamilies();
            if (e.PropertyName == nameof(StyleSettingsViewModel.Fonts))
            {
                RefreshPreview();
            }
        }
        else if (e.PropertyName == nameof(StyleSettingsViewModel.DraftVersion))
        {
            LoadFont();
        }
        else if (e.PropertyName == nameof(StyleSettingsViewModel.AlignmentIndex))
        {
            this.FindControl<SubtitleAlignmentPicker>("AlignmentPicker")!.SetCurrentValue(
                SubtitleAlignmentPicker.AlignmentIndexProperty, model!.AlignmentIndex);
        }
    }

    private void OnDraftChanging(object? sender, EventArgs e) => CancelNumericDrags();

    private void CancelNumericDrags()
    {
        CancelScalingGesture();
        foreach (var title in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            title.CancelDrag();
        }
    }

    private void RefreshFontFamilies()
    {
        var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
        picker.PreviewProvider = model!.Fonts.PreviewProvider;
        picker.RefreshFontCandidates(model.Fonts.Candidates, model.Styles.Select(value => value.Style.FontFamily));
    }

    private void ObserveFonts()
    {
        if (ReferenceEquals(observedFonts, model?.Fonts))
        {
            return;
        }

        if (observedFonts is not null)
        {
            observedFonts.Changed -= OnFontsChanged;
        }

        observedFonts = model?.Fonts;
        if (observedFonts is not null)
        {
            observedFonts.Changed += OnFontsChanged;
        }
    }

    private void OnFontsChanged(object? sender, EventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnFontsChanged(sender, args));
            return;
        }

        if (!disposed && model is not null && ReferenceEquals(sender, observedFonts))
        {
            RefreshFontFamilies();
            RefreshPreview();
        }
    }

    private void OnFontPickerChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == AutoCompleteBox.IsDropDownOpenProperty &&
            this.FindControl<FontFamilyPicker>("FontInput")!.IsDropDownOpen)
        {
            _ = LoadFontCandidatesAsync();
        }
    }

    private async Task LoadFontCandidatesAsync()
    {
        var currentModel = model;
        var fonts = observedFonts;
        if (disposed || currentModel is null || fonts is null)
        {
            return;
        }

        try
        {
            await fonts.EnsureLoadedAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!disposed && ReferenceEquals(currentModel, model) && ReferenceEquals(fonts, observedFonts))
            {
                currentModel.SetPreviewError(error.Message);
            }
        }
    }

    private void LoadFont()
    {
        var preset = model!.Draft;
        var style = preset?.Style ?? new();
        this.FindControl<FontFamilyPicker>("FontInput")!.SetCurrentFont(
            new(style.FontFamily, style.FontVariant, preset?.Font is null && !style.FontAssetId.HasValue));
    }

    private void RefreshPreview()
    {
        var revision = ++previewRevision;
        preview.Invalidate(revision);
        if (disposed || model is null || !model.HasPreview)
        {
            previewPresetId = null;
            this.FindControl<PreviewViewportControl>("StylePreviewViewport")!.ResetView();
            this.FindControl<VideoFramePresenter>("StylePreviewFrame")!.Clear();
            model?.SetPreviewError(null);
            return;
        }
        if (previewPresetId != model.Draft!.Id)
        {
            previewPresetId = model.Draft.Id;
            this.FindControl<PreviewViewportControl>("StylePreviewViewport")!.ResetView();
            this.FindControl<VideoFramePresenter>("StylePreviewFrame")!.Clear();
            model.SetPreviewError(null);
        }
        if (!IsVisible || !this.IsAttachedToVisualTree() || !model.TryCreatePreviewPreset(out var preset))
        {
            return;
        }
        preview.Submit(new(revision, preset!, model.PreviewText,
            model.CanvasWidth, model.CanvasHeight, model.Fonts.Catalog ?? SystemFontCatalog.Empty));
    }

    private void PresentPreview(SubtitleStylePreviewResult result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (disposed || result.Request.Revision != previewRevision || model?.Draft?.Id != result.Request.Preset.Id ||
                !IsVisible || !this.IsAttachedToVisualTree())
            {
                return;
            }
            model.SetPreviewError(result.Error?.Message);
            if (result.Frame is { } frame)
            {
                this.FindControl<VideoFramePresenter>("StylePreviewFrame")!.Present(frame);
            }
        });
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && preview is not null)
        {
            if (!IsVisible)
            {
                this.FindControl<PreviewViewportControl>("StylePreviewViewport")!.CancelInteraction();
            }
            RefreshPreview();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        CancelNumericDrags();
        ReleaseScalingBindings();
        disposed = true;
        previewRevision++;
        this.FindControl<PreviewViewportControl>("StylePreviewViewport")!.CancelInteraction();
        preview.Dispose();
        this.FindControl<FontFamilyPicker>("FontInput")!.PropertyChanged -= OnFontPickerChanged;
        if (observedFonts is not null)
        {
            observedFonts.Changed -= OnFontsChanged;
            observedFonts = null;
        }
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
            model.DraftChanging -= OnDraftChanging;
            model.CommitPendingInputs = null;
            model.HasPendingInputs = null;
        }
        this.FindControl<VideoFramePresenter>("StylePreviewFrame")!.Dispose();
    }

    private void FocusName(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("StyleNameInput")!.Focus();
    }

    private void SaveStyle(object? sender, RoutedEventArgs e)
    {
        Submit(false);
    }

    private void ApplyStyle(object? sender, RoutedEventArgs e)
    {
        Submit(true);
    }

    private void Submit(bool apply)
    {
        if (model is null)
        {
            return;
        }

        var picker = this.FindControl<FontFamilyPicker>("FontInput")!;
        if (!picker.CommitText())
        {
            model.RejectFont();
            picker.Focus();
            return;
        }

        var command = apply ? model.ApplyCommand : model.SaveCommand;
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        if (model.Error is not null && model.InvalidFieldKey is { } fieldKey)
        {
            var colorField = fieldKey.Split('.', 2);
            if (colorField.Length == 2 && this.FindControl<ColorDraftInput>(colorField[0]) is { } colorInput)
            {
                _ = colorInput.TryFocusInvalidField(colorField[1]);
                return;
            }
            if (!this.FindControl<VectorDraftInput>("ShadowOffsetInput")!.FocusField(fieldKey) &&
                !this.FindControl<SubtitleMarginsEditor>("MarginsEditor")!.FocusInvalidField(fieldKey) &&
                !this.FindControl<LayerPositionEditor>("PositionEditor")!.FocusInvalidField(fieldKey))
            {
                var input = this.FindControl<Control>(fieldKey) ?? this.FindControl<FontFamilyPicker>("FontInput")!;
                if (input is NumericDraftInput numeric)
                {
                    numeric.FocusInput();
                }
                else
                {
                    input.Focus();
                }
            }
        }
    }
}
