using System.Runtime.InteropServices;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

internal sealed class NativeVideoExportSession(nint context, ulong frames) : IDisposable
{
    private nint native = context;

    internal unsafe NativeVideoExportResult ReadResult(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(native == 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var info = new NativeExportResultInfo { StructSize = NativeExportAbi.RESULT_SIZE, AbiVersion = NativeExportAbi.VERSION };
        var error = stackalloc byte[1024];
        NativeVideoExport.Check(NativeExportMethods.GetResultInfo(native, ref info, error, 1024), error, cancellationToken);
        var decoder = new VideoDecodeSessionInfo((VideoDecodeMode)info.RequestedDecodeMode,
            (VideoDecoderBackend)info.ActiveDecodeBackend, info.HardwareConfirmed != 0,
            ReadString(info.FallbackReason, 256), info.Generation, info.DeliveredFrames);
        var color = new VideoExportColor(info.ColorRange, info.ColorMatrix, info.ColorPrimaries, info.ColorTransfer,
            info.ChromaLocation, info.AlphaMode, info.InferredFields, (VideoCodec)info.Codec, info.BitDepth,
            ReadString(info.PixelFormat, 32), info.Hdr.GetMastering(), info.Hdr.GetContentLight(), info.OutputVerification);
        return new(frames, Marshal.PtrToStringUTF8(NativeExportMethods.EncoderName(native)) ?? string.Empty,
            decoder, color, NativeVideoExport.ReadRateControl(info));
    }

    internal unsafe void Mux(string outputPath, string? audioPath, int audioStreamIndex, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(native == 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var arguments = new NativeExportMuxRequest
        {
            StructSize = (uint)sizeof(NativeExportMuxRequest), AbiVersion = NativeExportAbi.VERSION,
            AudioStreamIndex = audioPath is null ? -1 : audioStreamIndex
        };
        var error = stackalloc byte[1024];
        try
        {
            arguments.OutputPath = Marshal.StringToCoTaskMemUTF8(outputPath);
            arguments.AudioPath = audioPath is null ? 0 : Marshal.StringToCoTaskMemUTF8(audioPath);
            using var cancelled = cancellationToken.Register(() => NativeExportMethods.Cancel(native));
            NativeVideoExport.Check(NativeExportMethods.Mux(native, ref arguments, error, 1024), error, cancellationToken);
        }
        finally
        {
            Marshal.FreeCoTaskMem(arguments.OutputPath);
            Marshal.FreeCoTaskMem(arguments.AudioPath);
        }
    }

    public void Dispose()
    {
        if (native != 0)
        {
            NativeExportMethods.Destroy(native);
            native = 0;
        }
    }

    private static unsafe string ReadString(byte* buffer, int capacity)
    {
        var bytes = new ReadOnlySpan<byte>(buffer, capacity);
        var end = bytes.IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(bytes[..(end < 0 ? bytes.Length : end)]);
    }
}
