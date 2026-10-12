using Spectre.Console;

namespace AegiNext.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        System.Console.CancelKeyPress += handler;
        try
        {
            var interactive = !System.Console.IsInputRedirected && !System.Console.IsOutputRedirected;
            var console = AnsiConsole.Create(new() { Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No });
            var runtime = new CliRuntime(console, interactive);
            var application = new CliApplication(runtime);
            var exporter = new ExportRunner(runtime, new VideoExportService());
            application.RegisterInstance(exporter);
            application.RegisterCommand<ExportCommand>("export", "Export project video with burned-in subtitles", token => exporter.RunAsync(new(), token));
            return await application.RunAsync(args, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            System.Console.CancelKeyPress -= handler;
        }
    }
}
