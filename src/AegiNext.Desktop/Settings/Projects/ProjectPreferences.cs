namespace AegiNext.Desktop.Settings.Projects;

/// <summary>项目的默认位置、素材导入及独立的自动保存、历史备份策略。</summary>
public sealed record ProjectPreferences
{
    public string WorkspaceRoot { get; init; } = ResolveDefaultWorkspaceRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Path.GetTempPath());
    public bool AutoSaveEnabled { get; init; } = true;
    public int AutoSaveIntervalMinutes { get; init; } = 2;
    public bool BackupEnabled { get; init; } = true;
    public int BackupIntervalMinutes { get; init; } = 5;
    public int MaximumBackupCount { get; init; } = 20;
    public bool CopyImportedAssetsToSources { get; init; } = true;

    internal static string ResolveDefaultWorkspaceRoot(string documents, string profile, string temporary)
    {
        var root = Path.IsPathFullyQualified(documents) ? documents :
            Path.IsPathFullyQualified(profile) ? profile : temporary;
        return Path.Combine(root, "AegiNext", "Workspace");
    }

    /// <summary>验证路径语法及保护策略，不创建或探测目录可写性。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkspaceRoot) || !Path.IsPathFullyQualified(WorkspaceRoot) ||
            WorkspaceRoot.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ||
            AutoSaveIntervalMinutes is < 1 or > 1440 || BackupIntervalMinutes is < 1 or > 1440 ||
            MaximumBackupCount is < 1 or > 1000)
        {
            throw new InvalidDataException("项目设置包含无效的 workspace 路径、时间间隔或备份数量。");
        }

        try
        {
            _ = Path.GetFullPath(WorkspaceRoot);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidDataException("项目 workspace 路径无效。", error);
        }
    }
}
