using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Media.Decoding;
using AegiNext.Media.Runtime;

namespace AegiNext.Export;

internal static class NativeVideoExport
{
    internal static unsafe NativeVideoExportSession Run(VideoExportRequest request, string videoOutput, IProgress<VideoExportProgress>? progress, CancellationToken cancellationToken)
    {
        VideoExporter.Validate(request);
        NativeExportAbi.Validate(NativeExportMethods.AbiVersion(), NativeMediaLibrary.GetPath("aeginext_export"));
        var decodeCore = FfmpegVideoDecoder.GetCoreInfo();
        NativeExportAbi.ValidateCore(NativeExportMethods.CoreVersion(), decodeCore.Version,
            NativeExportMethods.Capabilities(), decodeCore.Capabilities, NativeMediaLibrary.GetPath("aeginext_export"));

        var inputAsset = request.Project.Assets.Single(asset => asset.Id == request.Project.Media!.AssetId);
        var input = ProjectAssetLocation.Resolve(inputAsset, request.ProjectDirectory);
        using var render = new ExportRenderContext(request.Project, request.ProjectDirectory, progress, cancellationToken);
        var handle = GCHandle.Alloc(render);
        var arguments = CreateRequestArguments(request);
        var error = stackalloc byte[1024];
        nint native = 0;
        try
        {
            arguments.InputPath = Marshal.StringToCoTaskMemUTF8(input);
            arguments.OutputPath = Marshal.StringToCoTaskMemUTF8(videoOutput);
            arguments.Preset = Marshal.StringToCoTaskMemUTF8(request.Preset);
            Check(NativeExportMethods.Create(out native, error, 1024), error, cancellationToken);
            render.NativeContext = native;
            using var cancelled = cancellationToken.Register(() => NativeExportMethods.Cancel(native));
            var result = NativeExportMethods.Run(native, ref arguments, &Render, GCHandle.ToIntPtr(handle), out var frames, error, 1024);
            if (render.Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            Check(result, error, cancellationToken);
            var session = new NativeVideoExportSession(native, frames);
            native = 0;
            return session;
        }
        finally
        {
            if (native != 0)
            {
                NativeExportMethods.Destroy(native);
            }

            handle.Free();
            Marshal.FreeCoTaskMem(arguments.InputPath);
            Marshal.FreeCoTaskMem(arguments.OutputPath);
            Marshal.FreeCoTaskMem(arguments.Preset);
        }
    }

    internal static NativeExportRequest CreateRequestArguments(VideoExportRequest request)
    {
        var settings = VideoExportSettingsValidator.Normalize(request.ToSettings());
        var mode = settings.RateControlMode;
        return new()
        {
            StructSize = NativeExportAbi.REQUEST_SIZE,
            AbiVersion = NativeExportAbi.VERSION,
            VideoStreamIndex = request.Project.Media!.VideoStreamIndex,
            Codec = (int)request.Codec,
            Quality = mode is VideoRateControlMode.CRF or VideoRateControlMode.CQ ? settings.Quality : 0,
            Width = (uint)request.Project.Width,
            Height = (uint)request.Project.Height,
            ReferenceWhiteNits = (float)request.Project.ReferenceWhiteNits,
            EncodingMode = (int)request.EncodingMode,
            VideoBitrate = mode == VideoRateControlMode.CRF ? 0 : settings.VideoBitrate,
            DecodeMode = (uint)request.DecodeMode,
            RateControlMode = (int)mode
        };
    }

    internal static VideoRateControlInfo ReadRateControl(NativeExportResultInfo info)
    {
        if (!Enum.IsDefined((VideoRateControlVerification)info.Verification) || info.BitratePrecision is 0 or > 1000000)
        {
            throw new InvalidDataException("原生导出码控证据或码率精度无效。");
        }

        return new((VideoRateControlMode)info.RateControlMode, info.VideoBitrate, info.Quality,
            (VideoRateControlVerification)info.Verification, checked((int)info.BitratePrecision));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int Render(nint context, long pts, int numerator, int denominator, uint width, uint height,
        float* pixels, ulong channels, NativeExportOverlayInfo* overlay)
    {
        try
        {
            return ((ExportRenderContext)GCHandle.FromIntPtr(context).Target!).Render(pts, numerator, denominator,
                width, height, pixels, channels, overlay);
        }
        catch
        {
            return 2;
        }
    }

    internal static unsafe void Check(int result, byte* error, CancellationToken cancellationToken)
    {
        if (result == 0)
        {
            return;
        }

        if (result == 4)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        var bytes = new ReadOnlySpan<byte>(error, 1024);
        var end = bytes.IndexOf((byte)0);
        var message = System.Text.Encoding.UTF8.GetString(bytes[..(end < 0 ? bytes.Length : end)]);
        if (result == 2)
        {
            throw new NotSupportedException(message);
        }

        throw new InvalidOperationException(message);
    }
}
