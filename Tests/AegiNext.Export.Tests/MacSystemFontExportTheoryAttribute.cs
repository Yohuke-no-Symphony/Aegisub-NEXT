namespace AegiNext.Export.Tests;

/// <summary>在具备媒体工具的 macOS 主机验证已安装系统字体的真实 worker 回退。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MacSystemFontExportTheoryAttribute : TheoryAttribute
{
    /// <summary>沿用真实导出环境检查，并限定本轮系统字体覆盖的验证平台。</summary>
    public MacSystemFontExportTheoryAttribute()
    {
        Skip = OperatingSystem.IsMacOS()
            ? new ExportTheoryAttribute().Skip
            : "中文与 ZWJ emoji 系统字体回退的真实导出验收限定 macOS。";
    }
}
