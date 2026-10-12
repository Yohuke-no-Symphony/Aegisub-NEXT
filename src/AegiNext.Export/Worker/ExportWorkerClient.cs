using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AegiNext.Export.Worker;

internal static class ExportWorkerClient
{
    private const int MESSAGE_LENGTH_LIMIT = 65536;
    private const int DIAGNOSTIC_LENGTH_LIMIT = 65536;

    internal static async Task<ExportWorkerMessage> RunAsync(string serializedJob, string? workerPath,
        string workingDirectory, Action<ExportWorkerMessage>? reportProgress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

        await using var cancelled = cancellationToken.Register(static state =>
        {
            try
            {
                Kill((Process)state!);
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }, process);
        var errors = ReadErrorsAsync(process.StandardError);
        try
        {
            await process.StandardInput.WriteLineAsync(serializedJob.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
            ExportWorkerMessage? completed = null;
            string? failure = null;
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Length > MESSAGE_LENGTH_LIMIT)
                {
                    throw new InvalidDataException("The export worker message exceeds the size limit.");
                }

                var message = JsonSerializer.Deserialize<ExportWorkerMessage>(line, ExportWire.Options)
                    ?? throw new InvalidDataException("The export worker returned an empty message.");
                if (message.Type == "progress")
                {
                    reportProgress?.Invoke(message);
                }
                else if (message.Type == "complete")
                {
                    completed = message;
                }
                else if (message.Type == "error")
                {
                    failure = message.Error;
                }
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
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
            try
            {
                Kill(process);
            }
            finally
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                await errors.ConfigureAwait(false);
            }
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
