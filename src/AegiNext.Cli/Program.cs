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
            var application = new CliApplication(new(console, interactive));
            return await application.RunAsync(args, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            System.Console.CancelKeyPress -= handler;
        }
    }
}
