using System.Globalization;
using System.Text.Json;

namespace AegiNext.Export.TestHost;

internal static class ExportCancellationTestHost
{
    private static async Task<int> Main()
    {
        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "block-request")))
        {
            await WriteMarkerAsync(Path.Combine(Environment.CurrentDirectory, "request"), "started").ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
            return 0;
        }

        using var input = new StreamReader(Console.OpenStandardInput());
        var request = await input.ReadLineAsync().ConfigureAwait(false)
            ?? throw new InvalidDataException("The test worker requires a request.");
        using var document = JsonDocument.Parse(request);
        var mode = document.RootElement.GetProperty("mode").GetString();
        var markerPrefix = document.RootElement.GetProperty("markerPrefix").GetString()
            ?? throw new InvalidDataException("The test worker requires marker paths.");
        using var video = new FileStream(Path.Combine(Environment.CurrentDirectory, "video.nut"),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        await WriteMarkerAsync(markerPrefix, "started").ConfigureAwait(false);
        await SendAsync("progress").ConfigureAwait(false);

        switch (mode)
        {
            case "normal":
                return await CompleteAsync(input, video, markerPrefix).ConfigureAwait(false);
            case "terminal-cancel":
                await SendAsync("complete").ConfigureAwait(false);
                return await CancelAsync(input, video, markerPrefix, false).ConfigureAwait(false);
            case "cooperative":
            case "hard-kill":
                return await CancelAsync(input, video, markerPrefix, mode == "hard-kill").ConfigureAwait(false);
            default:
                throw new InvalidDataException("The test worker mode is unsupported.");
        }
    }

    private static async Task<int> CompleteAsync(TextReader input, FileStream video, string markerPrefix)
    {
        var nextCommand = input.ReadLineAsync();
        await WaitForContinuationAsync(markerPrefix).ConfigureAwait(false);
        if (nextCommand.IsCompleted)
        {
            await Console.Error.WriteLineAsync("The request stream was closed before completion.").ConfigureAwait(false);
            return 65;
        }

        await SendAsync("complete").ConfigureAwait(false);
        if (await nextCommand.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false) is not null)
        {
            await Console.Error.WriteLineAsync("Normal completion requires input EOF.").ConfigureAwait(false);
            return 66;
        }

        video.Dispose();
        await WriteMarkerAsync(markerPrefix, "released").ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> CancelAsync(TextReader input, FileStream video, string markerPrefix, bool unresponsive)
    {
        var command = await input.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (command != "cancel")
        {
            await Console.Error.WriteLineAsync("Cancellation requires the cancel command.").ConfigureAwait(false);
            return 67;
        }

        await WriteMarkerAsync(markerPrefix, "received").ConfigureAwait(false);
        if (unresponsive)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(false);
        }

        await SendAsync("progress").ConfigureAwait(false);
        await WaitForContinuationAsync(markerPrefix).ConfigureAwait(false);
        video.Dispose();
        await WriteMarkerAsync(markerPrefix, "released").ConfigureAwait(false);
        return 1;
    }

    private static async Task WaitForContinuationAsync(string markerPrefix)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(markerPrefix + ".continue"))
        {
            await Task.Delay(10, deadline.Token).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync(string type)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { type, frames = 1 })).ConfigureAwait(false);
        await Console.Out.FlushAsync().ConfigureAwait(false);
    }

    private static async Task WriteMarkerAsync(string markerPrefix, string state)
    {
        var marker = markerPrefix + "." + state;
        await File.WriteAllTextAsync(marker + ".writing", Environment.ProcessId.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
        File.Move(marker + ".writing", marker);
    }
}
