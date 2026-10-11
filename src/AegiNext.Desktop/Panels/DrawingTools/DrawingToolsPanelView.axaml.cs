using System.ComponentModel;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Styling;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.DrawingTools;

internal sealed partial class DrawingToolsPanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly DrawingToolsPanelViewModel viewModel;
    private readonly Dictionary<ToolbarToggleButton, (CanvasEditMode Mode, ShapeKind? ShapeKind)> tools = [];
    private bool disposed;

    internal DrawingToolsPanelView(DrawingToolsPanelViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        AddTool("SelectGraphicButton", "SelectGraphic", CanvasEditMode.POSITION);
        AddTool("PathToolButton", "DrawPath", CanvasEditMode.CREATE_PATH, ShapeKind.PATH);
        AddTool("CropImageToolButton", "CropImage", CanvasEditMode.CROP);
        AddTool("EditShapePathToolButton", "EditShapePath", CanvasEditMode.SHAPE_PATH);
        AddTool("RectangleToolButton", "Rectangle", CanvasEditMode.CREATE_SHAPE, ShapeKind.RECTANGLE);
        AddTool("EllipseToolButton", "Ellipse", CanvasEditMode.CREATE_SHAPE, ShapeKind.ELLIPSE);
        AddTool("LineToolButton", "StraightSegment", CanvasEditMode.CREATE_SHAPE, ShapeKind.LINE);
        AddTool("ArrowToolButton", "DrawArrow", CanvasEditMode.CREATE_SHAPE, ShapeKind.ARROW);
        this.FindControl<Button>("SubtitleBackgroundButton")!.Content = WorkbenchIcon.Create("SubtitleBackground");
        this.FindControl<Button>("StraightSegmentButton")!.Content = WorkbenchIcon.Create("StraightSegment");
        this.FindControl<Button>("CurveSegmentButton")!.Content = WorkbenchIcon.Create("CurveSegment");
        this.FindControl<Button>("CloseShapePathButton")!.Content = WorkbenchIcon.Create("ClosedPath");
        this.FindControl<Button>("FinishShapePathButton")!.Content = WorkbenchIcon.Create("FinishPath");
        viewModel.PropertyChanged += OnViewModelChanged;
        SynchronizeTools();
    }

    public string PanelId => WorkbenchPanelIds.DRAWING_TOOLS;

    /// <inheritdoc />
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        return command == WorkbenchCommand.END_TEXT_INPUT && viewModel.ActiveMode is
            CanvasEditMode.CREATE_SHAPE or CanvasEditMode.CREATE_PATH or CanvasEditMode.CROP or CanvasEditMode.SHAPE_PATH;
    }

    /// <inheritdoc />
    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        viewModel.SelectTool(CanvasEditMode.POSITION);
        return true;
    }

    /// <inheritdoc />
    public void CancelGestures()
    {
    }

    /// <inheritdoc />
    public void FocusInvalidField(string? fieldKey) => this.FindControl<ToolbarToggleButton>("SelectGraphicButton")!.Focus();

    private void AddTool(string name, string icon, CanvasEditMode mode, ShapeKind? shapeKind = null)
    {
        var button = this.FindControl<ToolbarToggleButton>(name)!;
        button.Content = WorkbenchIcon.Create(icon);
        tools.Add(button, (mode, shapeKind));
        button.Click += OnToolClick;
    }

    private void OnToolClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToolbarToggleButton button && tools.TryGetValue(button, out var tool))
        {
            viewModel.SelectTool(tool.Mode, tool.ShapeKind);
            SynchronizeTools();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => SynchronizeTools();

    private void SynchronizeTools()
    {
        foreach (var (button, tool) in tools)
        {
            button.IsEnabled = viewModel.CanSelectTool(tool.Mode);
            button.IsChecked = viewModel.ActiveMode == tool.Mode &&
                (tool.Mode != CanvasEditMode.CREATE_SHAPE || viewModel.ActiveShapeKind == tool.ShapeKind);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        viewModel.PropertyChanged -= OnViewModelChanged;
        foreach (var button in tools.Keys)
        {
            button.Click -= OnToolClick;
        }
    }
}
