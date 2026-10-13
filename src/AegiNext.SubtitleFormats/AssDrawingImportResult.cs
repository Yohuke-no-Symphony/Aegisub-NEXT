using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssDrawingImportResult(ImmutableArray<ProjectLayer> Layers,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics, bool HasDrawing);
