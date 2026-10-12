using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssBodyWriteResult(string Text, ImmutableArray<SubtitleFormatDiagnostic> Diagnostics, MediaTime End);
