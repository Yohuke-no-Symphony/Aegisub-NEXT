using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>另存为的完整资源准备结果；工程文件保存成功前可撤回本次发布的托管资源。</summary>
public sealed class PreparedProjectRelocation : IAsyncDisposable
{
    private readonly PreparedProjectResourceFiles files;

    internal PreparedProjectRelocation(ProjectDocument snapshot, PreparedProjectResourceFiles files)
    {
        Snapshot = snapshot;
        this.files = files;
    }

    /// <summary>已按目标目录重定位的工程快照；托管资源路径保持兼容。</summary>
    public ProjectDocument Snapshot { get; }

    /// <summary>整批发布资源；失败或尚未接受时释放租约会撤回本次新文件。</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default) => files.CommitAsync(cancellationToken);

    /// <summary>工程文件保存成功后保留已发布资源。</summary>
    public void Accept() => files.Accept();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => files.DisposeAsync();
}
