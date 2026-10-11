using System.Runtime.InteropServices;

namespace AegiNext.Export;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportMuxRequest
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal nint OutputPath;
    internal nint AudioPath;
    internal int AudioStreamIndex;
    internal uint Reserved;
}
