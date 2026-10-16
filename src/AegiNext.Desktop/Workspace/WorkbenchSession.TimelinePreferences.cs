using System.ComponentModel;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private void SubscribeTimelinePreferences()
    {
        ViewModel.Timeline.PropertyChanged += OnTimelinePreferencePropertyChanged;
    }

    private void UnsubscribeTimelinePreferences()
    {
        ViewModel.Timeline.PropertyChanged -= OnTimelinePreferencePropertyChanged;
    }

    private void ApplyTimelinePreferences()
    {
        ViewModel.Timeline.IsSnapEnabled = preferences.TimelineSnapEnabled;
        ViewModel.Timeline.IsStepEnabled = preferences.TimelineStepEnabled;
        ViewModel.Timeline.IsAutoSeekEnabled = preferences.TimelineAutoSeekEnabled;
        ViewModel.Timeline.IsSpectrumVisible = preferences.TimelineSpectrumVisible;
        ViewModel.Timeline.IsWaveformVisible = preferences.TimelineWaveformVisible;
        ViewModel.Timeline.IsClassicTimingEnabled = preferences.TimelineClassicTimingEnabled;
    }

    private void OnTimelinePreferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsUpdating || closing)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ViewModel.Timeline.IsClassicTimingEnabled):
                InvalidateTimingSession();
                UpdatePreferences(current => current with { TimelineClassicTimingEnabled = ViewModel.Timeline.IsClassicTimingEnabled });
                break;
            case nameof(ViewModel.Timeline.IsSnapEnabled):
                UpdatePreferences(current => current with { TimelineSnapEnabled = ViewModel.Timeline.IsSnapEnabled });
                break;
            case nameof(ViewModel.Timeline.IsStepEnabled):
                UpdatePreferences(current => current with { TimelineStepEnabled = ViewModel.Timeline.IsStepEnabled });
                break;
            case nameof(ViewModel.Timeline.IsAutoSeekEnabled):
                UpdatePreferences(current => current with { TimelineAutoSeekEnabled = ViewModel.Timeline.IsAutoSeekEnabled });
                break;
            case nameof(ViewModel.Timeline.IsSpectrumVisible):
                UpdatePreferences(current => current with { TimelineSpectrumVisible = ViewModel.Timeline.IsSpectrumVisible });
                break;
            case nameof(ViewModel.Timeline.IsWaveformVisible):
                UpdatePreferences(current => current with { TimelineWaveformVisible = ViewModel.Timeline.IsWaveformVisible });
                break;
        }
    }
}
