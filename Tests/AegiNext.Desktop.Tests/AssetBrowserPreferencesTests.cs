using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Assets;

namespace AegiNext.Desktop.Tests;

public sealed class AssetBrowserPreferencesTests
{
    [Fact]
    public void PresentationPreferencesRoundTripAndParticipateInEquality()
    {
        var original = new WorkbenchPreferences();
        var edited = original with
        {
            AssetBrowser = new() { ViewMode = AssetBrowserViewMode.ICONS, ThumbnailSize = 144, FolderPaneWidth = 225 }
        };
        var restored = WorkbenchPreferencesStore.Deserialize(WorkbenchPreferencesStore.Serialize(edited));
        Assert.Equal(edited, restored);
        Assert.Equal(edited.AssetBrowser, restored.AssetBrowser);
        Assert.NotEqual(original, edited);
        var directory = Path.Combine(Path.GetTempPath(), "aeginext-asset-preferences-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "preferences.json"), "{}");
            using var store = new WorkbenchPreferencesStore(directory);
            Assert.Equal(original.AssetBrowser, store.Load().AssetBrowser);
            Assert.Null(store.LoadError);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(double.NaN, 140)]
    [InlineData(47, 140)]
    [InlineData(193, 140)]
    [InlineData(64, double.PositiveInfinity)]
    [InlineData(64, 79)]
    [InlineData(64, 641)]
    public void InvalidSizesCannotBePersisted(double thumbnailSize, double folderWidth)
    {
        var preferences = new WorkbenchPreferences
        {
            AssetBrowser = new() { ThumbnailSize = thumbnailSize, FolderPaneWidth = folderWidth }
        };
        Assert.Throws<InvalidDataException>(preferences.Validate);
    }

    [Fact]
    public void UnknownViewModeCannotBePersisted()
    {
        var preferences = new WorkbenchPreferences { AssetBrowser = new() { ViewMode = (AssetBrowserViewMode)123 } };
        Assert.Throws<InvalidDataException>(preferences.Validate);
    }
}
