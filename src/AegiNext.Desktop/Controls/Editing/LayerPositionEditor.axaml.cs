using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>共享位置草稿的可复用视图；位置语义和持久化由所属面板承担。</summary>
public sealed partial class LayerPositionEditor : UserControl
{
    public static readonly StyledProperty<LayerPositionDraft?> DraftProperty =
        AvaloniaProperty.Register<LayerPositionEditor, LayerPositionDraft?>(nameof(Draft));
    public static readonly StyledProperty<bool> ShowMarginsProperty =
        AvaloniaProperty.Register<LayerPositionEditor, bool>(nameof(ShowMargins));
    public static readonly StyledProperty<SubtitleMargins?> MarginsProperty =
        AvaloniaProperty.Register<LayerPositionEditor, SubtitleMargins?>(nameof(Margins));
    public static readonly StyledProperty<int> AlignmentProperty =
        AvaloniaProperty.Register<LayerPositionEditor, int>(nameof(Alignment), 7);
    public static readonly StyledProperty<int> CanvasWidthProperty =
        AvaloniaProperty.Register<LayerPositionEditor, int>(nameof(CanvasWidth), 1920);
    public static readonly StyledProperty<int> CanvasHeightProperty =
        AvaloniaProperty.Register<LayerPositionEditor, int>(nameof(CanvasHeight), 1080);
    public static readonly StyledProperty<bool> ShowAutomaticPositionActionProperty =
        AvaloniaProperty.Register<LayerPositionEditor, bool>(nameof(ShowAutomaticPositionAction), true);
    public static readonly StyledProperty<bool> ShowPositionModeProperty =
        AvaloniaProperty.Register<LayerPositionEditor, bool>(nameof(ShowPositionMode), true);

