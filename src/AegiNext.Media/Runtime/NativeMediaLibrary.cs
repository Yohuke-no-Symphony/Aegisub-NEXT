using System.Runtime.InteropServices;

namespace AegiNext.Media.Runtime;

/// <summary>解析和加载应用目录内的原生媒体模块，保留平台及依赖诊断。</summary>
public static class NativeMediaLibrary
{
    /// <summary>取得与当前平台匹配的邻接模块完整路径。</summary>
    public static string GetPath(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        if (moduleName is not ("aeginext_decode" or "aeginext_audio" or "aeginext_media" or "aeginext_export"))
        {
            throw new ArgumentException("只能加载已定义的 AegiNext 原生媒体模块。", nameof(moduleName));
        }
        var filename = OperatingSystem.IsWindows() ? moduleName + ".dll"
            : OperatingSystem.IsMacOS() ? "lib" + moduleName + ".dylib" : "lib" + moduleName + ".so";
        return Path.Combine(AppContext.BaseDirectory, filename);
    }

    /// <summary>加载邻接模块；缺失、架构不匹配或依赖失败时报告实际路径。</summary>
    public static nint Load(string moduleName)
    {
        var path = GetPath(moduleName);
        var filename = Path.GetFileName(path);
        try
        {
            return NativeLibrary.Load(path);
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException)
        {
            var reason = !File.Exists(path) ? "运行时文件缺失"
                : error is BadImageFormatException ? "模块格式或架构与当前进程不匹配"
                : "文件已存在，但其依赖库缺失或无法加载";
            throw new DllNotFoundException($"原生媒体模块 {filename}：{reason}。进程架构 {RuntimeInformation.ProcessArchitecture}；加载路径 {path}。请使用同一平台/RID 的完整应用包。原始错误：{error.Message}", error);
        }
    }
}
