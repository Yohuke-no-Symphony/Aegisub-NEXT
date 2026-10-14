using System.Collections.Immutable;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsRestoreBackup
{
    public int Version { get; init; } = UserSettingsBundleStore.CURRENT_VERSION;
    public Guid Id { get; init; }
    public ImmutableArray<UserSettingsRestoreEntry> Files { get; init; } = [];
}
