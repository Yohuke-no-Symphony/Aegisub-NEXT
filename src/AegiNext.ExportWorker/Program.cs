using System.Globalization;
using AegiNext.Application;
using AegiNext.Export.Worker;

namespace AegiNext.ExportWorker;

internal static class Program
{
    private static Task<int> Main()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return ExportWorkerHost.RunAsync(Console.In, Console.Out, Console.Error, ProjectStore.Deserialize);
    }
}
