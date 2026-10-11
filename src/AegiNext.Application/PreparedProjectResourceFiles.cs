using System.Security.Cryptography;

namespace AegiNext.Application;

/// <summary>管理整批合并资源的暂存、发布和接受；撤销编辑后仍保留已接受资源供重做读取。</summary>
internal sealed class PreparedProjectResourceFiles : IAsyncDisposable
{
    private readonly IReadOnlyList<ProjectMergeResourceFile> files;
    private readonly string destinationDirectory;
    private readonly string stagingDirectory;
    private readonly Dictionary<string, string> createdFiles = new(StringComparer.Ordinal);
    private readonly List<FileStream> readLeases = [];
    private readonly Lock lifecycle = new();
    private bool destinationDirectoryCreated;
    private readonly List<string> createdDirectories = [];
    private bool operationRunning;
    private bool committed;
    private bool accepted;
    private bool failed;
    private bool disposed;

    internal PreparedProjectResourceFiles(IReadOnlyList<ProjectMergeResourceFile> files, string destinationDirectory, string stagingDirectory)
    {
        this.files = files;
        this.destinationDirectory = destinationDirectory;
        this.stagingDirectory = stagingDirectory;
    }

    /// <summary>校验并原子发布整批文件；失败时撤回本次新建文件，已有文件始终保留。</summary>
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operationRunning || failed)
            {
                throw new InvalidOperationException("合并资源租约正在使用或已经失败。");
            }
            if (committed)
            {
                return;
            }

            operationRunning = true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count > 0)
            {
                ProjectMergeResources.RejectSymbolicLink(destinationDirectory);
                destinationDirectoryCreated = !Directory.Exists(destinationDirectory);
                Directory.CreateDirectory(destinationDirectory);
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AegiNext.Core.Projects.ProjectValidator.ValidateRelativePath(file.RelativePath);
                    var directory = destinationDirectory;
                    foreach (var component in file.RelativePath.Split('/')[..^1])
                    {
                        directory = Path.Combine(directory, component);
                        ProjectMergeResources.RejectSymbolicLink(directory);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                            createdDirectories.Add(directory);
                        }
                        ProjectMergeResources.RejectSymbolicLink(directory);
                    }
                    await PublishAsync(file, directory, cancellationToken).ConfigureAwait(false);
                }

                for (var index = 0; index < files.Count; index++)
                {
                    await VerifyAsync(readLeases[index], files[index].Sha256, cancellationToken).ConfigureAwait(false);
                    ProjectMergeResources.RejectSymbolicLink(readLeases[index].Name);
                    await using var current = new FileStream(readLeases[index].Name, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65536, true);
                    await VerifyAsync(current, files[index].Sha256, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            lock (lifecycle)
            {
                committed = true;
            }
        }
        catch (Exception error)
        {
            lock (lifecycle)
            {
                failed = true;
            }

            try
            {
                await CleanupAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("合并资源发布失败，撤回文件时也发生错误。", error, cleanupError);
            }

            throw;
        }
        finally
        {
            lock (lifecycle)
            {
                operationRunning = false;
            }
        }
    }

    /// <summary>在工程编辑事务成功应用后接受发布文件，使其继续服务于当前快照和撤销历史。</summary>
    public void Accept()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (operationRunning || !committed || failed)
            {
                throw new InvalidOperationException("只能接受已成功发布的合并资源。");
            }

            accepted = true;
        }
    }

    /// <summary>释放读取租约与暂存文件；尚未接受时仅清理本次实际新建的目标文件。</summary>
    public async ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            if (disposed)
            {
                return;
            }
            if (operationRunning)
            {
                throw new InvalidOperationException("合并资源正在发布，不能同时释放租约。");
            }

            operationRunning = true;
        }

        try
        {
            await CleanupAsync().ConfigureAwait(false);
            lock (lifecycle)
            {
                disposed = true;
            }
        }
        finally
        {
            lock (lifecycle)
            {
                operationRunning = false;
            }
        }
    }

    private async Task PublishAsync(ProjectMergeResourceFile file, string directory, CancellationToken cancellationToken)
    {
        ProjectMergeResources.RejectSymbolicLink(directory);
        var destination = Path.Combine(destinationDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        ProjectMergeResources.RejectSymbolicLink(destination);
        if (!File.Exists(destination))
        {
            var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                var source = Path.Combine(stagingDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                                 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    await VerifyAsync(output, file.Sha256, cancellationToken).ConfigureAwait(false);
                    output.Flush(true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ProjectMergeResources.RejectSymbolicLink(directory);
                ProjectMergeResources.RejectSymbolicLink(destination);
                try
                {
                    File.Move(temporary, destination);
                    createdFiles.Add(destination, file.Sha256);
                }
                catch (IOException) when (File.Exists(destination))
                {
                    ProjectMergeResources.RejectSymbolicLink(destination);
                }
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        var lease = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        readLeases.Add(lease);
        await VerifyAsync(lease, file.Sha256, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyAsync(FileStream stream, string expected, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("已存在同名但内容不同的合并资源。");
        }

        stream.Position = 0;
    }

    private async Task CleanupAsync()
    {
        var errors = new List<Exception>();
        foreach (var lease in readLeases)
        {
            try
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        readLeases.Clear();

        if (!accepted)
        {
            foreach (var (path, digest) in createdFiles.ToArray())
            {
                try
                {
                    ProjectMergeResources.RejectSymbolicLink(Path.GetDirectoryName(path)!);
                    ProjectMergeResources.RejectSymbolicLink(path);
                    if (File.Exists(path))
                    {
                        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 65536, true);
                        await VerifyAsync(stream, digest, CancellationToken.None).ConfigureAwait(false);
                    }
                    File.Delete(path);
                    createdFiles.Remove(path);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            foreach (var directory in createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    ProjectMergeResources.RejectSymbolicLink(directory);
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (destinationDirectoryCreated && Directory.Exists(destinationDirectory))
            {
                try
                {
                    ProjectMergeResources.RejectSymbolicLink(destinationDirectory);
                    if (!Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
                    {
                        Directory.Delete(destinationDirectory);
                    }
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
        }

        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
        catch (Exception error)
        {
            errors.Add(error);
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("合并资源清理未能完整完成。", errors);
        }
    }
}
