using AegiNext.Application.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WindowWorkbenchDialogService
{
    /// <summary>使用已保存快照选择导出素材；文件选择属于导出对话框，返回前排空当前导出。</summary>
    public async Task<bool> ShowEffectLibraryExportAsync(EffectLibrarySnapshot snapshot,
        Func<EffectLibraryExportRequest, IWorkbenchDialogService, CancellationToken, Task<bool>> exportAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exportAsync);
        cancellationToken.ThrowIfCancellationRequested();
        IWorkbenchDialogService? scopedDialogs = null;
        await using var model = new EffectLibraryExportDialogViewModel(snapshot,
            (request, token) => exportAsync(request, scopedDialogs!, token), cancellationToken);
        var dialog = new EffectLibraryExportDialog(model);
        scopedDialogs = new WindowWorkbenchDialogService(dialog, storageProvider, registerWindow);
        try
        {
            registerWindow?.Invoke(dialog);
            var answer = dialog.ShowDialog<bool>(ownerProvider());
            using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(dialog.CloseImmediately));
            return await answer;
        }
        finally
        {
            dialog.CloseImmediately();
            try
            {
                await model.Completion;
            }
            finally
            {
                await model.DisposeAsync();
            }
        }
    }
}
