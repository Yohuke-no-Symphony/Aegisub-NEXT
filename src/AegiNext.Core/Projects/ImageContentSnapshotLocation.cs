namespace AegiNext.Core.Projects;

/// <summary>按已接受内容指纹定位压缩图片的临时版本缓存；不改变工程的托管或外链身份。</summary>
public static class ImageContentSnapshotLocation
{
    /// <summary>获取可由预览和独立导出进程共同读取的临时缓存目录。</summary>
    public static string DirectoryPath => Path.Combine(Path.GetTempPath(), "AegiNext", "image-content");

    /// <summary>获取指定 SHA-256 版本的不可变缓存路径。</summary>
    public static string Resolve(string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException("图片版本必须是 SHA-256 指纹。", nameof(sha256));
        }
        return Path.Combine(DirectoryPath, sha256.ToLowerInvariant());
    }
}
