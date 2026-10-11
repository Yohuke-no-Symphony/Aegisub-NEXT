using System.Text.Json;

namespace AegiNext.Export.Worker;

internal sealed class ExportWorkerMessageWriter(TextWriter output)
{
    internal void Send(ExportWorkerMessage message)
    {
        output.WriteLine(JsonSerializer.Serialize(message, ExportWire.Options));
        output.Flush();
    }
}
