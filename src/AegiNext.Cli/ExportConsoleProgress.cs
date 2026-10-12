using System.Diagnostics;
using System.Globalization;
using AegiNext.Export;
using Spectre.Console;

namespace AegiNext.Cli;

internal sealed class ExportConsoleProgress(IAnsiConsole console, StatusContext? status = null) : IProgress<VideoExportProgress>
{
    private long lastReport;

    /// <inheritdoc />
    public void Report(VideoExportProgress value)
    {
        var now = Stopwatch.GetTimestamp();
        if (value.Stage != "complete" && lastReport != 0 && Stopwatch.GetElapsedTime(lastReport, now) < TimeSpan.FromMilliseconds(500))
        {
            return;
        }
        lastReport = now;
        var percent = value.Fraction is >= 0 and <= 1
            ? $" {value.Fraction.Value.ToString("P0", CultureInfo.InvariantCulture)}" : string.Empty;
        var text = $"{value.Stage}: {value.FrameCount} frames, time {value.Position}, encoder {value.Encoder ?? "pending"}{percent}";
        if (status is not null)
        {
            status.Status(Markup.Escape(text));
        }
        else
        {
            console.WriteLine(text);
        }
    }
}
