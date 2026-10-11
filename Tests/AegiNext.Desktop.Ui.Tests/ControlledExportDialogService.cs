using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class ControlledExportDialogService : IWorkbenchDialogService
{
    private readonly TaskCompletionSource<string?> output = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int SaveRequests { get; private set; }
    internal int ConfirmationRequests { get; private set; }
    internal string? SuggestedFileName { get; private set; }
    internal string? SuggestedDirectory { get; private set; }
    internal string[] Patterns { get; private set; } = [];
    internal string? DefaultExtension { get; private set; }
    internal List<string> ErrorMessages { get; } = [];
    internal Exception? PickerError { get; set; }
    internal TaskCompletionSource? PendingErrorAcknowledgement { get; set; }
    internal TaskCompletionSource ErrorShown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal void ResolveOutput(string? path) => output.TrySetResult(path);

    /// <summary>本上下文不选择输入资源。</summary>
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, string typeName, string[] patterns) => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<int?> ShowIntegerInputAsync(IntegerInputRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<int?>(null);
    }

    public Task<int> ConfirmPresetChangesAsync(bool effects) => Task.FromResult(2);

    public Task<string?> OpenFileAsync(string title, string typeName, string[] patterns) => Task.FromResult<string?>(null);

    /// <summary>等待测试显式确认或取消输出路径。</summary>
    public Task<string?> SaveFileAsync(string title, string typeName, string[] patterns, string extension,
        string suggestedName, string? suggestedDirectory = null)
    {
        SaveRequests++;
        SuggestedFileName = suggestedName;
        SuggestedDirectory = suggestedDirectory;
        Patterns = patterns;
        DefaultExtension = extension;
        if (PickerError is { } error)
        {
            return Task.FromException<string?>(error);
        }
        return output.Task;
    }

    /// <summary>记录错误提示，并按测试要求等待确认或关闭取消。</summary>
    public Task ShowErrorAsync(string titleKey, string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ErrorMessages.Add(message);
        ErrorShown.TrySetResult();
        return PendingErrorAcknowledgement?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
    }

    /// <summary>关闭上下文时明确丢弃未保存的测试工程。</summary>
    public Task<int> ConfirmUnsavedAsync()
    {
        ConfirmationRequests++;
        return Task.FromResult(2);
    }

    public Task<bool> ConfirmUnavailableMediaAsync(string mediaPath, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<TrackStyleUpdateDecision> ConfirmTrackStyleChangeAsync(string trackName, string presetName, int subtitleCount)
    {
        return Task.FromResult(TrackStyleUpdateDecision.DEFAULT_ONLY);
    }
}
