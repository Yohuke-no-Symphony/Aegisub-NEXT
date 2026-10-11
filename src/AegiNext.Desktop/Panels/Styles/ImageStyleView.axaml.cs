using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class ImageStyleView : UserControl, IWorkbenchPanelView
{
    private readonly StyleEditorInputController input;
    private readonly ImageStyleViewModel viewModel;
    private readonly ComboBox fit;

    internal ImageStyleView(ImageStyleViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        input = new(this, viewModel, session);
        fit = this.FindControl<ComboBox>("ImageFitInput")!;
        fit.SelectionChanged += OnFitChanged;
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
    public void Dispose()
    {
        fit.SelectionChanged -= OnFitChanged;
        input.Dispose();
    }
}
