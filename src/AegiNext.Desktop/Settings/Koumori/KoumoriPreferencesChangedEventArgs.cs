namespace AegiNext.Desktop.Settings.Koumori;

/// <summary>携带已经确认的自动打轴偏好。</summary>
public sealed class KoumoriPreferencesChangedEventArgs(KoumoriPreferences preferences) : EventArgs
{
    public KoumoriPreferences Preferences { get; } = preferences;
}
