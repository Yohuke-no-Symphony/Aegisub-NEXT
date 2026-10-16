namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private void SeekSelectedClipOnSelection()
    {
        if (!Preferences.TimelineAutoSeekEnabled || closing || IsProjectBusy || !ViewModel.Preview.CanSeek ||
            SelectedLayer is not { } layer)
        {
            return;
        }

        _ = SeekProjectTimeAsync(layer.Start);
    }
}
