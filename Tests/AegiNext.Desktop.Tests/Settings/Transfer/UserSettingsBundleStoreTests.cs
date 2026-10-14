using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

/// <summary>验证版本化用户设置包的完整往返和严格、有界 ZIP 读取。</summary>
[Collection("Workspace session")]
public sealed class UserSettingsBundleStoreTests
{
    private static readonly string[] archiveFileNames =
    [
        "export-presets.aegiexports", "layouts.json", "manifest.json", "preferences.json", "subtitle-color-tags.json"
    ];

    /// <summary>四种设置通过 v3 条目完整往返，所有个人参数保持原值。</summary>
    [Fact]
    public async Task CompleteBundleRoundTripsThroughMemoryAndAtomicFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var bundle = UserSettingsTransferTestData.CreateBundle();
        var bytes = UserSettingsBundleStore.Serialize(bundle);
        var entries = UserSettingsTransferTestData.ReadArchive(bytes);

        Assert.Equal(5, entries.Count);
        Assert.Equal(3, JsonNode.Parse(entries["manifest.json"])!["version"]!.GetValue<int>());
        Assert.Equal(archiveFileNames,
            entries.Keys.Order(StringComparer.Ordinal).ToArray());
        UserSettingsTransferTestData.AssertBundleEqual(bundle, UserSettingsBundleStore.Deserialize(bytes));
        var path = Path.Combine(directory.Path, "settings.aegisettings");
        await UserSettingsBundleStore.SaveAsync(bundle, path);
        UserSettingsTransferTestData.AssertBundleEqual(bundle, await UserSettingsBundleStore.LoadAsync(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    /// <summary>旧设置包保留真实来源版本，手动读取不返回效果素材。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacySettingsPreserveSourceVersionAndOnlyReturnSettings(int version)
    {
        var imported = UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.CreateLegacyArchive(version));

        Assert.Equal(version, imported.SourceVersion);
        Assert.Equal(version == 1, imported.ColorTags is null);
        Assert.Equal("zh-CN", imported.Preferences.Language);
        Assert.DoesNotContain("subtitle-styles.aegistyles", UserSettingsBundleStore.SerializeFiles(imported).Keys);
        Assert.DoesNotContain("effect-scripts.json", UserSettingsBundleStore.SerializeFiles(imported).Keys);
    }

    [Fact]
    public void DefaultBundleContainsEmptyPersonalLibraries()
    {
        var bundle = UserSettingsBundleStore.Deserialize(UserSettingsBundleStore.Serialize(new()));

        Assert.Equal(3, bundle.SourceVersion);
        Assert.Empty(bundle.ExportPresets.Presets);
        Assert.NotNull(bundle.ColorTags);
        Assert.Empty(bundle.ColorTags.Tags);
        Assert.Empty(bundle.Layouts.Presets);
    }

    /// <summary>未知、重复、缺失或非整数版本清单均按无效包拒绝。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"version\":4}")]
    [InlineData("{\"version\":\"1\"}")]
    [InlineData("{\"version\":true}")]
    [InlineData("{\"version\":1,\"version\":1}")]
    [InlineData("{\"version\":1,\"other\":0}")]
    public void InvalidManifestIsRejected(string manifest)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["manifest.json"] = Encoding.UTF8.GetBytes(manifest);

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>非法 UTF-8 清单不会泄漏解码异常类型。</summary>
    [Fact]
    public void InvalidUtf8ManifestReturnsControlledFormatError()
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["manifest.json"] = [0xFF];

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>条目名称必须完全匹配，任何路径或未知大小写条目都无法作为配置读取。</summary>
    [Theory]
    [InlineData("../preferences.json")]
    [InlineData("preferences.json/")]
    [InlineData("folder/preferences.json")]
    [InlineData("PREFERENCES.JSON")]
    [InlineData("C:\\preferences.json")]
    public void UnknownAndPathEntriesAreRejected(string name)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        var preferences = entries["preferences.json"];
        entries.Remove("preferences.json");
        entries[name] = preferences;

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>重复或缺失配置条目不能构成完整设置包。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DuplicateAndMissingEntriesAreRejected(bool duplicate)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        var altered = duplicate ? entries.Concat([new("preferences.json", entries["preferences.json"])]) :
            entries.Where(value => value.Key != "preferences.json");

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(altered)));
    }

    /// <summary>任一组成文档不完整都拒绝整包，不返回部分设置。</summary>
    [Theory]
    [InlineData("preferences.json")]
    [InlineData("export-presets.aegiexports")]
    [InlineData("layouts.json")]
    [InlineData("subtitle-color-tags.json")]
    public void IncompleteComponentRejectsEntireBundle(string name)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries[name] = "{}"u8.ToArray();

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>旧版包重新序列化为 v3，标签缺失状态继续表示保留本地库。</summary>
    [Fact]
    public void VersionOneBundleRetainsMissingTagLibraryAcrossReserialization()
    {
        var legacy = UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.CreateLegacyArchive(1));
        var restaged = UserSettingsBundleStore.Serialize(legacy);
        var restagedEntries = UserSettingsTransferTestData.ReadArchive(restaged);

        Assert.Null(legacy.ColorTags);
        Assert.Equal(1, legacy.SourceVersion);
        Assert.Null(UserSettingsBundleStore.Deserialize(restaged).ColorTags);
        Assert.False(restagedEntries.ContainsKey("subtitle-color-tags.json"));
        Assert.Equal(3, JsonNode.Parse(restagedEntries["manifest.json"])!["version"]!.GetValue<int>());
        Assert.Equal(3, UserSettingsBundleStore.Deserialize(restaged).SourceVersion);
        Assert.False(UserSettingsBundleStore.SerializeFiles(legacy).ContainsKey("subtitle-color-tags.json"));
    }

    /// <summary>各版本只接受该版本声明的完整文件集合。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacyManifestVersionRequiresItsExactFileSet(int version)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsTransferTestData.CreateLegacyArchive(version));
        entries.Remove("effect-scripts.json");

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(entries)));
    }

    /// <summary>高压缩条目仍遵守解压后容量限制，即使目录谎报较小的长度。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedEntryBudgetIsEnforcedBeyondDeclaredLength(bool forgedLength)
    {
        var entries = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        entries["preferences.json"] = new byte[65537];
        var bytes = UserSettingsTransferTestData.WriteArchive(entries);
        if (forgedLength)
        {
            RewriteDeclaredLength(bytes, "preferences.json", 1);
        }

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(bytes));
    }

    /// <summary>超大包在流读取前拒绝；已取消保存保持已有文件字节。</summary>
    [Fact]
    public async Task FileBudgetAndCancellationPreserveExistingDestination()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var large = Path.Combine(directory.Path, "large.aegisettings");
        using (var stream = File.Create(large))
        {
            stream.SetLength((long)UserSettingsBundleStore.MAXIMUM_FILE_BYTES + 1);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => UserSettingsBundleStore.LoadAsync(large));
        var path = Path.Combine(directory.Path, "unchanged.aegisettings");
        var original = "existing bytes"u8.ToArray();
        await File.WriteAllBytesAsync(path, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UserSettingsBundleStore.SaveAsync(new(), path, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static void RewriteDeclaredLength(byte[] bytes, string entryName, uint length)
    {
        for (var index = 0; index + 46 < bytes.Length; index++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) != 0x02014B50)
            {
                continue;
            }
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index + 28, 2));
            if (index + 46 + nameLength <= bytes.Length &&
                Encoding.UTF8.GetString(bytes, index + 46, nameLength) == entryName)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index + 24, 4), length);
                return;
            }
        }
        throw new InvalidOperationException("Test archive entry was not found.");
    }
}
