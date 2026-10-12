using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Tests;

public sealed class MaskPreferencesTests
{
    [Fact]
    public async Task MissingMaskOptionEnablesSnappingWithoutRewritingExistingSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Version\":1,\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.True(preferences.MaskSnapEnabled);
        Assert.Equal("zh-CN", preferences.Language);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaskSnappingRoundTripsWithUnrelatedSettings(bool enabled)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            MaskSnapEnabled = enabled,
            TimelineSnapEnabled = false,
            Language = "zh-CN",
            Volume = 0.375f,
            PreviewQuality = PreviewQuality.HIGH
        };

        await store.SaveAsync(expected, CancellationToken.None);
        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        var restored = reopened.Load();

        Assert.Null(reopened.LoadError);
        Assert.Equal(expected, restored);
        Assert.Equal(enabled, restored.MaskSnapEnabled);
        Assert.False(restored.TimelineSnapEnabled);
        Assert.Equal(1, restored.Version);
        Assert.Equal(expected.Language, restored.Language);
        Assert.Equal(expected.Volume, restored.Volume);
        Assert.Equal(expected.PreviewQuality, restored.PreviewQuality);
    }

    [Fact]
    public void MaskSnappingParticipatesInPreferenceEqualityAndEqualCopiesKeepTheirHash()
    {
        var original = new WorkbenchPreferences();
        var changed = original with { MaskSnapEnabled = false };

        Assert.NotEqual(original, changed);
        Assert.False(original == changed);
        var copy = changed with { };
        Assert.Equal(changed, copy);
        Assert.Equal(changed.GetHashCode(), copy.GetHashCode());
    }
}
