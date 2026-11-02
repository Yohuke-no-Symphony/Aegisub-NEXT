using System.Diagnostics;

namespace AegiNext.Desktop.Diagnostics;

/// <summary>
/// Optional file sink for debugging. Enabled only when <c>AEGINEXT_DEBUG_LOG</c> is set to a file path,
/// or to <c>1</c> for the default <c>%APPDATA%\AegiNext\logs\debug.log</c>; otherwise every call is a no-op.
/// </summary>
internal static class DebugLogFile
{
    internal const string ENVIRONMENT_VARIABLE = "AEGINEXT_DEBUG_LOG";
    private static readonly object gate = new();
    private static string? path;
    private static bool resolved;

    internal static string? Path
    {
        get
        {
            lock (gate)
            {
                if (!resolved)
                {
                    path = Resolve(Environment.GetEnvironmentVariable(ENVIRONMENT_VARIABLE));
                    resolved = true;
                }
                return path;
            }
        }
    }

    internal static void Write(string text)
    {
        if (Path is not { } target)
        {
            return;
        }

        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.AppendAllText(target, text + Environment.NewLine);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Debug output must never affect the application.
        }
    }

    /// <summary>Starts capturing unhandled exceptions and Avalonia trace output when the sink is enabled.</summary>
    internal static void Attach()
    {
        if (Path is null)
        {
            return;
        }

        Write($"{DateTimeOffset.Now:O}  START  pid={Environment.ProcessId}  {Environment.ProcessPath}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write($"{DateTimeOffset.Now:O}  FATAL  [UnhandledException] terminating={args.IsTerminating}{Environment.NewLine}{args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            Write($"{DateTimeOffset.Now:O}  ERROR  [UnobservedTask]{Environment.NewLine}{args.Exception}");
        Trace.Listeners.Add(new FileTraceListener());
    }

    internal static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim() is "1" or "true"
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AegiNext", "logs", "debug.log")
            : System.IO.Path.GetFullPath(value.Trim());
    }

    private sealed class FileTraceListener : TraceListener
    {
        public override void Write(string? message) => DebugLogFile.Write($"{DateTimeOffset.Now:O}  TRACE  {message}");

        public override void WriteLine(string? message) => Write(message);
    }
}
