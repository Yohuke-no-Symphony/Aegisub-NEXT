using System.ComponentModel;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private void SubscribeMaskPreferences()
    {
        ViewModel.Masks.PropertyChanged += OnMaskPreferencePropertyChanged;
    }

    private void UnsubscribeMaskPreferences()
    {
        ViewModel.Masks.PropertyChanged -= OnMaskPreferencePropertyChanged;
    }

    private void ApplyMaskPreferences()
    {
        ViewModel.Masks.IsSnapEnabled = preferences.MaskSnapEnabled;
        ViewModel.Preview.IsMaskSnapEnabled = preferences.MaskSnapEnabled;
    }

    private void OnMaskPreferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsUpdating || closing || e.PropertyName != nameof(ViewModel.Masks.IsSnapEnabled))
        {
            return;
        }

        UpdatePreferences(current => current with { MaskSnapEnabled = ViewModel.Masks.IsSnapEnabled });
    }
}
