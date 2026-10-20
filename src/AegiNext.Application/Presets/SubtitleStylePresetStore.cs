using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Application.Presets;

/// <summary>只接受当前版本的 .aegistyles ZIP，样式元数据与去重字体文件分离保存。</summary>
public static class SubtitleStylePresetStore
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目预算常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = EffectLibraryPackageStore.MAXIMUM_FILE_BYTES;

    /// <summary>流式读取明确声明字体携带策略的样式包，旧版 JSON 不受支持。</summary>
    public static async Task<SubtitleStylePackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var package = await EffectLibraryPackageStore.LoadCoreAsync(path, true, cancellationToken).ConfigureAwait(false);
        return new() { Styles = package.Styles, IncludesFonts = package.IncludesFonts, FontFamilies = package.FontFamilies };
    }

    /// <summary>完整验证字体与样式后原子替换样式包。</summary>
    public static Task SaveAsync(SubtitleStylePackage package, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(package, path, null, cancellationToken);
    }

    /// <summary>全部 ZIP 内容写入后进入宿主提交边界。</summary>
    public static Task SaveAsync(SubtitleStylePackage package, string path, Action? beforeCommit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        return EffectLibraryPackageStore.SaveCoreAsync(new() { Styles = package.Styles, IncludesFonts = package.IncludesFonts, FontFamilies = package.FontFamilies },
            path, true, beforeCommit, cancellationToken);
    }

    /// <summary>编码当前样式包，用于内存交换或格式校验。</summary>
    public static byte[] Serialize(SubtitleStylePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return EffectLibraryPackageStore.SerializeCore(new() { Styles = package.Styles, IncludesFonts = package.IncludesFonts, FontFamilies = package.FontFamilies }, true, default);
    }

    /// <summary>校验当前样式包并返回元数据、字体携带声明及其字体族。</summary>
    public static SubtitleStylePackage Deserialize(ReadOnlySpan<byte> bytes)
    {
        var package = EffectLibraryPackageStore.DeserializeCore(bytes, true, default);
        return new() { Styles = package.Styles, IncludesFonts = package.IncludesFonts, FontFamilies = package.FontFamilies };
    }
}
