using System.Globalization;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Projects;

/// <summary>项目设置草稿；逐字段确认，持久化和定时任务由组合根拥有。</summary>
public sealed class ProjectSettingsViewModel : ObservableObject
{
    private readonly Dictionary<ProjectSettingsField, string> errors = [];
    private ProjectPreferences preferences;
    private string workspaceRootText;
    private string autoSaveIntervalText;
    private string backupIntervalText;
    private string maximumBackupCountText;
    private string? externalError;

    /// <summary>使用已加载的项目设置初始化独立草稿。</summary>
    public ProjectSettingsViewModel(ProjectPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        this.preferences = preferences;
        workspaceRootText = preferences.WorkspaceRoot;
        autoSaveIntervalText = Format(preferences.AutoSaveIntervalMinutes);
        backupIntervalText = Format(preferences.BackupIntervalMinutes);
        maximumBackupCountText = Format(preferences.MaximumBackupCount);
    }

    public event EventHandler<ProjectPreferencesChangedEventArgs>? Changed;
    public ProjectPreferences Preferences => preferences;
    public string? Error => externalError ?? (errors.Values.FirstOrDefault() is { } key ? Localization.Get(key) : null);

    public string WorkspaceRootText
    {
        get => workspaceRootText;
        set => SetProperty(ref workspaceRootText, value);
    }

    public string AutoSaveIntervalText
    {
        get => autoSaveIntervalText;
        set => SetProperty(ref autoSaveIntervalText, value);
    }

    public string BackupIntervalText
    {
        get => backupIntervalText;
        set => SetProperty(ref backupIntervalText, value);
    }

    public string MaximumBackupCountText
    {
        get => maximumBackupCountText;
        set => SetProperty(ref maximumBackupCountText, value);
    }

    public bool AutoSaveEnabled
    {
        get => preferences.AutoSaveEnabled;
        set
        {
            if (preferences.AutoSaveEnabled != value)
            {
                Apply(preferences with { AutoSaveEnabled = value });
            }
        }
    }

    public bool BackupEnabled
    {
        get => preferences.BackupEnabled;
        set
        {
            if (preferences.BackupEnabled != value)
            {
                Apply(preferences with { BackupEnabled = value });
            }
        }
    }

    public bool CopyImportedAssetsToSources
    {
        get => preferences.CopyImportedAssetsToSources;
        set
        {
            if (preferences.CopyImportedAssetsToSources != value)
            {
                Apply(preferences with { CopyImportedAssetsToSources = value });
            }
        }
    }

    /// <summary>确认单字段的有效输入；无效输入保留原文且不通知偏好更新。</summary>
    public bool Commit(ProjectSettingsField field)
    {
        externalError = null;
        var next = preferences;
        var text = GetText(field).Trim();
        var valid = field == ProjectSettingsField.WORKSPACE_ROOT
            ? IsValidWorkspace(text)
            : int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed) &&
              parsed >= 1 && parsed <= (field == ProjectSettingsField.MAXIMUM_BACKUP_COUNT ? 1000 : 1440);
        if (!valid)
        {
            errors[field] = field switch
            {
                ProjectSettingsField.WORKSPACE_ROOT => "Settings.ProjectWorkspaceInvalid",
                ProjectSettingsField.MAXIMUM_BACKUP_COUNT => "Settings.ProjectBackupCountInvalid",
                _ => "Settings.ProjectIntervalInvalid"
            };
            OnPropertyChanged(nameof(Error));
            return false;
        }

        errors.Remove(field);
        if (field == ProjectSettingsField.WORKSPACE_ROOT)
        {
            next = next with { WorkspaceRoot = Path.GetFullPath(text) };
        }
        else
        {
            var value = int.Parse(text, CultureInfo.CurrentCulture);
            next = field switch
            {
                ProjectSettingsField.AUTO_SAVE_INTERVAL => next with { AutoSaveIntervalMinutes = value },
                ProjectSettingsField.BACKUP_INTERVAL => next with { BackupIntervalMinutes = value },
                ProjectSettingsField.MAXIMUM_BACKUP_COUNT => next with { MaximumBackupCount = value },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            };
        }

        Apply(next);
        Restore(field);
        return true;
    }

    /// <summary>恢复单字段到已确认设置，保留其他字段草稿。</summary>
    public void Restore(ProjectSettingsField field)
    {
        SetText(field, GetCommittedText(preferences, field));
        errors.Remove(field);
        externalError = null;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>回填共享设置，保留本页尚未确认的原始输入。</summary>
    public void UpdatePreferences(ProjectPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        foreach (var field in Enum.GetValues<ProjectSettingsField>())
        {
            if (GetText(field) == GetCommittedText(preferences, field))
            {
                SetText(field, GetCommittedText(value, field));
            }
        }

        preferences = value;
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(AutoSaveEnabled));
        OnPropertyChanged(nameof(BackupEnabled));
        OnPropertyChanged(nameof(CopyImportedAssetsToSources));
    }

    /// <summary>显示目录选择等界面操作的诊断。</summary>
    public void ShowError(string? message)
    {
        externalError = message;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>刷新错误展示语言，保留输入和已确认设置。</summary>
    public void RefreshLanguage() => OnPropertyChanged(nameof(Error));

    private void Apply(ProjectPreferences next)
    {
        if (next == preferences)
        {
            return;
        }

        next.Validate();
        preferences = next;
        OnPropertyChanged(nameof(Preferences));
        OnPropertyChanged(nameof(AutoSaveEnabled));
        OnPropertyChanged(nameof(BackupEnabled));
        OnPropertyChanged(nameof(CopyImportedAssetsToSources));
        Changed?.Invoke(this, new(next));
    }

    private string GetText(ProjectSettingsField field) => field switch
    {
        ProjectSettingsField.WORKSPACE_ROOT => WorkspaceRootText,
        ProjectSettingsField.AUTO_SAVE_INTERVAL => AutoSaveIntervalText,
        ProjectSettingsField.BACKUP_INTERVAL => BackupIntervalText,
        ProjectSettingsField.MAXIMUM_BACKUP_COUNT => MaximumBackupCountText,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private void SetText(ProjectSettingsField field, string value)
    {
        switch (field)
        {
            case ProjectSettingsField.WORKSPACE_ROOT:
                WorkspaceRootText = value;
                break;
            case ProjectSettingsField.AUTO_SAVE_INTERVAL:
                AutoSaveIntervalText = value;
                break;
            case ProjectSettingsField.BACKUP_INTERVAL:
                BackupIntervalText = value;
                break;
            case ProjectSettingsField.MAXIMUM_BACKUP_COUNT:
                MaximumBackupCountText = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    private static string GetCommittedText(ProjectPreferences value, ProjectSettingsField field) => field switch
    {
        ProjectSettingsField.WORKSPACE_ROOT => value.WorkspaceRoot,
        ProjectSettingsField.AUTO_SAVE_INTERVAL => Format(value.AutoSaveIntervalMinutes),
        ProjectSettingsField.BACKUP_INTERVAL => Format(value.BackupIntervalMinutes),
        ProjectSettingsField.MAXIMUM_BACKUP_COUNT => Format(value.MaximumBackupCount),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);

    private static bool IsValidWorkspace(string value)
    {
        try
        {
            new ProjectPreferences { WorkspaceRoot = value }.Validate();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
