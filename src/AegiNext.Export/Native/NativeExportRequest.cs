using System.Runtime.InteropServices;

namespace AegiNext.Export;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportRequest
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal int VideoStreamIndex;
    internal int Codec;
    internal int Crf;
    internal uint Width;
    internal uint Height;
    internal uint Flags;
    internal nint InputPath;
    internal nint OutputPath;
    internal nint Preset;
    internal float ReferenceWhiteNits;
    internal uint Reserved;
    internal int EncodingMode;
    internal int VideoBitrate;
    internal uint DecodeMode;
    internal uint DecodeReserved;
    internal int RateControlMode;
    internal uint RateControlReserved;
}
