using AegiNext.Export.Worker;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegiNext.Export;

internal static class ExportWire
{
    internal const int VERSION = 4;
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
            throw new InvalidDataException("导出 worker 请求协议、操作或专属参数不匹配，请使用同配置的应用和 worker。");
        }
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
