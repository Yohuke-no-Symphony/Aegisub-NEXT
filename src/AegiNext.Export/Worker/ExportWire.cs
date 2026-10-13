using AegiNext.Export.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegiNext.Export;

internal static class ExportWire
{
    internal const int VERSION = 5;
    internal const string CANCEL_COMMAND = "cancel";
    internal static JsonSerializerOptions Options { get; } = Create();

    internal static void ValidateJob(ExportWorkerJob job)
    {
        if (job.ProtocolVersion != VERSION || job.Operation is not ExportWorkerOperation.BURN_IN and
            not ExportWorkerOperation.SUBTITLE_MUX ||
            job.Operation == ExportWorkerOperation.SUBTITLE_MUX && job.Video is not null ||
            job.Operation == ExportWorkerOperation.BURN_IN &&
            (job.Video is null || job.Video.RateControlMode is not VideoRateControlMode.CRF and
                not VideoRateControlMode.VBR and not VideoRateControlMode.CBR and not VideoRateControlMode.CQ))
        {
            throw new InvalidDataException("The export worker request protocol, operation, or operation-specific parameters do not match; use the application and worker from the same build configuration.");
        }
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
