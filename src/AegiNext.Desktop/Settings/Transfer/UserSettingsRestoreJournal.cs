namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsRestoreJournal
{
    public int Version { get; init; } = UserSettingsBundleStore.CURRENT_VERSION;
    public Guid BackupId { get; init; }
    public string PendingSha256 { get; init; } = string.Empty;
    public string Phase { get; init; } = "applying";
}
