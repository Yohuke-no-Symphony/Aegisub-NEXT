using System.Text.Json;
using System.Text.Json.Serialization;

namespace AegiNext.Export;

internal static class ExportWire
{
    internal const int VERSION = 3;
    internal static JsonSerializerOptions Options { get; } = Create();

    internal static void ValidateJob(ExportWorkerJob job)
    {
        if (job.ProtocolVersion != VERSION || job.RateControlMode is not VideoRateControlMode.CRF and
            not VideoRateControlMode.VBR and not VideoRateControlMode.CBR and not VideoRateControlMode.CQ)
        {
            throw new InvalidDataException("导出 worker 请求协议或显式码控模式不匹配，请使用同配置的应用和 worker。");
        }
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
