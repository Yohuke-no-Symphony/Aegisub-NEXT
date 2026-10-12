using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests.Presets;

public sealed class SubtitleStylePresetTests
{
    [Fact]
    public async Task PrepareImportsFontForAnEmptyTrackDefaultAndReusesThePreparedResource()
    {
        using var directory = new TemporaryProjectDirectory();
        var bytes = ImmutableArray.Create<byte>(0, 1, 0, 0, 11, 22, 33, 44);
        var font = new EmbeddedSubtitleFont("字幕字体.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        var preset = SystemPreset("Future subtitles") with { Font = font };
        var project = new ProjectDocument();

        var prepared = await SubtitleStylePresetService.PrepareAsync(preset, project, directory.Path);

        Assert.Empty(prepared.Project.Subtitles);
        var asset = Assert.Single(prepared.Project.Assets);
        Assert.Equal(asset.Id, prepared.Style.FontAssetId);
        Assert.Equal(bytes.ToArray(), await File.ReadAllBytesAsync(ProjectAssetLocation.Resolve(asset, directory.Path)));
        var styled = ProjectEditingOperations.SetSubtitleTrackStyle(prepared.Project, ProjectTrack.DEFAULT_TRACK_ID,
            preset.Id, preset.Name, prepared.Style);
        var editor = new ProjectEditor(styled);
        editor.AddSubtitle(new(0), new(2), "Later");
        Assert.Equal(asset.Id, Assert.Single(editor.Snapshot.Subtitles).Style.FontAssetId);
        var reused = await SubtitleStylePresetService.PrepareAsync(preset, editor.Snapshot, directory.Path);
        Assert.Single(reused.Project.Assets);
        Assert.Equal(asset.Id, reused.Style.FontAssetId);
    }

    [Fact]
    public async Task CaptureExportImportAndApplyMovesFontAcrossProjectsWithoutDanglingIds()
    {
        using var source = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var bytes = new byte[] { 0, 1, 0, 0, 11, 22, 33, 44 };
        var file = Path.Combine(source.Path, "字幕字体.ttf");
        await File.WriteAllBytesAsync(file, bytes);
        var font = await ProjectResources.ImportAsync(file, ProjectAssetKind.FONT, source.Path);
        var editor = new ProjectEditor(new ProjectDocument { Assets = [font] });
        var lineId = editor.AddSubtitle(new(1, 3), new(7, 3), "跨工程字体");
        var style = new SubtitleStyle
        {
            FontAssetId = font.Id, FontFamily = "项目字体", FontSize = 42, Fill = new(2, 0.5, 0.1),
            Position = new() { Anchor = new(0.25, 0.75), Pivot = new(1, 0), Offset = new(35, -17) }
        };
        var preset = await SubtitleStylePresetService.CaptureAsync("中文样式", style, editor.Snapshot, source.Path);
        Assert.Null(preset.Style.FontAssetId);
        Assert.Equal(bytes, preset.Font!.Data.ToArray());
        Assert.Equal(font.Sha256, preset.Font.Sha256);
        var original = new SubtitleStylePresetCollection { Presets = [preset] };
        var json = SubtitleStylePresetStore.Serialize(original);
        Assert.Contains(Convert.ToBase64String(bytes), Encoding.UTF8.GetString(json), StringComparison.Ordinal);
        var portable = SubtitleStylePresetStore.Deserialize(json);
        var target = new ProjectEditor();
        var targetId = target.AddSubtitle(new(5), new(6), "保持文本与时间");
        var applied = await SubtitleStylePresetService.ApplyAsync(Assert.Single(portable.Presets), target.Snapshot, destination.Path, [targetId]);
        var appliedLine = Assert.Single(applied.Subtitles);
        var imported = Assert.Single(applied.Assets);
        Assert.NotEqual(font.Id, imported.Id);
        Assert.Equal(imported.Id, appliedLine.Style.FontAssetId);
        Assert.Equal(style with { FontAssetId = imported.Id }, appliedLine.Style);
        Assert.Equal("保持文本与时间", appliedLine.Text);
        Assert.Equal(target.Snapshot.Subtitles[0].Start, appliedLine.Start);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(ProjectAssetLocation.Resolve(imported, destination.Path)));
        Assert.Empty(target.Snapshot.Assets);
        Assert.Null(target.Snapshot.Subtitles[0].Style.FontAssetId);
        Assert.Equal(lineId, editor.Snapshot.Subtitles[0].Id);
        var reapplied = await SubtitleStylePresetService.ApplyAsync(preset, applied, destination.Path, [targetId]);
        Assert.Single(reapplied.Assets);
        Assert.Equal(imported.Id, reapplied.Subtitles[0].Style.FontAssetId);
        var beforeApply = target.Snapshot;
        target.Apply("Apply portable style and position", _ => applied);
        Assert.Equal(style.Position, target.Snapshot.Subtitles[0].Style.Position);
        Assert.True(target.Undo());
        Assert.Same(beforeApply, target.Snapshot);
        Assert.True(target.Redo());
        Assert.Same(applied, target.Snapshot);
    }

