using AegiNext.Rendering.Fonts;

namespace AegiNext.Rendering.Tests;

/// <summary>需要实际安装的思源黑体，其他字体的成功捕获不代替此项验收。</summary>
public sealed class MacSourceHanFontFactAttribute : FactAttribute
{
    /// <summary>按字体目录的族及全部本地化别名识别思源黑体，缺失时明确跳过。</summary>
    public MacSourceHanFontFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS and an installed Source Han Sans family.";
            return;
        }
        var catalog = new SystemFontCatalog();
        if (!catalog.InstalledFaces.Any(face => Matches(face.FamilyName) || face.Aliases.Any(Matches)))
        {
            Skip = "The actual system font catalog has no Source Han Sans, 思源黑 or 源ノ角 family or alias.";
        }
    }

    internal static bool Matches(string name)
    {
        return name.Contains("Source Han Sans", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("思源黑", StringComparison.Ordinal) || name.Contains("源ノ角", StringComparison.Ordinal);
    }
}
