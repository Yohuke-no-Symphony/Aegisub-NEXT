using System.Text.Json;

namespace AegiNext.Export.Worker;

internal sealed class ExportWorkerMessageWriter(TextWriter output)
{
    internal ExportWorkerOperation Operation { get; set; }

    internal void Send(ExportWorkerMessage message)
    {
        output.WriteLine(JsonSerializer.Serialize(message with { Operation = Operation }, ExportWire.Options));
        output.Flush();
    }
}
