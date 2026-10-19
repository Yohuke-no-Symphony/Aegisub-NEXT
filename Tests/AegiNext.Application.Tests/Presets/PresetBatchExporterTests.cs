using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

/// <summary>所选预设逐文件导出的载荷、文件名与失败清理合同。</summary>
public sealed class PresetBatchExporterTests
{
    private static readonly string[] expectedEffectFilenames = ["fade-in.aegifx", "personal.aegifx"];

    /// <summary>每个选中样式输出一个自包含文件，并保留其内嵌字体。</summary>
    [Fact]
    public async Task SelectedStylesExportAsSeparateSelfContainedFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var destination = Path.Combine(directory.Path, "export");
        var first = Style("First");
        var second = Style("Second");
        var unselected = Style("Unselected");

        await TestPresetBatchExporter.ExportStylesAsync([first, second], destination);

        Assert.Equal(2, Directory.GetFiles(destination).Length);
        var firstReadBack = Assert.Single((await TestStylePackageStore.LoadAsync(Path.Combine(destination, "First.aegistyles"))).Presets);
        var secondReadBack = Assert.Single((await TestStylePackageStore.LoadAsync(Path.Combine(destination, "Second.aegistyles"))).Presets);
        Assert.Equal(first.Id, firstReadBack.Id);
        Assert.Equal(first.Style, firstReadBack.Style);
        Assert.NotEmpty((await SubtitleStylePresetStore.LoadAsync(Path.Combine(destination, "First.aegistyles"))).FontFamilies);
        Assert.Equal(second.Id, secondReadBack.Id);
        Assert.False(File.Exists(Path.Combine(destination, unselected.Name + ".aegistyles")));
        Assert.Empty(Directory.GetDirectories(destination));
    }

    /// <summary>脚本按脚本标识命名，内置与个人源均保持原文。</summary>
    [Fact]
    public async Task SelectedEffectsIncludingBuiltinsExportOriginalSources()
    {
        using var directory = new TemporaryProjectDirectory();
        var builtin = new EffectScriptPreset(Guid.NewGuid(), "内置淡入", BuiltinEffectScripts.Get("fade-in").Source);
        var custom = Effect("personal", "与脚本标识不同的名字");

        await TestPresetBatchExporter.ExportEffectsAsync([builtin, custom], directory.Path);

        Assert.Equal(expectedEffectFilenames, Directory.GetFiles(directory.Path).Select(Path.GetFileName).Order());
        Assert.Equal(builtin.Source, await File.ReadAllTextAsync(Path.Combine(directory.Path, "fade-in.aegifx")));
        Assert.Equal(custom.Source, await File.ReadAllTextAsync(Path.Combine(directory.Path, "personal.aegifx")));
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    /// <summary>不安全字符被替换，批内同名使用稳定身份消歧且不依赖选择顺序。</summary>
    [Fact]
    public async Task UnsafeNamesAndSanitizedCollisionsHaveStablePortableFilenames()
    {
        using var directory = new TemporaryProjectDirectory();
        var first = Style("A/B");
        var second = Style("A\\B");
        var reserved = Style("CON");
        var traversal = Style("../Outside");
        var longName = Style(new string('字', 128));
        var firstDestination = Path.Combine(directory.Path, "first");
        var secondDestination = Path.Combine(directory.Path, "second");

        await TestPresetBatchExporter.ExportStylesAsync([first, second, reserved, traversal, longName], firstDestination);
        await TestPresetBatchExporter.ExportStylesAsync([longName, traversal, reserved, second, first], secondDestination);

        Assert.True(File.Exists(Path.Combine(firstDestination, $"A_B-{first.Id:N}.aegistyles")));
        Assert.True(File.Exists(Path.Combine(firstDestination, $"A_B-{second.Id:N}.aegistyles")));
        Assert.True(File.Exists(Path.Combine(firstDestination, "_CON.aegistyles")));
        Assert.Equal(Directory.GetFiles(firstDestination).Select(Path.GetFileName).Order(), Directory.GetFiles(secondDestination).Select(Path.GetFileName).Order());
        Assert.Equal(5, Directory.GetFiles(firstDestination).Length);
        Assert.Empty(Directory.GetDirectories(firstDestination));
        Assert.False(File.Exists(Path.Combine(directory.Path, "Outside.aegistyles")));
    }

    /// <summary>自然文件名与消歧后名称相同仍可产生唯一输出。</summary>
    [Fact]
    public async Task CollisionWithAnIdSuffixedNaturalNameIsResolvedBeforeWriting()
    {
        using var directory = new TemporaryProjectDirectory();
        var first = Style("A/B");
        var second = Style("A\\B");
        var third = Style($"A_B-{first.Id:N}");

        await TestPresetBatchExporter.ExportStylesAsync([first, second, third], directory.Path);

        Assert.True(File.Exists(Path.Combine(directory.Path, $"A_B-{first.Id:N}.aegistyles")));
        Assert.True(File.Exists(Path.Combine(directory.Path, $"A_B-{second.Id:N}.aegistyles")));
        Assert.True(File.Exists(Path.Combine(directory.Path, $"A_B-{first.Id:N}-{third.Id:N}.aegistyles")));
        Assert.Equal(3, Directory.GetFiles(directory.Path).Length);
    }

    /// <summary>任一目标已存在时，整批在暂存前拒绝且保留原文件。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistingDestinationRejectsTheWholeBatchBeforeWriting(bool styles)
    {
        using var directory = new TemporaryProjectDirectory();
        var extension = styles ? ".aegistyles" : ".aegifx";
        var blocked = Path.Combine(directory.Path, "second" + extension);
        await File.WriteAllTextAsync(blocked, "Retain existing content");

        if (styles)
        {
            await Assert.ThrowsAsync<IOException>(() => TestPresetBatchExporter.ExportStylesAsync([Style("first"), Style("second")], directory.Path));
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => TestPresetBatchExporter.ExportEffectsAsync([Effect("first", "First"), Effect("second", "Second")], directory.Path));
        }

        Assert.Equal("Retain existing content", await File.ReadAllTextAsync(blocked));
        Assert.Single(Directory.GetFiles(directory.Path));
        Assert.Empty(Directory.GetDirectories(directory.Path));
    }

    /// <summary>后续非法条目在输出目录创建前拒绝全部输出。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidLaterPresetRejectsTheWholeBatchBeforeWriting(bool styles)
    {
        using var directory = new TemporaryProjectDirectory();
        var destination = Path.Combine(directory.Path, "not-created");
        if (styles)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => TestPresetBatchExporter.ExportStylesAsync([Style("First"), Style("Invalid") with { Id = Guid.Empty }], destination));
        }
        else
        {
            await Assert.ThrowsAsync<EffectScriptException>(() => TestPresetBatchExporter.ExportEffectsAsync([Effect("first", "First"), Effect("second", "Second") with { Source = "invalid source" }], destination));
        }

        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    /// <summary>重复身份不允许以多个不同文件名重复导出同一条目。</summary>
    [Fact]
    public async Task DuplicateIdentitiesAreRejectedBeforeWriting()
    {
        using var directory = new TemporaryProjectDirectory();
        var style = Style("First");
        var effect = Effect("first", "First");

        await Assert.ThrowsAsync<InvalidDataException>(() => TestPresetBatchExporter.ExportStylesAsync([style, style with { Name = "Second" }], directory.Path));
        await Assert.ThrowsAsync<InvalidDataException>(() => TestPresetBatchExporter.ExportEffectsAsync([effect, Effect("second", "Second") with { Id = effect.Id }], directory.Path));

        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    /// <summary>空输入和已取消请求不产生目标目录或临时文件。</summary>
    [Fact]
    public async Task EmptyAndCanceledBatchesDoNotWriteFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var destination = Path.Combine(directory.Path, "not-created");
        await TestPresetBatchExporter.ExportStylesAsync(Array.Empty<SubtitleStylePreset>(), destination);
        await TestPresetBatchExporter.ExportEffectsAsync(Array.Empty<EffectScriptPreset>(), destination);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestPresetBatchExporter.ExportStylesAsync([Style("First")], destination, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestPresetBatchExporter.ExportEffectsAsync([Effect("first", "First")], destination, cancellation.Token));

        Assert.False(Directory.Exists(destination));
    }

    /// <summary>实际暂存开始后取消应移除本次暂存目录和临时文件。</summary>
    [Fact]
    public async Task CancellationDuringStagingRemovesAllTemporaryFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestPresetBatchExporter.ExportStylesAsync(
            [Style("First"), Style("Second")], directory.Path, () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }, cancellation.Token));
        Assert.Empty(Directory.GetFileSystemEntries(directory.Path));
    }

    /// <summary>预检查之后产生的目标冲突不得覆盖外部内容，并须清理已发布的本次文件。</summary>
    [Fact]
    public async Task DestinationCreatedDuringStagingPreservesItAndRemovesPublishedFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var blockedPath = Path.Combine(directory.Path, "Second.aegistyles");
        await Assert.ThrowsAsync<IOException>(() => TestPresetBatchExporter.ExportStylesAsync(
            [Style("First"), Style("Second")], directory.Path, () => Directory.CreateDirectory(blockedPath)));
        Assert.Equal(blockedPath, Assert.Single(Directory.GetFileSystemEntries(directory.Path)));
        Assert.True(Directory.Exists(blockedPath));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    private static SubtitleStylePreset Style(string name)
    {
        return new(Guid.NewGuid(), name, new());
    }

    private static EffectScriptPreset Effect(string id, string name)
    {
        return new(Guid.NewGuid(), name, BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{id}\"", StringComparison.Ordinal));
    }
}
