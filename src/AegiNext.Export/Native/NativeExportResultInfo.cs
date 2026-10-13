using System.Runtime.InteropServices;

namespace AegiNext.Export;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeExportResultInfo
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal uint CoreVersion;
    internal uint Capabilities;
    internal uint RequestedDecodeMode;
    internal uint ActiveDecodeBackend;
    internal uint HardwareConfirmed;
    internal uint InferredFields;
    internal ulong Generation;
    internal ulong DeliveredFrames;
    internal int ColorRange;
    internal int ColorMatrix;
    internal int ColorPrimaries;
    internal int ColorTransfer;
    internal int ChromaLocation;
    internal int AlphaMode;
    internal fixed byte FallbackReason[256];
    internal int RateControlMode;
    internal int VideoBitrate;
    internal int Quality;
    internal uint Verification;
    internal int Codec;
    internal int BitDepth;
    internal fixed byte PixelFormat[32];
    internal NativeExportHdrMetadata Hdr;
    internal uint OutputVerification;
    internal uint BitratePrecision;
    internal uint OutputWidth;
    internal uint OutputHeight;
    internal uint PaddingTop;
    internal uint PaddingBottom;
    internal uint PaddingLeft;
    internal uint PaddingRight;
}
