namespace AegiNext.Application;

/// <summary>一次图片资源准备请求；指定现有身份用于收集、刷新或重新定位。</summary>
public sealed record ProjectImageImport(string SourcePath, bool CopyToProject, Guid? AssetId = null,
    string? ExpectedSha256 = null, string? FileName = null);
