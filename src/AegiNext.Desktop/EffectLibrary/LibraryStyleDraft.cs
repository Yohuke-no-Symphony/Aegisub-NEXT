using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.EffectLibrary;

internal sealed class LibraryStyleDraft(SubtitleStylePreset preset)
{
    internal SubtitleStylePreset Preset { get; private set; } = preset;

    internal void Rename(string name)
    {
        Preset = Preset with { Name = name.Trim().Normalize() };
    }

    internal void UpdateTimingPostProcessor(TimingPostProcessorOptions? options)
    {
        Preset = Preset with { TimingPostProcessor = options };
    }

    internal void UpdateStyle(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        Preset = Preset with
        {
            Style = style with { FontAssetId = null }
        };
    }

    internal LibraryStyleDraft Duplicate(string name)
    {
        return new(Preset with { Id = Guid.NewGuid(), Name = name.Trim().Normalize() });
    }

    internal static string UniqueName(string desired, IEnumerable<SubtitleStylePreset> presets)
    {
        var names = presets.Select(value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        desired = desired.Trim().Normalize();
        var name = LimitName(desired, 128);
        for (var suffix = 2; names.Contains(name); suffix++)
        {
            var ending = $" {suffix}";
            name = LimitName(desired, 128 - ending.Length) + ending;
        }

        return name;
    }

    private static string LimitName(string value, int length)
    {
        if (value.Length <= length)
        {
            return value;
        }

        if (char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length].TrimEnd();
    }
}
