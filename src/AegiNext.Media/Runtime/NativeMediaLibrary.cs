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
            throw new ArgumentException("Only defined AegiNext native media modules can be loaded.", nameof(moduleName));
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
            var reason = !File.Exists(path) ? "The runtime file is missing"
                : error is BadImageFormatException ? "The module format or architecture does not match the current process"
                : "The file exists, but its dependencies are missing or cannot be loaded";
            throw new DllNotFoundException($"Native media module {filename}: {reason}. Process architecture: {RuntimeInformation.ProcessArchitecture}; load path: {path}. Use the complete application package for the same platform/RID. Original error: {error.Message}", error);
        }
    }
}
