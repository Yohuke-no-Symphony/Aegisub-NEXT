using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class ImageStyleView : UserControl, IWorkbenchPanelView
{
    private readonly StyleEditorInputController input;
    private readonly ImageStyleViewModel viewModel;
    private readonly ComboBox fit;
    private readonly ComboBox playbackMode;
    private readonly ToolbarToggleButton animation;
    private readonly LayerPositionEditor position;

    internal ImageStyleView(ImageStyleViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        input = new(this, viewModel, session);
        fit = this.FindControl<ComboBox>("ImageFitInput")!;
        fit.SelectionChanged += OnFitChanged;
        playbackMode = this.FindControl<ComboBox>("ImagePlaybackModeInput")!;
        playbackMode.SelectionChanged += OnFitChanged;
        animation = this.FindControl<ToolbarToggleButton>("ImageAnimationEnabledInput")!;
        animation.IsCheckedChanged += OnAnimationChanged;
        position = this.FindControl<LayerPositionEditor>("PositionEditor")!;
        position.PresetPositionChanged += OnPositionPresetChanged;
    }

    public string PanelId => "styles";
    public void CancelGestures() => input.CancelGestures();
    public void FocusInvalidField(string? fieldKey)
    {
        if (fieldKey is null || !position.FocusInvalidField(fieldKey))
        {
            input.FocusInvalidField(fieldKey);
        }
    }
    private void OnPositionPresetChanged(object? sender, EventArgs e) => viewModel.CommitDrafts();
    private void OnFitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewModel.HasDrafts)
        {
            viewModel.CommitDrafts();
        }
    }
    private void OnAnimationChanged(object? sender, RoutedEventArgs e)
    {
        if (viewModel.HasDrafts)
        {
            viewModel.CommitDrafts();
        }
    }
    public void Dispose()
    {
        fit.SelectionChanged -= OnFitChanged;
        playbackMode.SelectionChanged -= OnFitChanged;
        animation.IsCheckedChanged -= OnAnimationChanged;
        position.PresetPositionChanged -= OnPositionPresetChanged;
        input.Dispose();
    }
}
