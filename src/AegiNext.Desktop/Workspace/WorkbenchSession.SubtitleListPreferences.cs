using System.ComponentModel;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private void SubscribeSubtitleListPreferences()
    {
        ViewModel.Subtitles.PropertyChanged += OnSubtitleListPreferencePropertyChanged;
    }

    private void UnsubscribeSubtitleListPreferences()
    {
        ViewModel.Subtitles.PropertyChanged -= OnSubtitleListPreferencePropertyChanged;
    }

    private void ApplySubtitleListPreferences()
    {
        ViewModel.Subtitles.HideTimes = preferences.SubtitleListHideTimes;
        ViewModel.Subtitles.HideDuration = preferences.SubtitleListHideDuration;
    }

    private void OnSubtitleListPreferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsUpdating || closing)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ViewModel.Subtitles.HideTimes):
                UpdatePreferences(current => current with { SubtitleListHideTimes = ViewModel.Subtitles.HideTimes });
                break;
            case nameof(ViewModel.Subtitles.HideDuration):
                UpdatePreferences(current => current with { SubtitleListHideDuration = ViewModel.Subtitles.HideDuration });
                break;
        }
    }
}
