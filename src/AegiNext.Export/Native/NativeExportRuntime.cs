using System.Reflection;
using System.Runtime.InteropServices;
using AegiNext.Media.Runtime;

namespace AegiNext.Export;

internal static class NativeExportRuntime
{
    static NativeExportRuntime()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeExportRuntime).Assembly, Resolve);
    }

    internal static void Initialize()
    {
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return name == "aeginext_export" ? NativeMediaLibrary.Load(name) : 0;
    }
}
