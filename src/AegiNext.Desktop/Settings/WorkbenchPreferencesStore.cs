using System.Text.Json;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Settings;

/// <summary>原子保存应用偏好，不与工程资产共用目录。</summary>
public sealed class WorkbenchPreferencesStore : IDisposable
{
    internal const int MAXIMUM_FILE_BYTES = 64 * 1024;
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>创建存储；测试可注入专用目录。</summary>
    public WorkbenchPreferencesStore(string? directory = null)
    {
        var baseDirectory = directory ??
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "AegiNext");
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new InvalidOperationException("应用数据目录不可用。");
        }

        DirectoryPath = Path.GetFullPath(baseDirectory);
        path = Path.Combine(DirectoryPath, "preferences.json");
    }

    public Exception? LoadError { get; private set; }
    public string DirectoryPath { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }

    /// <summary>读取设置；缺失使用默认值，损坏或不可读保留诊断供界面显示。</summary>
    public WorkbenchPreferences Load()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(path))
            {
                return new();
            }

            if (new FileInfo(path).Length > MAXIMUM_FILE_BYTES)
            {
                throw new InvalidDataException("偏好文件过大。");
            }

            var bytes = File.ReadAllBytes(path);
            var value = JsonSerializer.Deserialize<WorkbenchPreferences>(bytes)
                        ?? throw new InvalidDataException("偏好文件为空。");
            value = WorkbenchPreferencesMigration.Upgrade(value, bytes);
            value.Validate();
            return value;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                          or JsonException)
        {
            LoadError = error;
            return new();
        }
    }

    internal static byte[] Serialize(WorkbenchPreferences value, bool allowForeignWorkspace = false)
    {
        ValidateTransfer(value, allowForeignWorkspace);
        return SettingsTransferJson.Serialize(value, MAXIMUM_FILE_BYTES);
    }

    internal static WorkbenchPreferences Deserialize(ReadOnlySpan<byte> bytes, bool allowForeignWorkspace = false)
    {
        var value = SettingsTransferJson.Deserialize<WorkbenchPreferences>(bytes, MAXIMUM_FILE_BYTES);
        value = WorkbenchPreferencesMigration.Upgrade(value, bytes);
        ValidateTransfer(value, allowForeignWorkspace);
        return value;
    }

    private static void ValidateTransfer(WorkbenchPreferences value, bool allowForeignWorkspace)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (allowForeignWorkspace && value.Projects is { } projects && IsForeignAbsolutePath(projects.WorkspaceRoot))
        {
            var validation = value with
            {
                Projects = projects with { WorkspaceRoot = new ProjectPreferences().WorkspaceRoot }
            };
            validation.Validate();
        }
        else
        {
            value.Validate();
        }
    }

    private static bool IsForeignAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || Path.IsPathFullyQualified(path))
        {
            return false;
        }

        if (path.StartsWith('/'))
        {
            return true;
        }

        if (path.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
        {
            return false;
        }

        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
        {
            return path.AsSpan(2).IndexOf(':') < 0;
        }

        return path.StartsWith("\\\\", StringComparison.Ordinal) && path.IndexOf(':') < 0 &&
            path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }

    /// <summary>串行写入同目录临时文件后替换，取消不提交未完成的设置。</summary>
    public async Task SaveAsync(WorkbenchPreferences value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, Serialize(value), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            gate.Release();
        }
    }
}
