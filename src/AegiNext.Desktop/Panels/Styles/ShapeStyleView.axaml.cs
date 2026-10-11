using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Styles;

internal sealed partial class ShapeStyleView : UserControl, IWorkbenchPanelView
{
    private readonly StyleEditorInputController input;
    private readonly ShapeStyleViewModel viewModel;
    private readonly ComboBox shapeType;
    private readonly LayerPositionEditor position;

    internal ShapeStyleView(ShapeStyleViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        input = new(this, viewModel, session);
        viewModel.FillDraft.Committed += OnColorCommitted;
        viewModel.StrokeDraft.Committed += OnColorCommitted;
        shapeType = this.FindControl<ComboBox>("ShapeTypeInput")!;
        shapeType.SelectionChanged += OnShapeTypeChanged;
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
    private void OnColorCommitted(object? sender, ColorDraftCommittedEventArgs e) => viewModel.CommitDrafts();
    private void OnShapeTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewModel.HasDrafts)
        {
            viewModel.CommitDrafts();
        }
    }
    public void Dispose()
    {
        viewModel.FillDraft.Committed -= OnColorCommitted;
        viewModel.StrokeDraft.Committed -= OnColorCommitted;
        shapeType.SelectionChanged -= OnShapeTypeChanged;
        position.PresetPositionChanged -= OnPositionPresetChanged;
        input.Dispose();
    }
}
