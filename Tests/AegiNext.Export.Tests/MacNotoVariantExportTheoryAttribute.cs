using SkiaSharp;

namespace AegiNext.Export.Tests;

/// <summary>限定真实命名字体导出验收的媒体工具、平台及独立／可变 Noto 字体来源。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MacNotoVariantExportTheoryAttribute : TheoryAttribute
{
    private const uint FVAR_TAG = 0x66766172;

    /// <summary>缺少验收环境时明确跳过，不把字体回退结果计为命名变体证据。</summary>
    public MacNotoVariantExportTheoryAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "系统 Noto 命名变体的真实导出验收限定 macOS；Windows 字体后端未在此测试中验收。";
            return;
        }

        Skip = new ExportTheoryAttribute().Skip;
        if (Skip is not null)
        {
            return;
        }

        var manager = SKFontManager.Default;
        var installed = manager.GetFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!installed.IsSupersetOf(["Noto Sans SC", "Noto Serif SC"]))
        {
            Skip = "真实变体导出验收需要已安装 Noto Sans SC 和 Noto Serif SC。";
            return;
        }

        if (!HasFontSource(manager, "Noto Sans SC", variable: false) ||
            !HasFontSource(manager, "Noto Serif SC", variable: true) ||
            HasFontSource(manager, "Noto Serif SC", variable: false))
        {
            Skip = "真实变体导出验收需要独立字重 Noto Sans SC，以及只安装可变字体的 Noto Serif SC。";
        }
    }

    private static bool HasFontSource(SKFontManager manager, string family, bool variable)
    {
        using var styles = manager.GetFontStyles(family);
        for (var index = 0; index < styles.Count; index++)
        {
            using var typeface = styles.CreateTypeface(index);
            if (typeface is not null && (typeface.GetTableSize(FVAR_TAG) > 0) == variable)
            {
                return true;
            }
        }

        return false;
    }
}
