namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private bool suppressClipAutoSeek;

    /// <summary>点击片段后没有拖动时补做自动跳转；拖动开始时不移动播放头。</summary>
    internal void SeekSelectedClipAfterClick() => SeekSelectedClipOnSelection();

    private void SeekSelectedClipOnSelection()
    {
        if (suppressClipAutoSeek || !Preferences.TimelineAutoSeekEnabled || closing || IsProjectBusy || !ViewModel.Preview.CanSeek ||
            SelectedLayer is not { } layer)
        {
            return;
        }

        _ = SeekProjectTimeAsync(layer.Start);
    }
}
