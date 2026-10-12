using System.Runtime.InteropServices;
using AegiNext.Core.Media;

namespace AegiNext.Export;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportRational
{
    internal int Numerator;
    internal int Denominator;

    internal readonly MediaRatio ToMetadata() => Denominator > 0
        ? new(Numerator, Denominator)
        : throw new InvalidDataException("The native HDR metadata contains an invalid denominator.");
}
