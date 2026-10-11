namespace AegiNext.Core.Projects;

/// <summary>图片文件由工程托管，或保持对原始文件的链接；逻辑素材目录与存储位置独立。</summary>
public enum ProjectAssetStorage
{
    MANAGED,
    LINKED
}
