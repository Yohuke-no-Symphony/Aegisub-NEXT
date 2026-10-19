using System.Collections.Immutable;

namespace AegiNext.Export.Worker;

internal sealed record ExportWorkerFontFamily(string FamilyName, ImmutableArray<ExportWorkerFontFile> Files);
