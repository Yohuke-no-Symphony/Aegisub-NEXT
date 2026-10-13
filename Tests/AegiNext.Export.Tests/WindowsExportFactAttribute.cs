namespace AegiNext.Export.Tests;

/// <summary>仅在 Windows 上验证文件共享锁的真实删除行为。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsExportFactAttribute : FactAttribute
{
    /// <summary>根据当前平台启用 Windows 文件锁测试。</summary>
    public WindowsExportFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows 文件共享锁测试需要在 Windows 上运行。";
        }
    }
}
