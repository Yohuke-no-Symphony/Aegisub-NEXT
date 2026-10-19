using AegiNext.Export.Worker;
using System.Collections.Immutable;
using System.Text.Json;

namespace AegiNext.Export;

internal sealed record ExportWorkerJob(JsonElement Project, string ProjectDirectory, string TemporaryDirectory,
    string Extension, string FfmpegPath, ExportWorkerOperation Operation = ExportWorkerOperation.UNKNOWN,
    ExportWorkerVideoOptions? Video = null, int ProtocolVersion = 0)
{
    public ImmutableArray<ExportWorkerFontFamily> FontFamilies { get; init; } = [];
}
