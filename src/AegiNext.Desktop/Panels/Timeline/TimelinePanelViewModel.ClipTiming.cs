namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelViewModel
{
    private string layerStart = string.Empty;
    private string layerEnd = string.Empty;
    private string? clipTimingValidationError;
    private string? invalidClipTimingField;

    public string LayerStart
    {
        get => layerStart;
        set => SetProperty(ref layerStart, value);
    }

    public string LayerEnd
    {
        get => layerEnd;
        set => SetProperty(ref layerEnd, value);
    }

    public bool HasSelectedClip => SelectedLayer is not null;
    public bool CanEditClipTiming => HasSelectedClip && !session.IsClosing && !session.IsProjectBusy;

    public string? ClipTimingValidationError
    {
        get => clipTimingValidationError;
        internal set => SetProperty(ref clipTimingValidationError, value);
    }

    public string? InvalidClipTimingField
    {
        get => invalidClipTimingField;
        internal set => SetProperty(ref invalidClipTimingField, value);
    }

    internal void RefreshClipTimingAvailability()
    {
        OnPropertyChanged(nameof(HasSelectedClip));
        OnPropertyChanged(nameof(CanEditClipTiming));
    }

    internal void CommitClipTimingDrafts(bool focusInvalid = true) => session.TryCommitDrafts(focusInvalid);

    internal void RestoreClipTimingField(string fieldKey) => session.RestoreClipTimingField(fieldKey);
}
