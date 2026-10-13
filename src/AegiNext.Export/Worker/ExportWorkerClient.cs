using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AegiNext.Export.Worker;

internal static class ExportWorkerClient
{
    private const int MESSAGE_LENGTH_LIMIT = 65536;
    private const int DIAGNOSTIC_LENGTH_LIMIT = 65536;
    private const int CANCELLATION_GRACE_SECONDS = 5;

    internal static async Task<ExportWorkerMessage> RunAsync(string serializedJob, string? workerPath,
        string workingDirectory, Action<ExportWorkerMessage>? reportProgress, CancellationToken cancellationToken,
        TimeSpan? cancellationGracePeriod = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gracePeriod = cancellationGracePeriod ?? TimeSpan.FromSeconds(CANCELLATION_GRACE_SECONDS);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(gracePeriod, TimeSpan.Zero);
        var executable = OperatingSystem.IsWindows() ? "aegn-exporter.exe" : "aegn-exporter";
        var worker = workerPath ?? Path.Combine(AppContext.BaseDirectory,
            File.Exists(Path.Combine(AppContext.BaseDirectory, executable)) ? executable : "aegn-exporter.dll");
        if (!Path.IsPathFullyQualified(worker) || !File.Exists(worker))
        {
            throw new FileNotFoundException("The adjacent aegn-exporter is missing; deploy the complete application.", worker);
        }

        var start = new ProcessStartInfo
        {
            FileName = worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? ExportExecutable.Resolve("dotnet", Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")) : worker,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        if (worker.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(worker);
        }

        using var process = new Process();
        process.StartInfo = start;
        if (!process.Start())
        {
            throw new InvalidOperationException("The export worker could not be started.");
        }

        using var inputGate = new SemaphoreSlim(1, 1);
        using var finished = new CancellationTokenSource();
        var cancellation = Task.CompletedTask;
        var errors = ReadErrorsAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteLineAsync(serializedJob.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            cancellation = CancelWhenRequestedAsync(process, inputGate, gracePeriod, finished.Token, cancellationToken);
            ExportWorkerMessage? completed = null;
            string? failure = null;
            while (await process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
            {
                if (line.Length > MESSAGE_LENGTH_LIMIT)
                {
                    throw new InvalidDataException("The export worker message exceeds the size limit.");
                }

                var message = JsonSerializer.Deserialize<ExportWorkerMessage>(line, ExportWire.Options)
                    ?? throw new InvalidDataException("The export worker returned an empty message.");
                if (message.Type == "progress")
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        reportProgress?.Invoke(message);
                    }
                }
                else if (message.Type == "complete")
                {
                    completed = message;
                    break;
                }
                else if (message.Type == "error")
                {
                    failure = message.Error;
                    break;
                }
            }

            await finished.CancelAsync().ConfigureAwait(false);
            await cancellation.ConfigureAwait(false);
            await CloseInputAsync(process, inputGate).ConfigureAwait(false);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            var diagnostics = await errors.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0 || completed is null)
            {
                throw new InvalidOperationException(failure ?? $"Export failed: {diagnostics}");
            }

            return completed;
        }
        finally
        {
            await finished.CancelAsync().ConfigureAwait(false);
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    Kill(process);
                }
                finally
                {
                    try
                    {
                        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            await CloseInputAsync(process, inputGate).ConfigureAwait(false);
                        }
                        finally
                        {
                            await errors.ConfigureAwait(false);
                        }
                    }
                }
            }
        }
    }

    private static async Task CancelWhenRequestedAsync(Process process, SemaphoreSlim inputGate,
        TimeSpan gracePeriod, CancellationToken finished, CancellationToken cancellationToken)
    {
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), requested);
        try
        {
            await requested.Task.WaitAsync(finished).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (finished.IsCancellationRequested)
        {
        }

        if (!cancellationToken.IsCancellationRequested || process.HasExited)
        {
            return;
        }

        using var deadline = new CancellationTokenSource(gracePeriod);
        try
        {
            await SendCancellationAsync(process, inputGate, deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Kill(process);
        }
        catch (IOException)
        {
            Kill(process);
        }
        catch (InvalidOperationException)
        {
            Kill(process);
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task SendCancellationAsync(Process process, SemaphoreSlim inputGate, CancellationToken cancellationToken)
    {
        await inputGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(ExportWire.CANCEL_COMMAND.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }
        finally
        {
            inputGate.Release();
        }
    }

    private static async Task CloseInputAsync(Process process, SemaphoreSlim inputGate)
    {
        await inputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
        }
        finally
        {
            inputGate.Release();
        }
    }

    private static void Kill(Process process)
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
        catch (System.ComponentModel.Win32Exception) when (process.HasExited)
        {
        }
    }

    private static async Task<string> ReadErrorsAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0)
            {
                return output.ToString();
            }

            output.Append(buffer, 0, Math.Min(count, Math.Max(0, DIAGNOSTIC_LENGTH_LIMIT - output.Length)));
        }
    }
}
