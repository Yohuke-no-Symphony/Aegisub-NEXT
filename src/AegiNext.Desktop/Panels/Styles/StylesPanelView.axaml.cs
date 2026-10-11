using System.ComponentModel;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class StylesPanelView : UserControl, IWorkbenchPanelView
{
    private readonly StylesPanelViewModel viewModel;
    private readonly ContentControl host;
    private readonly Dictionary<StyleEditorViewModel, UserControl> views;
    private IWorkbenchPanelView? activeView;

    internal StylesPanelView(StylesPanelViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        host = this.FindControl<ContentControl>("StyleEditorHost")!;
        views = new()
        {
            [viewModel.Subtitle] = new SubtitleStyleView(viewModel.Subtitle, session),
            [viewModel.Image] = new ImageStyleView(viewModel.Image, session),
            [viewModel.Shape] = new ShapeStyleView(viewModel.Shape, session)
        };
        viewModel.PropertyChanged += OnEditorChanged;
        ShowEditor();
    }

    public string PanelId => "styles";
    public void CancelGestures() => activeView?.CancelGestures();
    public void FocusInvalidField(string? fieldKey) => activeView?.FocusInvalidField(fieldKey);

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StylesPanelViewModel.ActiveEditor))
        {
            ShowEditor();
        }
    }

    private void ShowEditor()
    {
        activeView?.CancelGestures();
        var view = viewModel.ActiveEditor is { } editor ? views[editor] : null;
        activeView = view as IWorkbenchPanelView;
        host.Content = view;
    }

    public void Dispose()
    {
        viewModel.PropertyChanged -= OnEditorChanged;
        foreach (var view in views.Values.OfType<IWorkbenchPanelView>())
        {
            view.Dispose();
        }
    }
}