    public LayerPositionDraft? Draft
    {
        get => GetValue(DraftProperty);
        set => SetValue(DraftProperty, value);
    }
    public bool ShowMargins
    {
        get => GetValue(ShowMarginsProperty);
        set => SetValue(ShowMarginsProperty, value);
    }
    public SubtitleMargins? Margins
    {
        get => GetValue(MarginsProperty);
        set => SetValue(MarginsProperty, value);
    }
    public int Alignment
    {
        get => GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }
    public int CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }
    public int CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }

    /// <summary>决定宿主是否在位置编辑器内显示自动位置操作。</summary>
    public bool ShowAutomaticPositionAction
    {
        get => GetValue(ShowAutomaticPositionActionProperty);
        set => SetValue(ShowAutomaticPositionActionProperty, value);
    }

    /// <summary>字幕可选择自动布局；图片和绘图直接使用共享位置字段。</summary>
    public bool ShowPositionMode
    {
        get => GetValue(ShowPositionModeProperty);
        set => SetValue(ShowPositionModeProperty, value);
    }

    private LayerPositionDraft? model;
    private readonly AnchorPresetPicker presets;
    private readonly StackPanel draftRoot;
    private readonly SegmentedRadioButton automaticMode;
    private readonly SegmentedRadioButton customMode;
    private bool attached;
    private bool synchronizingModes;

    /// <summary>创建数值编辑器，隔离内部草稿上下文并兼容旧 DataContext 输入。</summary>
    public LayerPositionEditor()
    {
        AvaloniaXamlLoader.Load(this);
        presets = this.FindControl<AnchorPresetPicker>("AnchorPresets")!;
        draftRoot = this.FindControl<StackPanel>("DraftRoot")!;
        automaticMode = this.FindControl<SegmentedRadioButton>("AutomaticPositionMode")!;
        customMode = this.FindControl<SegmentedRadioButton>("CustomPositionMode")!;
        var groupName = Guid.NewGuid().ToString("N");
        automaticMode.GroupName = groupName;
        customMode.GroupName = groupName;
        automaticMode.IsCheckedChanged += ModeChanged;
        customMode.IsCheckedChanged += ModeChanged;
        DataContextChanged += (_, _) => ChangeModel();
        presets.PresetSelected += (_, selection) =>
        {
            if (model?.SelectPreset(selection.Anchor, selection.SetPivot, selection.ResetOffset) == true)
            {
                PresetPositionChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (model?.Validate() is { } field)
            {
                FocusInvalidField(field);
            }
        };
        this.FindControl<Button>("AutomaticPositionButton")!.Click += (_, _) =>
        {
            if (model is not null)
            {
                if (AutomaticPositionRequested is { } handler)
                {
                    handler(this, EventArgs.Empty);
                }
                else
                {
                    model.IsExplicit = false;
                    ExplicitPositionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        };
        AddHandler(KeyDownEvent, RestoreField, RoutingStrategies.Tunnel);
        ChangeModel();
    }

    /// <summary>用户切换显式位置且纯草稿已经同步后，通知父面板提交；模型加载不触发此事件。</summary>
    public event EventHandler? ExplicitPositionChanged;

    /// <summary>请求所属面板恢复自动位置，工程动画的重置由所属协调服务决定。</summary>
    public event EventHandler? AutomaticPositionRequested;

    /// <summary>完整九宫格操作已原子更新纯草稿后通知所属面板提交。</summary>
    public event EventHandler? PresetPositionChanged;

    /// <summary>将验证焦点定位到该编辑器内部的数值字段。</summary>
    public bool FocusInvalidField(string fieldKey)
    {
        if (fieldKey is "AutomaticPositionMode" or "CustomPositionMode")
        {
            var mode = fieldKey == "AutomaticPositionMode" || !customMode.IsEffectivelyEnabled ? automaticMode : customMode;
            mode.BringIntoView();
            return mode.Focus();
        }
        if (model?.IsExplicit != true)
        {
            return false;
        }
        foreach (var vector in new[] { "AnchorInput", "PivotInput", "OffsetInput" })
        {
            if (this.FindControl<VectorDraftInput>(vector)!.FocusField(fieldKey))
            {
                return true;
            }
        }

        var control = this.FindControl<Control>(fieldKey);
        if (control is null)
        {
            return false;
        }

        control.BringIntoView();
        return control.Focus();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DraftProperty)
        {
            ChangeModel();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        ChangeModel();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void RestoreField(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || args.Source is not Control source || model is null)
        {
            return;
        }

        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input?.Name is { } fieldKey && model.RestoreField(fieldKey))
        {
            args.Handled = true;
        }
    }

    private void ChangeModel()
    {
        if (draftRoot is null || presets is null)
        {
            return;
        }
        var previousSynchronization = synchronizingModes;
        synchronizingModes = true;
        try
        {
            if (model is not null)
            {
                model.PropertyChanged -= ModelChanged;
            }
            model = Draft ?? DataContext as LayerPositionDraft;
            draftRoot.DataContext = model;
            if (attached && model is not null)
            {
                model.PropertyChanged += ModelChanged;
            }
            RefreshSelection();
        }
        finally
        {
            synchronizingModes = previousSynchronization;
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayerPositionDraft.AnchorSelection) or nameof(LayerPositionDraft.PivotSelection) or
            nameof(LayerPositionDraft.IsExplicit) or nameof(LayerPositionDraft.CanCustomize))
        {
            RefreshSelection();
        }
    }

    private void RefreshSelection()
    {
        presets.SetSelection(model?.AnchorSelection, model?.PivotSelection);
        RefreshModes();
    }

    private void RefreshModes()
    {
        var previousSynchronization = synchronizingModes;
        synchronizingModes = true;
        try
        {
            automaticMode.IsChecked = model is { IsExplicit: false };
            customMode.IsChecked = model is { IsExplicit: true };
            automaticMode.IsEnabled = model is not null;
            customMode.IsEnabled = model?.CanCustomize == true;
        }
        finally
        {
            synchronizingModes = previousSynchronization;
        }
    }

    private void ModeChanged(object? sender, RoutedEventArgs args)
    {
        if (!attached || synchronizingModes || sender is not SegmentedRadioButton { IsChecked: true } mode ||
            model is not { } draft || !mode.IsEffectivelyEnabled)
        {
            return;
        }
        var isExplicit = ReferenceEquals(mode, customMode);
        if (draft.IsExplicit != isExplicit)
        {
            draft.IsExplicit = isExplicit;
            ExplicitPositionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
