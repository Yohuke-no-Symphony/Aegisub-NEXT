using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal sealed class CanvasDrawingCommandEventArgs(CanvasDrawingCommand command, ProjectDocument source,
    Guid? layerId, CanvasEditMode mode, bool isQuery) : EventArgs
{
    internal CanvasDrawingCommand Command { get; } = command;
    internal ProjectDocument Source { get; } = source;
    internal Guid? LayerId { get; } = layerId;
    internal CanvasEditMode Mode { get; } = mode;
    internal bool IsQuery { get; } = isQuery;
    internal bool Handled { get; set; }
}
