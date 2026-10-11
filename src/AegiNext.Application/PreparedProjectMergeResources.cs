namespace AegiNext.Application;

/// <summary>管理合并资源的暂存、发布与接受；编辑撤销后保留已接受文件供重做。</summary>
public sealed class PreparedProjectMergeResources : IAsyncDisposable
{
    private readonly PreparedProjectResourceFiles files;

    internal PreparedProjectMergeResources(IReadOnlyList<ProjectMergeSource> sources,
        IReadOnlyList<ProjectMergeResourceFile> resources, string destinationDirectory, string stagingDirectory)
    {
        Sources = sources;
        files = new(resources, destinationDirectory, stagingDirectory);
    }

    /// <summary>资源路径已准备为目标工程位置的来源快照。</summary>
    public IReadOnlyList<ProjectMergeSource> Sources { get; }

    /// <summary>原子发布资源，失败时撤回本次新建文件。</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default) => files.CommitAsync(cancellationToken);

    /// <summary>在工程编辑成功后接受文件。</summary>
    public void Accept() => files.Accept();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => files.DisposeAsync();
}
