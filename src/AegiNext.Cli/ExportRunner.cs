using System.Globalization;
using AegiNext.Application;
using AegiNext.Export;
using Spectre.Console;

namespace AegiNext.Cli;

internal sealed class ExportRunner(CliRuntime runtime, IVideoExportService service, TimeProvider? clock = null)
{
    internal async Task<int> RunAsync(ExportSettings options, CancellationToken cancellationToken)
    {
        var fullWizard = options.IsEmpty;
        var initialConfiguration = ExportOptions.Resolve(options);
        if (options.Output is { } explicitOutput)
        {
            _ = CliPaths.ValidateExplicitOutput(explicitOutput,
                VideoExportCapabilities.Get(initialConfiguration.Settings.Codec).Extensions, initialConfiguration.Extension);
        }
        if (options.Input is not null)
        {
            options.Input = CliPaths.ResolveInput(options.Input, ".aeginext");
            _ = CliPaths.ResolveOutput(options.Input, options.Output, initialConfiguration.Extension, true, clock);
        }
        else
        {
            runtime.RequireInteractive(options.NonInteractive);
            options.Input = await PromptInputAsync(cancellationToken).ConfigureAwait(false);
        }
        if (fullWizard)
        {
            await FillWizardAsync(options, cancellationToken).ConfigureAwait(false);
        }
        var configuration = ExportOptions.Resolve(options);
        var input = CliPaths.ResolveInput(options.Input, ".aeginext");
        var output = CliPaths.ResolveOutput(input, options.Output, configuration.Extension, true, clock);
        var project = await ProjectStore.LoadAsync(input, cancellationToken).ConfigureAwait(false);
        if (project.Media is null || project.Width % 2 != 0 || project.Height % 2 != 0)
        {
            throw new CliUsageException("Video export requires a media binding and a project canvas with even width and height.");
        }
        var request = new VideoExportRequest(project, Path.GetDirectoryName(input)!, output)
            .WithSettings(configuration.Settings) with { DecodeMode = configuration.DecodeMode };
        PrintSummary(request);
        VideoExportResult result;
        if (runtime.CanPrompt(options))
        {
            result = await runtime.Console.Status().StartAsync("Preparing video export...", context =>
                service.ExportAsync(request, new ExportConsoleProgress(runtime.Console, context), cancellationToken))
                .ConfigureAwait(false);
        }
        else
        {
            result = await service.ExportAsync(request, new ExportConsoleProgress(runtime.Console), cancellationToken)
                .ConfigureAwait(false);
        }
        runtime.Console.WriteLine($"Exported: {result.OutputPath} ({result.Frames} frames, encoder {result.Encoder ?? "unknown"})");
        return 0;
    }

