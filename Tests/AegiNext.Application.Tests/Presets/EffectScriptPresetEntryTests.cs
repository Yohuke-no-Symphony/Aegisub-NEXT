using AegiNext.Application.Presets;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectScriptPresetEntryTests
{
    [Fact]
    public async Task CompleteEffectFilePreservesIdentityDisplayNameAndOriginalSource()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "personal.aegieffect");
        var preset = Effect("personal-fade", "自定义展示名称");

        await EffectScriptPresetStore.WritePresetAsync(preset, path);
        var read = await EffectScriptPresetStore.ReadPresetAsync(path);

        Assert.Equal(preset, read);
        Assert.Equal(preset, EffectScriptPresetStore.DeserializePreset(EffectScriptPresetStore.SerializePreset(preset)));
    }

    [Fact]
    public void CompleteEffectFileRequiresExactlyOneValidatedPreset()
    {
        var empty = EffectScriptPresetStore.Serialize(new());
        var multiple = EffectScriptPresetStore.Serialize(new() { Presets = [Effect("personal-one", "One"), Effect("personal-two", "Two")] });

        Assert.Throws<InvalidDataException>(() => EffectScriptPresetStore.DeserializePreset(empty));
        Assert.Throws<InvalidDataException>(() => EffectScriptPresetStore.DeserializePreset(multiple));
        Assert.Throws<InvalidDataException>(() => EffectScriptPresetStore.DeserializePreset([0xFF]));
    }

    [Fact]
    public async Task InvalidCompleteEffectNeverOverwritesAnExistingFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "retained.aegieffect");
        var preset = Effect("personal-retained", "Retained");
        await EffectScriptPresetStore.WritePresetAsync(preset, path);
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<EffectScriptException>(() => EffectScriptPresetStore.WritePresetAsync(preset with { Source = "invalid" }, path));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private static EffectScriptPreset Effect(string scriptId, string name)
    {
        return new(Guid.NewGuid(), name, BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{scriptId}\"", StringComparison.Ordinal));
    }
}
