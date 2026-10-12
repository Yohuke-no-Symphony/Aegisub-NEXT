using AegiNext.Export.Worker;
using System.Runtime.InteropServices;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class VideoEncodingOptionsTests
{
    [Fact]
    public void DefaultsKeepSoftwareQualityAndNativeAbiIsExplicitlyVersioned()
    {
        var request = CreateRequest();
        Assert.Equal(VideoEncodingMode.SOFTWARE, request.EncodingMode);
        Assert.Equal(VideoRateControlMode.AUTOMATIC, request.RateControlMode);
        Assert.Equal(VideoDecodeMode.Auto, request.DecodeMode);
        Assert.Equal(20, request.Quality);
        Assert.Equal(8000000, request.VideoBitrate);
        Assert.Equal(88, Marshal.SizeOf<NativeExportRequest>());
        Assert.Equal(64, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.EncodingMode)).ToInt32());
        VideoExporter.Validate(request);
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(200000001)]
    public void HardwareBitrateIsValidatedBeforeStartingWorker(int bitrate)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, VideoBitrate = bitrate };
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(request));
    }

    [Fact]
    public void InactiveQualityFieldsDoNotBlockTheSelectedEncodingMode()
    {
        VideoExporter.Validate(CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Quality = -1 });
        VideoExporter.Validate(CreateRequest() with { VideoBitrate = -1 });
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { EncodingMode = (VideoEncodingMode)9 }));
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { DecodeMode = (VideoDecodeMode)9 }));
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { RateControlMode = (VideoRateControlMode)9 }));
        VideoExporter.Validate(CreateRequest() with { RateControlMode = VideoRateControlMode.VBR, Quality = -1 });
        VideoExporter.Validate(CreateRequest() with { RateControlMode = VideoRateControlMode.CBR, AudioBitrate = -1 });
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { AudioMode = AudioExportMode.Aac, AudioBitrate = -1 }));
    }

    [Fact]
    public void WorkerWireCarriesHardwareModeBitrateAndActualEncoderIdentity()
    {
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mp4",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.Hevc, "slow", 20, AudioExportMode.None, 192000, VideoEncodingMode.HARDWARE, 12000000, VideoDecodeMode.Software, VideoRateControlMode.CBR),
            ProtocolVersion: ExportWire.VERSION);
        var serialized = JsonSerializer.Serialize(job, ExportWire.Options);
        var restored = Assert.IsType<ExportWorkerJob>(JsonSerializer.Deserialize<ExportWorkerJob>(serialized, ExportWire.Options));
        Assert.Equal(VideoEncodingMode.HARDWARE, restored.Video!.EncodingMode);
        Assert.Equal(12000000, restored.Video!.VideoBitrate);
        Assert.Equal(VideoDecodeMode.Software, restored.Video!.DecodeMode);
        Assert.Equal(VideoRateControlMode.CBR, restored.Video!.RateControlMode);
        Assert.Equal(ExportWire.VERSION, restored.ProtocolVersion);
        Assert.Contains("\"rate_mode\":\"CBR\"", serialized, StringComparison.Ordinal);
        ExportWire.ValidateJob(restored);
        var message = new ExportWorkerMessage("complete", 42, Encoder: "hevc_videotoolbox",
            Decoder: new(VideoDecodeMode.Software, VideoDecoderBackend.Software, false, string.Empty, 0, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0), RateControl: new(VideoRateControlMode.CBR, 12000000, 0));
        Assert.Equal(message, JsonSerializer.Deserialize<ExportWorkerMessage>(JsonSerializer.Serialize(message, ExportWire.Options), ExportWire.Options));
    }

    [Theory]
    [InlineData(VideoDecodeMode.Auto)]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Hardware)]
    public void LegacyWorkerWithoutDecodeAndEffectiveColorDiagnosticsCannotCommit(VideoDecodeMode mode)
    {
        var request = CreateRequest() with { DecodeMode = mode };
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(request,
            new("complete", 42, Encoder: "libx264")));
    }

    [Fact]
    public void RequiredHardwareDecodeCannotAcceptAWorkerSoftwareFallback()
    {
        var request = CreateRequest() with { DecodeMode = VideoDecodeMode.Hardware };
        var message = new ExportWorkerMessage("complete", 42, Encoder: "libx264",
            Decoder: new(VideoDecodeMode.Hardware, VideoDecoderBackend.Software, false, "unavailable", 1, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(request, message));
        VideoExporter.ValidateCompletedDecoder(request, message with
        {
            Decoder = new(VideoDecodeMode.Hardware, VideoDecoderBackend.VideoToolbox, true, string.Empty, 1, 42)
        });
    }

    [Theory]
    [InlineData(VideoDecoderBackend.Software, true, 1UL)]
    [InlineData(VideoDecoderBackend.VideoToolbox, false, 1UL)]
    [InlineData(VideoDecoderBackend.Software, false, 0UL)]
    public void AutomaticWorkerMustReportAConsistentActualDecoder(VideoDecoderBackend backend, bool confirmed, ulong generation)
    {
        var message = new ExportWorkerMessage("complete", 42, Encoder: "libx264",
            Decoder: new(VideoDecodeMode.Auto, backend, confirmed, string.Empty, generation, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(CreateRequest(), message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("libx264")]
    [InlineData("libx265")]
    [InlineData("hevc_videotoolbox")]
    [InlineData("h264_unknown")]
    public void HardwareWorkerMustConfirmMatchingBackendBeforeAtomicOutputCommit(string? encoder)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Codec = VideoCodec.H264 };
        var error = Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedEncoder(request, encoder));
        Assert.Contains("worker", error.Message, StringComparison.Ordinal);
        Assert.Contains("未提交成片", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VideoCodec.Auto, "h264_videotoolbox")]
    [InlineData(VideoCodec.H264, "h264_nvenc")]
    [InlineData(VideoCodec.H264, "h264_qsv")]
    [InlineData(VideoCodec.H264, "h264_amf")]
    [InlineData(VideoCodec.Hevc, "hevc_videotoolbox")]
    [InlineData(VideoCodec.Hevc, "hevc_nvenc")]
    [InlineData(VideoCodec.Hevc, "hevc_qsv")]
    [InlineData(VideoCodec.Hevc, "hevc_amf")]
    public void MatchingHardwareIdentityIsAcceptedAndSoftwareEncoderIdentityIsIndependent(VideoCodec codec, string encoder)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Codec = codec };
        VideoExporter.ValidateCompletedEncoder(request, encoder);
        VideoExporter.ValidateCompletedEncoder(request with { EncodingMode = VideoEncodingMode.SOFTWARE }, null);
    }

    /// <summary>请求应用配置只改变压制参数，保留独立的工程、解码和执行路径。</summary>
    [Fact]
    public void SettingsMappingPreservesProjectDecodeAndRuntimePaths()
    {
        var original = CreateRequest() with { DecodeMode = VideoDecodeMode.Software, FfmpegPath = "ffmpeg-original", WorkerPath = "worker-original" };
        var settings = new VideoExportSettings
        {
            Codec = VideoCodec.Hevc,
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.CBR,
            Preset = "slow",
            Quality = 30,
            VideoBitrate = 12500000,
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        };

        var changed = original.WithSettings(settings);

        Assert.Equal(settings, changed.ToSettings());
        Assert.Same(original.Project, changed.Project);
        Assert.Equal(original.ProjectDirectory, changed.ProjectDirectory);
        Assert.Equal(original.OutputPath, changed.OutputPath);
        Assert.Equal(original.DecodeMode, changed.DecodeMode);
        Assert.Equal(original.FfmpegPath, changed.FfmpegPath);
        Assert.Equal(original.WorkerPath, changed.WorkerPath);
        Assert.Equal(VideoRateControlMode.AUTOMATIC, original.RateControlMode);
        Assert.Throws<ArgumentNullException>(() => original.WithSettings(null!));
    }

    /// <summary>原生请求只传递当前码控方式的有效参数，且兼容自动方式先解析为显式值。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.AUTOMATIC, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.AUTOMATIC, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CRF, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR, VideoRateControlMode.CBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR, VideoRateControlMode.CBR)]
    public void NativeArgumentsOnlyCarryEffectiveRateControlFields(VideoEncodingMode encodingMode,
        VideoRateControlMode mode, VideoRateControlMode effectiveMode)
    {
        var request = CreateRequest() with { EncodingMode = encodingMode, RateControlMode = mode, Quality = 25, VideoBitrate = 12500000 };

        var arguments = NativeVideoExport.CreateRequestArguments(request);

        Assert.Equal((int)effectiveMode, arguments.RateControlMode);
        Assert.Equal(effectiveMode == VideoRateControlMode.CRF ? 25 : 0, arguments.Quality);
        Assert.Equal(effectiveMode == VideoRateControlMode.CRF ? 0 : 12500000, arguments.VideoBitrate);
        Assert.Equal(0U, arguments.RateControlReserved);
        Assert.Equal(88U, arguments.StructSize);
        Assert.Equal(6U, arguments.AbiVersion);
    }

    /// <summary>回执读取原生结果字段并拒绝未知保留位，不能由 managed 请求生成替代回执。</summary>
    [Fact]
    public void RateControlReceiptReadsNativeResultFields()
    {
        var info = new NativeExportResultInfo { RateControlMode = (int)VideoRateControlMode.CBR, VideoBitrate = 12345678, Quality = 0, Verification = 1, BitratePrecision = 1000 };

        Assert.Equal(new(VideoRateControlMode.CBR, 12345678, 0), NativeVideoExport.ReadRateControl(info));
        info.Verification = 0;
        Assert.Throws<InvalidDataException>(() => NativeVideoExport.ReadRateControl(info));
    }

    /// <summary>旧请求缺少协议版本，或新协议没有明确码控方式时，worker 在编码前拒绝。</summary>
    [Theory]
    [InlineData(0, VideoRateControlMode.CRF)]
    [InlineData(1, VideoRateControlMode.CRF)]
    [InlineData(2, VideoRateControlMode.CRF)]
    [InlineData(ExportWire.VERSION, VideoRateControlMode.AUTOMATIC)]
    [InlineData(ExportWire.VERSION, (VideoRateControlMode)9)]
    public void WorkerRejectsMismatchedProtocolAndUnresolvedModes(int version, VideoRateControlMode mode)
    {
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mp4",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.H264, "medium", 20, AudioExportMode.None, 192000, RateControlMode: mode),
            ProtocolVersion: version);

        Assert.Throws<InvalidDataException>(() => ExportWire.ValidateJob(job));
    }

    /// <summary>缺少新版字段的旧 JSON 不能通过属性默认值冒充当前 worker 协议。</summary>
    [Fact]
    public void MissingProtocolVersionInLegacyJsonIsRejected()
    {
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options), Path.GetTempPath(), Path.GetTempPath(), ".mp4",
            "ffmpeg", ExportWorkerOperation.BURN_IN, new(VideoCodec.H264, "medium", 20, AudioExportMode.None, 192000, RateControlMode: VideoRateControlMode.CRF),
            ProtocolVersion: ExportWire.VERSION);
        var json = JsonSerializer.Serialize(job, ExportWire.Options).Replace(
             $",\"protocolVersion\":{ExportWire.VERSION}", string.Empty, StringComparison.Ordinal);
        var legacy = JsonSerializer.Deserialize<ExportWorkerJob>(json, ExportWire.Options)!;

        Assert.Equal(0, legacy.ProtocolVersion);
        Assert.Throws<InvalidDataException>(() => ExportWire.ValidateJob(legacy));
    }

    /// <summary>缺少回执或实际模式、有效值和非活动字段不匹配时，Host 拒绝提交。</summary>
    [Theory]
    [InlineData(VideoRateControlMode.CRF, VideoRateControlMode.CRF, 0, 19)]
    [InlineData(VideoRateControlMode.CRF, VideoRateControlMode.CRF, 8000000, 20)]
    [InlineData(VideoRateControlMode.CRF, VideoRateControlMode.VBR, 8000000, 0)]
    [InlineData(VideoRateControlMode.VBR, VideoRateControlMode.VBR, 7999999, 0)]
    [InlineData(VideoRateControlMode.VBR, VideoRateControlMode.VBR, 8000000, 20)]
    [InlineData(VideoRateControlMode.CBR, VideoRateControlMode.VBR, 8000000, 0)]
    [InlineData(VideoRateControlMode.CBR, VideoRateControlMode.AUTOMATIC, 8000000, 0)]
    public void InconsistentNativeRateControlCannotCommit(VideoRateControlMode requestedMode,
        VideoRateControlMode actualMode, int bitrate, int crf)
    {
        var request = CreateRequest() with { RateControlMode = requestedMode };

        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedRateControl(request, new(actualMode, bitrate, crf)));
        var missing = JsonSerializer.Deserialize<ExportWorkerMessage>("{\"type\":\"complete\",\"frames\":42}", ExportWire.Options)!;
        Assert.Null(missing.RateControl);
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedRateControl(request, missing.RateControl));
    }

    /// <summary>匹配的显式回执才通过 Host 验证，兼容请求按原 CPU／GPU 行为解析。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.AUTOMATIC, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.AUTOMATIC, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CRF, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR, VideoRateControlMode.CBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR, VideoRateControlMode.CBR)]
    public void MatchingActualRateControlIsAccepted(VideoEncodingMode encodingMode,
        VideoRateControlMode requestedMode, VideoRateControlMode actualMode)
    {
        var request = CreateRequest() with { EncodingMode = encodingMode, RateControlMode = requestedMode };
        var receipt = new VideoRateControlInfo(actualMode, actualMode == VideoRateControlMode.CRF ? 0 : request.VideoBitrate,
            actualMode == VideoRateControlMode.CRF ? request.Quality : 0);

        VideoExporter.ValidateCompletedRateControl(request, receipt);
    }

    /// <summary>Host 只接受编码器共同精度的有效目标，原始便携值不能冒充 native 已使用的值。</summary>
    [Fact]
    public void NativeBitrateAndReceiptUseNormalizedWholeKilobits()
    {
        var request = CreateRequest() with { RateControlMode = VideoRateControlMode.VBR, VideoBitrate = 8000999 };
        var arguments = NativeVideoExport.CreateRequestArguments(request);

        Assert.Equal(8000000, arguments.VideoBitrate);
        Assert.Equal(8000999, request.VideoBitrate);
        VideoExporter.ValidateCompletedRateControl(request, new(VideoRateControlMode.VBR, 8000000, 0));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedRateControl(request,
            new(VideoRateControlMode.VBR, 8000999, 0)));
    }

    private static VideoExportRequest CreateRequest()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(Path.GetTempPath(), "encoding-options.mp4"));
        var document = new ProjectDocument
        {
            Width = 96, Height = 64, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        };
        return new(document, Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "encoded-options.mp4"));
    }
}
