using AegiNext.Desktop.Workspace;
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
    private readonly CheckBox animation;

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
        animation = this.FindControl<CheckBox>("ImageAnimationEnabledInput")!;
        animation.IsCheckedChanged += OnAnimationChanged;
    }

    public string PanelId => "styles";
    public void CancelGestures() => input.CancelGestures();
    public void FocusInvalidField(string? fieldKey) => input.FocusInvalidField(fieldKey);
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
        input.Dispose();
    }
}
