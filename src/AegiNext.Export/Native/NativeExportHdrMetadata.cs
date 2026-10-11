using System.Runtime.InteropServices;
using AegiNext.Core.Media;

namespace AegiNext.Export;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportHdrMetadata
{
    internal uint Flags;
    internal uint Reserved;
    internal NativeExportRational RedX;
    internal NativeExportRational RedY;
    internal NativeExportRational GreenX;
    internal NativeExportRational GreenY;
    internal NativeExportRational BlueX;
    internal NativeExportRational BlueY;
    internal NativeExportRational WhiteX;
    internal NativeExportRational WhiteY;
    internal NativeExportRational MinLuminance;
    internal NativeExportRational MaxLuminance;
    internal uint MaxCll;
    internal uint MaxFall;

    internal readonly MediaMasteringDisplayInfo? GetMastering()
    {
        Validate();
        return (Flags & 3) == 0 ? null : new()
        {
            RedX = (Flags & 1) != 0 ? RedX.ToMetadata() : null,
            RedY = (Flags & 1) != 0 ? RedY.ToMetadata() : null,
            GreenX = (Flags & 1) != 0 ? GreenX.ToMetadata() : null,
            GreenY = (Flags & 1) != 0 ? GreenY.ToMetadata() : null,
            BlueX = (Flags & 1) != 0 ? BlueX.ToMetadata() : null,
            BlueY = (Flags & 1) != 0 ? BlueY.ToMetadata() : null,
            WhitePointX = (Flags & 1) != 0 ? WhiteX.ToMetadata() : null,
            WhitePointY = (Flags & 1) != 0 ? WhiteY.ToMetadata() : null,
            MinLuminance = (Flags & 2) != 0 ? MinLuminance.ToMetadata() : null,
            MaxLuminance = (Flags & 2) != 0 ? MaxLuminance.ToMetadata() : null
        };
    }

    internal readonly MediaContentLightInfo? GetContentLight()
    {
        Validate();
        return (Flags & 4) == 0 ? null : new() { MaxContentLightLevel = MaxCll, MaxFrameAverageLightLevel = MaxFall };
    }

    private readonly void Validate()
    {
        if (Reserved != 0 || (Flags & ~7U) != 0)
        {
            throw new InvalidDataException("原生 HDR 元数据标记无效。");
        }
    }
}
