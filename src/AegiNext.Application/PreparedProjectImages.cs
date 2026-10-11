using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>图片资源的准备结果；先发布文件，再提交工程，成功后接受资源。</summary>
public sealed class PreparedProjectImages : IAsyncDisposable
{
    private readonly PreparedProjectResourceFiles files;

    internal PreparedProjectImages(ImmutableArray<ProjectAsset> assets, PreparedProjectResourceFiles files)
    {
        Assets = assets;
        this.files = files;
    }

    /// <summary>尚未登记到工程中的完整图片资源记录。</summary>
    public ImmutableArray<ProjectAsset> Assets { get; }

    /// <summary>发布本次托管文件；尚未接受时释放租约会撤回新文件。</summary>
    public Task CommitAsync(CancellationToken cancellationToken = default) => files.CommitAsync(cancellationToken);

    /// <summary>在工程编辑提交成功后保留文件供当前工程及撤销历史使用。</summary>
    public void Accept() => files.Accept();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => files.DisposeAsync();
}
