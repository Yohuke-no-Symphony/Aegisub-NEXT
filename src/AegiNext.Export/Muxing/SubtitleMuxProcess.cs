using System.Diagnostics;
using System.Globalization;
using System.Text;
using AegiNext.Core.Timing;

namespace AegiNext.Export.Muxing;

internal static class SubtitleMuxProcess
{
    internal static Task RunAsync(string executable, IReadOnlyList<string> arguments,
        Action<MediaTime> reportPosition, CancellationToken cancellationToken)
    {
        return RunToolAsync(executable, arguments, line =>
        {
            const string PREFIX = "out_time_us=";
            if (line.StartsWith(PREFIX, StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(PREFIX.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds))
            {
                reportPosition(new(microseconds, 1000000));
            }
        }, true, cancellationToken);
    }

    internal static Task RunProbeAsync(string executable, IReadOnlyList<string> arguments,
        Action<string> readPacket, CancellationToken cancellationToken)
    {
        return RunToolAsync(executable, arguments, readPacket, false, cancellationToken);
    }

    private static async Task RunToolAsync(string executable, IReadOnlyList<string> arguments,
        Action<string> readLine, bool muxing, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = new CancellationTokenSource(TimeSpan.FromHours(24));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var process = new Process();
        process.StartInfo = new()
        {
            FileName = executable, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        process.StandardInput.Close();
        using var cancelled = lifetime.Token.Register(static state => Stop((Process)state!), process);
        var errors = ReadErrorsAsync(process.StandardError);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                if (line.Length > 4096)
                {
                    throw new InvalidDataException("媒体工具消息超过限制。");
                }

                readLine(line);
            }

            await process.WaitForExitAsync(lifetime.Token).ConfigureAwait(false);
            var diagnostics = await errors.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                if (muxing)
                {
                    throw new NotSupportedException($"目标容器不能原样保存全部源流或新增字幕；请选择兼容容器。{Environment.NewLine}{diagnostics}");
                }

                throw new InvalidDataException($"无法校验音频包：{diagnostics}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("媒体工具超过 24 小时，子进程已停止。");
        }
        finally
        {
            Stop(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            result.Append(buffer, 0, Math.Min(count, Math.Max(0, 65536 - result.Length)));
        }

        return result.ToString();
    }
}
