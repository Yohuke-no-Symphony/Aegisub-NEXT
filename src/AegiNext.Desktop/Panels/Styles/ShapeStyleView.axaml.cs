using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class ShapeStyleView : UserControl, IWorkbenchPanelView
{
    private readonly StyleEditorInputController input;
    private readonly ShapeStyleViewModel viewModel;

    internal ShapeStyleView(ShapeStyleViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        input = new(this, viewModel, session);
        viewModel.FillDraft.Committed += OnColorCommitted;
        viewModel.StrokeDraft.Committed += OnColorCommitted;
    }

    public string PanelId => "styles";
    public void CancelGestures() => input.CancelGestures();
    public void FocusInvalidField(string? fieldKey) => input.FocusInvalidField(fieldKey);
    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs e) => viewModel.CommitDrafts();
    public void Dispose()
    {
        viewModel.FillDraft.Committed -= OnColorCommitted;
        viewModel.StrokeDraft.Committed -= OnColorCommitted;
        input.Dispose();
    }
}
