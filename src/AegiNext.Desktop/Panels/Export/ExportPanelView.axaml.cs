using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.Export;

internal sealed partial class ExportPanelView : UserControl, IWorkbenchPanelView
{
    private readonly ExportPanelViewModel viewModel;
    private readonly ComboBox[] choices;
    private bool disposed;

    internal ExportPanelView(ExportPanelViewModel viewModel, WorkbenchSession session)
    {
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        choices = [this.FindControl<ComboBox>("CodecCombo")!, this.FindControl<ComboBox>("ContainerCombo")!, this.FindControl<ComboBox>("SpeedCombo")!,
            this.FindControl<ComboBox>("AudioModeCombo")!, this.FindControl<ComboBox>("QualityModeCombo")!,
            this.FindControl<ComboBox>("BitrateModeCombo")!];
        viewModel.ChoicesRefreshing += OnChoicesRefreshing;
        viewModel.ChoicesRefreshed += OnChoicesRefreshed;
    }
    public string PanelId => "export";

    /// <inheritdoc />
    public void CancelGestures()
    {
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
    }
    /// <inheritdoc />
    public void FocusInvalidField(string? fieldKey) => this.FindControl<Control>(fieldKey ?? "CodecCombo")?.Focus();

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.ChoicesRefreshing -= OnChoicesRefreshing;
            viewModel.ChoicesRefreshed -= OnChoicesRefreshed;
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
        var selection = new[] { viewModel.Codec, viewModel.Container, viewModel.Speed, viewModel.AudioMode, viewModel.QualityMode, viewModel.BitrateMode };
        for (var index = 0; index < choices.Length; index++)
        {
            choices[index].EndInit();
            choices[index].SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, selection[index]);
        }
    }
}