    [Fact]
    public async Task MissingOrTamperedProjectFontCannotBeCapturedAsSystemFont()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "font.ttf");
        await File.WriteAllBytesAsync(path, [1, 2, 3]);
        var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.FONT, directory.Path);
        var project = new ProjectDocument { Assets = [asset] };
        var style = new SubtitleStyle { FontAssetId = asset.Id };
        await File.WriteAllBytesAsync(ProjectAssetLocation.Resolve(asset, directory.Path), [9, 8, 7]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.CaptureAsync("损坏", style, project, directory.Path));
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.CaptureAsync("缺失", style with { FontAssetId = Guid.NewGuid() }, project, directory.Path));
    }

    [Fact]
    public async Task LibraryWritesAreAtomicAndInvalidImportDoesNotChangeSnapshotOrDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        using var library = new SubtitleStylePresetLibrary(path);
        await library.LoadAsync();
        Assert.Empty(library.Snapshot.Presets);
        var preset = SystemPreset("保留");
        await library.UpsertAsync(preset);
        var snapshot = library.Snapshot;
        var before = await File.ReadAllBytesAsync(path);
        var invalidPath = Path.Combine(directory.Path, "invalid.aegistyles");
        await File.WriteAllTextAsync(invalidPath, "{\"version\":99,\"presets\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(invalidPath));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.UpsertAsync(SystemPreset("取消"), cancellation.Token));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task EditRemoveExportSubsetAndConcurrentUpdatesPreserveLibrary()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        using var library = new SubtitleStylePresetLibrary(path);
        var first = SystemPreset("First");
        var second = SystemPreset("Second");
        await Task.WhenAll(library.UpsertAsync(first), library.UpsertAsync(second));
        Assert.Equal(2, library.Snapshot.Presets.Length);
        await library.UpsertAsync(first with { Name = "Renamed", Style = first.Style with { FontSize = 99 } });
        Assert.Equal(99, library.Snapshot.Presets.Single(preset => preset.Id == first.Id).Style.FontSize);
        var export = Path.Combine(directory.Path, "selected.aegistyles");
        await library.ExportAsync(export, [first.Id]);
        Assert.Equal(first.Id, Assert.Single((await SubtitleStylePresetStore.LoadAsync(export)).Presets).Id);
        await library.RemoveAsync(second.Id);
        Assert.Single(library.Snapshot.Presets);
        using var loaded = new SubtitleStylePresetLibrary(path);
        await loaded.LoadAsync();
        Assert.Equal("Renamed", Assert.Single(loaded.Snapshot.Presets).Name);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(export));
        Assert.Single(library.Snapshot.Presets);
    }

    [Theory]
    [InlineData("\"version\": {version}", "\"version\": 99")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"version\": {version},")]
    [InlineData("\"version\": {version},", "\"version\": {version}, \"unknown\": true,")]
    [InlineData("\"fontSize\": 64,", "\"fontSize\": 0,")]
    [InlineData("\"fontSize\": 64,", "")]
    [InlineData("\"font\": null", "\"font\": null, \"unrecognized\": 1")]
    [InlineData("\"fontSize\": 64,", "\"fontSize\": 64, \"fontSize\": 65,")]
    [InlineData("\"alignment\": \"BOTTOM_CENTER\"", "\"alignment\": 7")]
    public void UnsupportedDuplicateUnknownMissingAndInvalidFieldsAreRejected(string old, string replacement)
    {
        var json = Encoding.UTF8.GetString(SubtitleStylePresetStore.Serialize(new() { Presets = [SystemPreset("Valid")] }));
        var currentVersion = SubtitleStylePresetCollection.CURRENT_VERSION.ToString(CultureInfo.InvariantCulture);
        old = old.Replace("{version}", currentVersion, StringComparison.Ordinal);
        replacement = replacement.Replace("{version}", currentVersion, StringComparison.Ordinal);
        Assert.Contains(old, json, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetStore.Deserialize(Encoding.UTF8.GetBytes(json.Replace(old, replacement, StringComparison.Ordinal))));
    }

    [Fact]
    public void InvalidUtf8AndCorruptEmbeddedHashAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetStore.Deserialize([0x7b, 0xff, 0x7d]));
        var bytes = ImmutableArray.Create<byte>(1, 2, 3);
        var font = new EmbeddedSubtitleFont("font.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        var preset = SystemPreset("Font") with { Font = font };
        var json = Encoding.UTF8.GetString(SubtitleStylePresetStore.Serialize(new() { Presets = [preset] }));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetStore.Deserialize(Encoding.UTF8.GetBytes(json.Replace(font.Sha256, new string('0', 64), StringComparison.Ordinal))));
    }

    [Fact]
    public void PortableFontBudgetAcceptsTypicalLargeCjkFontAndRejectsOversizedPayload()
    {
        var bytes = new byte[20 * 1024 * 1024];
        bytes[0] = 1;
        bytes[^1] = 2;
        var data = ImmutableCollectionsMarshal.AsImmutableArray(bytes);
        var font = new EmbeddedSubtitleFont("NotoSansCJK.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), data);
        var preset = SystemPreset("CJK") with { Font = font };
        var json = SubtitleStylePresetStore.Serialize(new() { Presets = [preset] });
        var restored = Assert.Single(SubtitleStylePresetStore.Deserialize(json).Presets).Font!;
        Assert.Equal(data.Length, restored.Data.Length);
        Assert.Equal(font.Sha256, restored.Sha256);
        var oversized = font with { Data = ImmutableCollectionsMarshal.AsImmutableArray(new byte[32 * 1024 * 1024 + 1]) };
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetStore.Serialize(new() { Presets = [preset with { Font = oversized }] }));
    }

    [Fact]
    public async Task OversizedFileIsRejectedBeforeReadAndApplyRejectsUnknownSelectionBeforeAssetWrites()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "oversized.aegistyles");
        await using (var file = File.Create(path))
        {
            file.SetLength(SubtitleStylePresetStore.MAXIMUM_FILE_BYTES + 1L);
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetStore.LoadAsync(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetService.ApplyAsync(SystemPreset("Style"), new(), directory.Path, [Guid.NewGuid()]));
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "assets")));
    }

    [Fact]
    public async Task ExistingLibraryIsPreservedOnInvalidEditAndCannotBeOverwrittenBySubsetExport()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        using var library = new SubtitleStylePresetLibrary(path);
        var first = SystemPreset("First");
        await library.UpsertAsync(first);
        var original = library.Snapshot;
        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(SystemPreset("FIRST")));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(path, []));
        Assert.Same(original, library.Snapshot);
        Assert.Equal(first.Id, Assert.Single((await SubtitleStylePresetStore.LoadAsync(path)).Presets).Id);
    }

    [Fact]
    public async Task FirstMutationLoadsExistingLibraryAndRefusesCorruptFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var first = SystemPreset("Existing");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [first] }, path);
        using (var library = new SubtitleStylePresetLibrary(path))
        {
            await library.UpsertAsync(SystemPreset("Added"));
            Assert.Equal(2, library.Snapshot.Presets.Length);
            Assert.Contains(library.Snapshot.Presets, item => item.Id == first.Id);
        }

        await File.WriteAllTextAsync(path, "invalid JSON");
        using var corrupted = new SubtitleStylePresetLibrary(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => corrupted.UpsertAsync(SystemPreset("Cannot overwrite")));
        Assert.Equal("invalid JSON", await File.ReadAllTextAsync(path));
        Assert.Empty(corrupted.Snapshot.Presets);
    }

    [Fact]
    public async Task DisposedLibraryRejectsFurtherOperations()
    {
        using var directory = new TemporaryProjectDirectory();
        var library = new SubtitleStylePresetLibrary(Path.Combine(directory.Path, "styles.aegistyles"));
        library.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.LoadAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.UpsertAsync(SystemPreset("Disposed")));
        library.Dispose();
    }

    [Fact]
    public async Task SwitchingToSystemFontRemovesOnlyTheSelectedStyleReference()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(1), "One");
        var second = editor.AddSubtitle(new(1), new(2), "Two");
        var preset = SystemPreset("Style") with { Style = new() { FontFamily = "New Family", Bold = true } };
        var result = await SubtitleStylePresetService.ApplyAsync(preset, editor.Snapshot, directory.Path, [first]);
        Assert.Equal(preset.Style, result.Subtitles.Single(line => line.Id == first).Style);
        Assert.Equal(editor.Snapshot.Subtitles.Single(line => line.Id == second).Style, result.Subtitles.Single(line => line.Id == second).Style);
        Assert.False(Directory.Exists(Path.Combine(directory.Path, "assets")));
    }

    private static SubtitleStylePreset SystemPreset(string name)
    {
        return new(Guid.NewGuid(), name, new());
    }
}
