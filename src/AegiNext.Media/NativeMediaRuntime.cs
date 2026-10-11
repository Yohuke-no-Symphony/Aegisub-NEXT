using System.Reflection;
using System.Runtime.InteropServices;
using AegiNext.Media.Runtime;

namespace AegiNext.Media;

internal static class NativeMediaRuntime
{
    static NativeMediaRuntime()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMediaRuntime).Assembly, Resolve);
    }

    internal static void Initialize()
    {
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name is not ("aeginext_decode" or "aeginext_audio" or "aeginext_media"))
        {
            return 0;
        }

        return NativeMediaLibrary.Load(name);
    }
}
