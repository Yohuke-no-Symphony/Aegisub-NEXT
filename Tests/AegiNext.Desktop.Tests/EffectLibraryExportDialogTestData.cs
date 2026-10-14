using AegiNext.Application.Presets;
using AegiNext.Core.Effects;

namespace AegiNext.Desktop.Tests;

internal static class EffectLibraryExportDialogTestData
{
    internal static EffectLibrarySnapshot Snapshot()
    {
        return new()
        {
            Revision = Guid.NewGuid(),
            Styles = new() { Presets = [new(Guid.NewGuid(), "字幕 Bright", new()), new(Guid.NewGuid(), "Compact", new())] },
            Effects = new()
            {
                Presets =
                [
                    new(Guid.NewGuid(), "Motion Display", BuiltinEffectScripts.Get("fade-in").Source.Replace("fade-in", "glow-script", StringComparison.Ordinal)),
                    new(Guid.NewGuid(), "Soft Exit", BuiltinEffectScripts.Get("fade-out").Source.Replace("fade-out", "exit-script", StringComparison.Ordinal))
                ]
            }
        };
    }
}