    private async Task<string> PromptInputAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var input = await runtime.PromptTextAsync("Project file path", cancellationToken).ConfigureAwait(false);
            try
            {
                return CliPaths.ResolveInput(input, ".aeginext");
            }
            catch (CliUsageException error)
            {
                runtime.Console.WriteLine(error.Message);
            }
        }
    }

    private async Task FillWizardAsync(ExportSettings options, CancellationToken cancellationToken)
    {
        options.Encoding = await runtime.SelectAsync("Video codec", ["auto", "h264", "hevc", "vp8", "vp9", "av1"], cancellationToken)
            .ConfigureAwait(false);
        var initial = ExportOptions.Resolve(options);
        var capability = VideoExportCapabilities.Get(initial.Settings.Codec);
        if (capability.SupportsHardwareOnCurrentPlatform)
        {
            options.UseGpu = await runtime.ConfirmAsync("Use GPU encoding (fail if unavailable)?", cancellationToken).ConfigureAwait(false);
        }
        var qualityMode = capability.QualityMode.ToString().ToLowerInvariant();
        var mode = await runtime.SelectAsync("Rate control", options.UseGpu ? ["vbr", "cbr"] : [qualityMode, "vbr", "cbr"], cancellationToken)
            .ConfigureAwait(false);
        options.Crf = mode == "crf";
        options.Cq = mode == "cq";
        options.Vbr = mode == "vbr";
        options.Cbr = mode == "cbr";
        if (options.Crf || options.Cq)
        {
            options.Quality = await PromptIntegerAsync($"Quality ({capability.QualityMinimum}–{capability.QualityMaximum})",
                capability.DefaultQuality, capability.QualityMinimum, capability.QualityMaximum, cancellationToken).ConfigureAwait(false);
        }
        if (!options.Crf)
        {
            while (true)
            {
                options.VideoBitrate = await runtime.PromptTextAsync("Video bitrate (Mbps, at most three decimal places)", cancellationToken, "8")
                    .ConfigureAwait(false);
                try
                {
                    _ = ExportOptions.Resolve(options);
                    break;
                }
                catch (CliUsageException error)
                {
                    runtime.Console.WriteLine(error.Message);
                }
            }
        }
        options.Speed = await runtime.SelectAsync("Encoding speed", ["medium", "ultrafast", "superfast", "veryfast", "faster", "fast", "slow", "slower", "veryslow"],
            cancellationToken).ConfigureAwait(false);
        options.Audio = await runtime.SelectAsync("Audio mode", ["copy", "aac", "opus", "none"], cancellationToken).ConfigureAwait(false);
        if (options.Audio is "aac" or "opus")
        {
            options.AudioBitrate = await PromptIntegerAsync("Audio bitrate (kbps, 32–512)", 192, 32, 512, cancellationToken).ConfigureAwait(false);
        }
        options.Decode = await runtime.SelectAsync("Video decode mode", ["auto", "software", "hardware"], cancellationToken).ConfigureAwait(false);
        var containers = capability.Extensions.Where(extension => extension != ".webm" || options.Audio != "aac")
            .Select(extension => extension[1..]);
        var defaultContainer = initial.Extension[1..];
        options.Container = await runtime.SelectAsync("Output container", containers.OrderBy(value => value != defaultContainer), cancellationToken)
            .ConfigureAwait(false);
        while (true)
        {
            var output = await runtime.PromptTextAsync("Output file or directory (leave blank to use the project output directory)", cancellationToken, allowEmpty: true)
                .ConfigureAwait(false);
            options.Output = string.IsNullOrWhiteSpace(output) ? null : output;
            try
            {
                var configuration = ExportOptions.Resolve(options);
                _ = CliPaths.ResolveOutput(options.Input!, options.Output, configuration.Extension, true, clock);
                break;
            }
            catch (Exception error) when (error is CliUsageException or ArgumentException)
            {
                runtime.Console.WriteLine(error.Message);
            }
        }
    }

    private async Task<int> PromptIntegerAsync(string title, int defaultValue, int minimum, int maximum,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var text = await runtime.PromptTextAsync(title, cancellationToken, defaultValue.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(false);
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum)
            {
                return value;
            }
            runtime.Console.WriteLine($"Enter an integer between {minimum} and {maximum}.");
        }
    }

    private void PrintSummary(VideoExportRequest request)
    {
        var settings = request.ToSettings();
        var rate = settings.RateControlMode == VideoRateControlMode.CRF ? $"quality {settings.Quality}" :
            settings.RateControlMode == VideoRateControlMode.CQ ?
                $"quality {settings.Quality}, {settings.VideoBitrate / 1_000_000m} Mbps" : $"{settings.VideoBitrate / 1_000_000m} Mbps";
        runtime.Console.WriteLine($"Export: {settings.Codec} / {settings.EncodingMode} / {settings.RateControlMode} ({rate}) / {settings.Preset}");
        runtime.Console.WriteLine($"Audio: {settings.AudioMode}" + (settings.AudioMode is AudioExportMode.Aac or AudioExportMode.Opus
            ? $" ({settings.AudioBitrate / 1000} kbps)" : string.Empty));
        runtime.Console.WriteLine($"Decode: {request.DecodeMode}; output: {request.OutputPath}");
    }
}
