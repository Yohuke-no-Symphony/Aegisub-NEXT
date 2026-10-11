using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace AegiNext.Media.Processes;

/// <summary>运行具有超时、输出上限和进程树取消保障的媒体工具。</summary>
public static class MediaToolProcess
{
    /// <summary>执行参数列表并等待退出；失败、超限或取消时回收子进程及其输出读取。</summary>
    public static async Task<MediaToolProcessOutput> RunAsync(string executablePath, IReadOnlyList<string> arguments,
        TimeSpan timeout, int outputLimit, int errorLimit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(errorLimit);
        using var deadline = new CancellationTokenSource(timeout);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true),
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = ReadBoundedAsync(process.StandardOutput, outputLimit, lifetime);
        var stderr = ReadBoundedAsync(process.StandardError, errorLimit, lifetime);
        try
        {
            process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(lifetime.Token)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await StopAsync(process).ConfigureAwait(false);
            await ((Task)Task.WhenAll(stdout, stderr)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            cancellationToken.ThrowIfCancellationRequested();
            RethrowReadFailure(stdout);
            RethrowReadFailure(stderr);
            if (deadline.IsCancellationRequested)
            {
                throw new TimeoutException($"媒体探测超过 {timeout.TotalSeconds} 秒，子进程已停止。");
            }

            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int limit, CancellationTokenSource lifetime)
    {
        try
        {
            var result = new StringBuilder(Math.Min(limit, 4096));
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer, lifetime.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    return result.ToString();
                }

                if (count > limit - result.Length)
                {
                    throw new InvalidDataException($"媒体工具输出超过 {limit} 个字符限制。");
                }

                result.Append(buffer, 0, count);
            }
        }
        catch
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void RethrowReadFailure(Task<string> task)
    {
        if (task.Exception?.InnerException is { } exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }
    }

    private static async Task StopAsync(Process process)
    {
        Exception? terminationError = null;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            terminationError = error;
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        if (terminationError is not null)
        {
            ExceptionDispatchInfo.Capture(terminationError).Throw();
        }
    }
}
