using System.Text;
using Spectre.Console;

namespace AegiNext.Cli;

/// <summary>参数和向导共用的字幕转换与原子写入流程。</summary>
public sealed class ConvertRunner(CliRuntime runtime, ISubtitleConversionService conversion)
{
    /// <summary>验证参数、补齐输入并在接受诊断后提交输出。</summary>
    public async Task<int> RunAsync(ConvertSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fullWizard = settings.Input is null && settings.Target is null && settings.Output is null && !settings.AcceptLoss;
        var input = settings.Input is null ? null : CliPaths.ResolveInput(settings.Input, ".aeginext", ".ass", ".srt");
        var target = settings.Target is null ? null : NormalizeTarget(settings.Target);
        if (settings.Output is not null)
        {
            CliPaths.ValidateExplicitOutput(settings.Output, ["ass", "srt"], target);
            if (input is not null && target is not null)
            {
                CliPaths.ResolveOutput(input, settings.Output, target);
            }
        }
        if ((input is null || target is null) && !runtime.CanPrompt(settings))
        {
            throw new CliUsageException("convert requires --input/-i PATH and --target ass|srt; interactive prompts are disabled.");
        }
        while (input is null)
        {
            var value = await runtime.PromptTextAsync("Input file path:", cancellationToken).ConfigureAwait(false);
            try
            {
                input = CliPaths.ResolveInput(value, ".aeginext", ".ass", ".srt");
            }
            catch (CliUsageException error)
            {
                runtime.Console.WriteLine(error.Message);
            }
        }
        target ??= await runtime.SelectAsync("Select target subtitle format:", ["ass", "srt"], cancellationToken).ConfigureAwait(false);
        var outputOption = settings.Output;
        if (fullWizard)
        {
            var value = await runtime.PromptTextAsync("Output file or directory (leave blank to use the input directory):", cancellationToken, allowEmpty: true).ConfigureAwait(false);
            outputOption = string.IsNullOrWhiteSpace(value) ? null : value;
        }
        var output = CliPaths.ResolveOutput(input, outputOption, target);
        runtime.Console.WriteLine($"Converting: {input}");
        runtime.Console.WriteLine($"Target: {target} -> {output}");
        var result = await conversion.ConvertAsync(input, target, cancellationToken).ConfigureAwait(false);
        if (!result.Diagnostics.IsEmpty)
        {
            var table = new Table().AddColumn("Diagnostic code").AddColumn("Description");
            foreach (var diagnostic in result.Diagnostics)
            {
                table.AddRow(new Text(diagnostic.Code), new Text(diagnostic.Message));
            }
            runtime.Console.Write(table);
            if (!settings.AcceptLoss)
            {
                if (!runtime.CanPrompt(settings))
                {
                    throw new CliUsageException("Conversion diagnostics require --accept-loss before output can be written in non-interactive mode.");
                }
                if (!await runtime.ConfirmAsync("Accept these conversion diagnostics and write the output?", cancellationToken).ConfigureAwait(false))
                {
                    throw new OperationCanceledException("Conversion canceled by user.");
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false, true), leaveOpen: true);
                await writer.WriteAsync(result.Text.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, false);
        }
        finally
        {
            File.Delete(temporary);
        }
        runtime.Console.WriteLine($"Written: {output}");
        return 0;
    }

    private static string NormalizeTarget(string target)
    {
        if (!target.Equals("ass", StringComparison.OrdinalIgnoreCase) && !target.Equals("srt", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--target must be ass or srt.");
        }
        return target.ToLowerInvariant();
    }
}
