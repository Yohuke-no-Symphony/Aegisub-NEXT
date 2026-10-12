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
            throw new CliUsageException("convert 必须填写 --input/-i PATH 和 --target ass|srt；当前环境禁止交互询问。");
        }
        while (input is null)
        {
            var value = await runtime.PromptTextAsync("输入文件路径：", cancellationToken).ConfigureAwait(false);
            try
            {
                input = CliPaths.ResolveInput(value, ".aeginext", ".ass", ".srt");
            }
            catch (CliUsageException error)
            {
                runtime.Console.WriteLine(error.Message);
            }
        }
        target ??= await runtime.SelectAsync("选择目标字幕格式：", ["ass", "srt"], cancellationToken).ConfigureAwait(false);
        var outputOption = settings.Output;
        if (fullWizard)
        {
            var value = await runtime.PromptTextAsync("输出文件或目录（留空使用输入目录）：", cancellationToken, allowEmpty: true).ConfigureAwait(false);
            outputOption = string.IsNullOrWhiteSpace(value) ? null : value;
        }
        var output = CliPaths.ResolveOutput(input, outputOption, target);
        runtime.Console.WriteLine($"转换：{input}");
        runtime.Console.WriteLine($"目标：{target} → {output}");
        var result = await conversion.ConvertAsync(input, target, cancellationToken).ConfigureAwait(false);
        if (!result.Diagnostics.IsEmpty)
        {
            var table = new Table().AddColumn("诊断代码").AddColumn("说明");
            foreach (var diagnostic in result.Diagnostics)
            {
                table.AddRow(new Text(diagnostic.Code), new Text(diagnostic.Message));
            }
            runtime.Console.Write(table);
            if (!settings.AcceptLoss)
            {
                if (!runtime.CanPrompt(settings))
                {
                    throw new CliUsageException("转换存在诊断；脚本需显式提供 --accept-loss 才能写入输出。");
                }
                if (!await runtime.ConfirmAsync("接受上述转换诊断并写入输出？", cancellationToken).ConfigureAwait(false))
                {
                    throw new OperationCanceledException("用户取消转换。");
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
        runtime.Console.WriteLine($"已写入：{output}");
        return 0;
    }

    private static string NormalizeTarget(string target)
    {
        if (!target.Equals("ass", StringComparison.OrdinalIgnoreCase) && !target.Equals("srt", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--target 仅支持 ass 或 srt。");
        }
        return target.ToLowerInvariant();
    }
}
