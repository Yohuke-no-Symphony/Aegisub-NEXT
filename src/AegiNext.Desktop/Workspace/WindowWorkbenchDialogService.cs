using AegiNext.Application;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed class WindowWorkbenchDialogService : IWorkbenchDialogService
{
    private readonly Func<Window> ownerProvider;
    private readonly Func<IStorageProvider>? storageProvider;
    private readonly Action<Window>? registerWindow;

    /// <summary>重命名项目素材项；取消或关闭返回空值。</summary>
    public async Task<string?> RenameAssetItemAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new AssetItemRenameDialog(name);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<string?>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
        return await answer;
    }

    /// <summary>重命名项目素材分类；取消或关闭返回空值。</summary>
    public async Task<string?> RenameAssetFolderAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new AssetFolderRenameDialog(name);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<string?>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
        return await answer;
    }

    /// <summary>确认删除素材分类，保留子文件夹和源文件。</summary>
    public async Task<bool> ConfirmAssetFolderDeletionAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new AssetFolderDeletionDialog(name);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    internal WindowWorkbenchDialogService(Window owner, Func<IStorageProvider>? storageProvider = null,
        Action<Window>? registerWindow = null) : this(() => owner, storageProvider, registerWindow)
    {
    }

    internal WindowWorkbenchDialogService(Func<Window> ownerProvider, Func<IStorageProvider>? storageProvider = null,
        Action<Window>? registerWindow = null)
    {
        this.ownerProvider = ownerProvider;
        this.storageProvider = storageProvider;
        this.registerWindow = registerWindow;
    }
    /// <summary>在所属窗口选择本地文件；取消时返回空路径。</summary>
    public async Task<string?> OpenFileAsync(string title, string typeName, string[] patterns)
    {
        var files = await (storageProvider?.Invoke() ?? ownerProvider().StorageProvider).OpenFilePickerAsync(new()
        {
            Title = Localization.Get("Workbench." + (title)), AllowMultiple = false,
            FileTypeFilter = [new(Localization.Get("Workbench." + (typeName))) { Patterns = patterns }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath() ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
    }

    /// <summary>选择多个本地文件，取消时返回空集合。</summary>
    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns)
    {
        var files = await (storageProvider?.Invoke() ?? ownerProvider().StorageProvider).OpenFilePickerAsync(new()
        {
            Title = Localization.Get("Workbench." + title), AllowMultiple = true,
            FileTypeFilter = [new(Localization.Get("Workbench." + typeName)) { Patterns = patterns }]
        });
        return files.Select(file => file.TryGetLocalPath() ??
            throw new NotSupportedException(Localization.Get("Preview.LocalFile"))).ToArray();
    }

    /// <summary>选择批量导出的本地目标目录。</summary>
    public async Task<string?> OpenFolderAsync(string title)
    {
        var folders = await (storageProvider?.Invoke() ?? ownerProvider().StorageProvider).OpenFolderPickerAsync(new()
        {
            Title = Localization.Get("Workbench." + title), AllowMultiple = false
        });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath() ??
            throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
    }

    /// <summary>输入并确认指定范围的整数；取消请求会关闭窗口。</summary>
    public async Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var model = new IntegerInputDialogViewModel(request);
        var dialog = new IntegerInputDialog(model);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<int?>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(null)));
        return await answer;
    }

    /// <summary>询问个人样式或脚本草稿：保存、恢复或取消切换。</summary>
    public async Task<int> ConfirmPresetChangesAsync(bool effects)
    {
        var dialog = new UnsavedProjectDialog(effects ? "Settings.UnsavedEffectText" : "Settings.UnsavedStyleText");
        registerWindow?.Invoke(dialog);
        var result = await dialog.ShowDialog<int>(ownerProvider());
        return result switch { 1 => 0, 2 => 1, _ => 2 };
    }

    /// <summary>询问压制预设草稿：保存、恢复或取消切换。</summary>
    public async Task<int> ConfirmExportPresetChangesAsync()
    {
        var dialog = new UnsavedProjectDialog("Settings.UnsavedExportPresetText");
        registerWindow?.Invoke(dialog);
        var result = await dialog.ShowDialog<int>(ownerProvider());
        return result switch { 1 => 0, 2 => 1, _ => 2 };
    }

    /// <summary>询问个人颜色标签草稿：保存、恢复或取消切换。</summary>
    public async Task<int> ConfirmColorTagChangesAsync()
    {
        var dialog = new UnsavedProjectDialog("Settings.UnsavedColorTagsText");
        registerWindow?.Invoke(dialog);
        var result = await dialog.ShowDialog<int>(ownerProvider());
        return result switch { 1 => 0, 2 => 1, _ => 2 };
    }

    /// <summary>提示下次启动恢复个人设置；取消请求会关闭确认窗口。</summary>
    public async Task<bool> ConfirmSettingsRestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new SettingsRestartDialog();
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    /// <summary>选择带指定扩展名的本地保存路径，并由系统确认覆盖。</summary>
    public async Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension, string suggestedName,
        string? suggestedDirectory = null)
    {
        var provider = storageProvider?.Invoke() ?? ownerProvider().StorageProvider;
        var startLocation = suggestedDirectory is null ? null : await provider.TryGetFolderFromPathAsync(suggestedDirectory);
        var file = await provider.SaveFilePickerAsync(new()
        {
            Title = Localization.Get("Workbench." + title),
            SuggestedStartLocation = startLocation,
            SuggestedFileName = suggestedName.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? suggestedName[..^extension.Length] : suggestedName,
            DefaultExtension = extension.TrimStart('.'),
            FileTypeChoices = [new(Localization.Get("Workbench." + typeName)) { Patterns = patterns }], ShowOverwritePrompt = true
        });
        return file is null ? null : file.TryGetLocalPath() ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
    }

    /// <summary>显示可复制的错误原因；所属工程关闭时取消等待并关闭提示。</summary>
    public async Task ShowErrorAsync(string titleKey, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new ErrorDialog(titleKey, message);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog(ownerProvider());
        await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        await answer;
    }

    /// <summary>填写项目创建信息；错误留在同一面板中，成功后才关闭。</summary>
    public async Task<bool> ShowNewProjectAsync(string workspaceRoot,
        Func<ProjectCreationRequest, CancellationToken, Task<ProjectOpenResult>> create,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NewProjectDialog? dialog = null;
        using var model = new NewProjectDialogViewModel(workspaceRoot, create, async token =>
        {
            token.ThrowIfCancellationRequested();
            var folders = await (storageProvider?.Invoke() ?? dialog!.StorageProvider).OpenFolderPickerAsync(new()
            {
                Title = Localization.Get("Workbench.BrowseProjectLocation"), AllowMultiple = false
            });
            token.ThrowIfCancellationRequested();
            return folders.Count == 0 ? null : folders[0].TryGetLocalPath()
                ?? throw new NotSupportedException(Localization.Get("Preview.LocalFile"));
        }, cancellationToken);
        dialog = new(model);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        var accepted = false;
        try
        {
            accepted = await answer;
        }
        finally
        {
            model.Dispose();
            await model.Completion;
        }
        return accepted || model.WasCreated;
    }

    /// <summary>展示具体字幕转换损失；取消请求会关闭确认窗口。</summary>
    public async Task<bool> ConfirmSubtitleConversionAsync(SubtitleConversionReview review, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new SubtitleConversionDialog(review);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    /// <summary>等待用户决定如何处理项目的未保存修改。</summary>
    public Task<int> ConfirmUnsavedAsync()
    {
        var dialog = new UnsavedProjectDialog();
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<int>(ownerProvider());
    }

    /// <summary>确认是否在媒体不可用时继续打开项目；取消请求会关闭确认窗口。</summary>
    public async Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new UnavailableProjectMediaDialog(mediaPath, reason);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    /// <summary>决定是否将更换的轨道预设同步到现有片段；默认仅更新后续创建样式。</summary>
    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        var dialog = new TrackStyleChangeDialog(trackName, presetName, subtitleCount);
        registerWindow?.Invoke(dialog);
        return dialog.ShowDialog<TrackStyleUpdateDecision>(ownerProvider());
    }

    /// <summary>确认删除冻结的预设选择或未保存草稿；关闭时默认取消。</summary>
    public async Task<bool> ConfirmPresetDeletionAsync(PresetDeletionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new PresetDeletionDialog(request);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }

    /// <summary>确认删除字幕轨道及全部内容；取消请求会关闭窗口，默认选择取消。</summary>
    public async Task<bool> ConfirmTrackDeletionAsync(string trackName, int subtitleCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new TrackDeletionDialog(trackName, subtitleCount);
        registerWindow?.Invoke(dialog);
        var answer = dialog.ShowDialog<bool>(ownerProvider());
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await answer;
    }
}
