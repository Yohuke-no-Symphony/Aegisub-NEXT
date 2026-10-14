using System.Collections.Immutable;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsRestorePayload(UserSettingsBundle Settings,
    ImmutableArray<UserSettingsBundleFile> Files, IReadOnlyDictionary<string, byte[]> Content);
