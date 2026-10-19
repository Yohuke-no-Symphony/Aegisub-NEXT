using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Projects;
using AegiNext.Export.Worker;
using AegiNext.Media.Decoding;

namespace AegiNext.Export.Tests;

/// <summary>成片尺寸回执只允许实际 AMF AV1 编码报告的右侧和底部补边。</summary>
public sealed class VideoExportGeometryTests
{
    /// <summary>经过验证的 AMF 补边必须保留编码后的实际尺寸。</summary>
    [Theory]
    [InlineData(1920, 1080, 0, 0)]
    [InlineData(1920, 1082, 2, 0)]
    [InlineData(1922, 1082, 2, 2)]
    public void AmfAv1AcceptsReportedRightAndBottomPadding(int width, int height, int bottom, int right)
    {
        var request = CreateRequest();
        var geometry = new VideoExportGeometry(width, height, PaddingBottom: bottom, PaddingRight: right);
        var encoded = CreateEncodedResult(geometry);

        VideoExporter.ValidateCompletedEncoder(request, encoded.Encoder);
        VideoExporter.ValidateCompletedOutput(request, encoded.OutputColor);
        VideoExporter.ValidateCompletedGeometry(request, encoded.Encoder, geometry);
        ExportMuxer.ValidateCompletedVideo(request, encoded, CreateVideoStream(width, height));
    }

    /// <summary>丢失尺寸回执的旧 worker 不能通过当前提交边界。</summary>
    [Theory]
    [InlineData("{\"type\":\"complete\",\"encoder\":\"av1_amf\"}")]
    [InlineData("{\"type\":\"complete\",\"encoder\":\"av1_amf\",\"outputGeometry\":null}")]
    public void MissingGeometryInWorkerReceiptCannotCommit(string json)
    {
        var completed = JsonSerializer.Deserialize<ExportWorkerMessage>(json, ExportWire.Options)!;

        Assert.Null(completed.OutputGeometry);
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedGeometry(
            CreateRequest(), completed.Encoder, completed.OutputGeometry));
    }

    /// <summary>无效尺寸、负补边、顶部或左侧补边以及缺少证据的尺寸漂移都必须拒绝。</summary>
    [Theory]
    [InlineData(0, 1080, 0, 0, 0, 0)]
    [InlineData(-1, 1080, 0, 0, 0, 0)]
    [InlineData(1920, 0, 0, 0, 0, 0)]
    [InlineData(1920, -1, 0, 0, 0, 0)]
    [InlineData(1920, 1082, 2, 0, 0, 0)]
    [InlineData(1922, 1080, 0, 0, 2, 0)]
    [InlineData(1920, 1080, -1, 0, 0, 0)]
    [InlineData(1920, 1080, 0, -1, 0, 0)]
    [InlineData(1920, 1080, 0, 0, -1, 0)]
    [InlineData(1920, 1080, 0, 0, 0, -1)]
    [InlineData(1920, 1082, 0, 0, 0, 0)]
    [InlineData(1922, 1080, 0, 0, 0, 0)]
    [InlineData(1920, 1082, 0, 1, 0, 0)]
    [InlineData(1920, 1080, 0, int.MaxValue, 0, 0)]
    [InlineData(1920, 1080, 0, 0, 0, int.MaxValue)]
    public void InvalidGeometryCannotCommit(int width, int height, int top, int bottom, int left, int right)
    {
        var geometry = new VideoExportGeometry(width, height, top, bottom, left, right);

        var error = Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedGeometry(
            CreateRequest(), "av1_amf", geometry));

        Assert.Contains("output was not committed", error.Message, StringComparison.Ordinal);
    }

    /// <summary>允许补边的身份不能从软件模式、其他编码器或自动编码请求推断。</summary>
    [Theory]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, "av1_qsv")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, "av1_nvenc")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, null)]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, "")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.SOFTWARE, "av1_amf")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.SOFTWARE, "libsvtav1")]
    [InlineData(VideoCodec.H264, VideoEncodingMode.HARDWARE, "h264_amf")]
    [InlineData(VideoCodec.Hevc, VideoEncodingMode.HARDWARE, "hevc_amf")]
    [InlineData(VideoCodec.Auto, VideoEncodingMode.HARDWARE, "av1_amf")]
    public void PaddingCannotCrossEncoderCodecOrMode(VideoCodec codec, VideoEncodingMode mode, string? encoder)
    {
        var request = CreateRequest() with { Codec = codec, EncodingMode = mode };

        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedGeometry(
            request, encoder, new(1920, 1082, PaddingBottom: 2)));
    }

    /// <summary>其他编码器只能提交与工程完全相等的尺寸。</summary>
    [Theory]
    [InlineData(VideoCodec.H264, VideoEncodingMode.HARDWARE, "h264_amf")]
    [InlineData(VideoCodec.Hevc, VideoEncodingMode.HARDWARE, "hevc_qsv")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, "av1_nvenc")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.HARDWARE, "av1_qsv")]
    [InlineData(VideoCodec.Av1, VideoEncodingMode.SOFTWARE, "libsvtav1")]
    public void UnpaddedGeometryRemainsExactForOtherEncoders(VideoCodec codec, VideoEncodingMode mode, string encoder)
    {
        var request = CreateRequest() with { Codec = codec, EncodingMode = mode };

        VideoExporter.ValidateCompletedGeometry(request, encoder, new(1920, 1080));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedGeometry(
            request, encoder, new(1920, 1082)));
    }

    /// <summary>画布和补边相加越界时必须拒绝，不能绕回成为有效尺寸。</summary>
    [Fact]
    public void CanvasAndPaddingArithmeticCannotOverflow()
    {
        var request = CreateRequest();
        request = request with { Project = request.Project with { Width = int.MaxValue, Height = int.MaxValue } };

        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedGeometry(
            request, "av1_amf", new(int.MaxValue, int.MaxValue, PaddingBottom: 2, PaddingRight: 2)));
    }

    /// <summary>回执必须直接读取原生尺寸与补边字段。</summary>
    [Fact]
    public void NativeGeometryReceiptReadsTheReportedFields()
    {
        var info = new NativeExportResultInfo
        {
            OutputWidth = 1944,
            OutputHeight = 1106,
            PaddingTop = 3,
            PaddingBottom = 23,
            PaddingLeft = 5,
            PaddingRight = 19
        };

        Assert.Equal(new(1944, 1106, 3, 23, 5, 19), NativeVideoExport.ReadGeometry(info));
    }

    /// <summary>原生无效尺寸或超出托管表示范围的值不能截断。</summary>
    [Fact]
    public void NativeGeometryRejectsMissingDimensionsAndValuesOutsideManagedRange()
    {
        var info = new NativeExportResultInfo { OutputWidth = 1920, OutputHeight = 1082, PaddingBottom = 2 };
        info.OutputWidth = 0;
        Assert.Throws<InvalidDataException>(() => NativeVideoExport.ReadGeometry(info));
        info.OutputWidth = uint.MaxValue;
        Assert.Throws<InvalidDataException>(() => NativeVideoExport.ReadGeometry(info));
        info.OutputWidth = 1920;
        info.PaddingBottom = uint.MaxValue;
        Assert.Throws<InvalidDataException>(() => NativeVideoExport.ReadGeometry(info));
    }

    /// <summary>补边不能放宽最终文件的编码、尺寸、像素格式或色彩校验。</summary>
    [Theory]
    [InlineData("codec")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("pixelFormat")]
    [InlineData("range")]
    [InlineData("matrix")]
    [InlineData("primaries")]
    [InlineData("transfer")]
    public void FinalProbeMustMatchActualEncodedGeometryAndColor(string changedField)
    {
        var encoded = CreateEncodedResult(new(1920, 1082, PaddingBottom: 2));
        var stream = CreateVideoStream(1920, 1082);
        var video = stream.Video!;
        stream = changedField switch
        {
            "codec" => stream with { CodecName = "hevc" },
            "width" => stream with { Video = video with { Width = 1922 } },
            "height" => stream with { Video = video with { Height = 1080 } },
            "pixelFormat" => stream with { Video = video with { PixelFormat = "yuv420p10le" } },
            "range" => stream with { Video = video with { Color = video.Color with { Range = "pc" } } },
            "matrix" => stream with { Video = video with { Color = video.Color with { Matrix = "bt2020nc" } } },
            "primaries" => stream with { Video = video with { Color = video.Color with { Primaries = "bt2020" } } },
            "transfer" => stream with { Video = video with { Color = video.Color with { Transfer = "smpte2084" } } },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField))
        };

        var error = Assert.Throws<InvalidDataException>(() => ExportMuxer.ValidateCompletedVideo(CreateRequest(), encoded, stream));

        Assert.Contains("Actual codec=", error.Message, StringComparison.Ordinal);
        Assert.Contains("expected codec=av1, size=1920x1082", error.Message, StringComparison.Ordinal);
        Assert.Contains("encoder=av1_amf", error.Message, StringComparison.Ordinal);
        Assert.Contains("canvas=1920x1080", error.Message, StringComparison.Ordinal);
        Assert.Contains("padding(top/bottom/left/right)=0/2/0/0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>编码结束即可验证尺寸，最终提交仍须等待封装验证完成。</summary>
    [Fact]
    public void GeometryCanBeValidatedBeforeFinalContainerVerification()
    {
        var request = CreateRequest();
        var encoded = CreateEncodedResult(new(1920, 1082, PaddingBottom: 2));
        encoded = encoded with { OutputColor = encoded.OutputColor with { OutputVerification = 1 } };

        VideoExporter.ValidateCompletedGeometry(request, encoded.Encoder, encoded.OutputGeometry);
        Assert.Throws<InvalidDataException>(() => ExportMuxer.ValidateCompletedVideo(
            request, encoded, CreateVideoStream(1920, 1082)));
    }

    /// <summary>最终封装验证、实际编码和 HDR 位深合同仍是必需条件。</summary>
    [Theory]
    [InlineData(0U, VideoCodec.Av1, 8, "yuv420p", 1)]
    [InlineData(1U, VideoCodec.Av1, 8, "yuv420p", 1)]
    [InlineData(2U, VideoCodec.Av1, 8, "yuv420p", 1)]
    [InlineData(3U, VideoCodec.Hevc, 8, "yuv420p", 1)]
    [InlineData(3U, VideoCodec.Av1, 8, "yuv420p10le", 1)]
    [InlineData(3U, VideoCodec.Av1, 8, "yuv420p", 16)]
    [InlineData(3U, VideoCodec.Av1, 8, "yuv420p", 18)]
    public void PaddingDoesNotReplaceCompleteOutputEvidence(uint verified, VideoCodec codec, int depth, string pixelFormat, int transfer)
    {
        var encoded = CreateEncodedResult(new(1920, 1082, PaddingBottom: 2));
        encoded = encoded with
        {
            OutputColor = encoded.OutputColor with
            {
                OutputVerification = verified,
                Codec = codec,
                BitDepth = depth,
                PixelFormat = pixelFormat,
                Transfer = transfer
            }
        };

        Assert.Throws<InvalidDataException>(() => ExportMuxer.ValidateCompletedVideo(
            CreateRequest(), encoded, CreateVideoStream(1920, 1082)));
    }

    /// <summary>新版完成回执完整往返尺寸和各边补边。</summary>
    [Fact]
    public void WorkerReceiptSerializesOutputGeometry()
    {
        var encoded = CreateEncodedResult(new(1922, 1082, PaddingBottom: 2, PaddingRight: 2));
        var message = new ExportWorkerMessage("complete", encoded.Frames, Encoder: encoded.Encoder,
            Decoder: encoded.Decoder, OutputColor: encoded.OutputColor, RateControl: encoded.RateControl,
            Operation: ExportWorkerOperation.BURN_IN, OutputGeometry: encoded.OutputGeometry);

        var serialized = JsonSerializer.Serialize(message, ExportWire.Options);
        var restored = JsonSerializer.Deserialize<ExportWorkerMessage>(serialized, ExportWire.Options)!;
        using var json = JsonDocument.Parse(serialized);

        Assert.Equal(7, ExportWire.VERSION);
        Assert.Equal(message, restored);
        Assert.Equal(1082, json.RootElement.GetProperty("outputGeometry").GetProperty("height").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("outputGeometry").GetProperty("paddingBottom").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("outputGeometry").GetProperty("paddingRight").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("outputGeometry").GetProperty("paddingTop").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("outputGeometry").GetProperty("paddingLeft").GetInt32());
    }

    /// <summary>新增尺寸属性保持旧构造器和六字段解构的源码兼容。</summary>
    [Fact]
    public void ExportResultPreservesItsExistingConstructorAndDeconstruction()
    {
        var result = new VideoExportResult("output.mkv", 1, "av1_amf")
        {
            OutputGeometry = new(1920, 1082, PaddingBottom: 2)
        };

        var (outputPath, frames, encoder, decoder, color, rateControl) = result;

        Assert.Equal("output.mkv", outputPath);
        Assert.Equal(1UL, frames);
        Assert.Equal("av1_amf", encoder);
        Assert.Null(decoder);
        Assert.Null(color);
        Assert.Null(rateControl);
        Assert.Equal(1082, result.OutputGeometry.Height);
    }

    private static VideoExportRequest CreateRequest() => new(new ProjectDocument { Width = 1920, Height = 1080 },
        Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "geometry-output.mkv"))
    {
        Codec = VideoCodec.Av1,
        EncodingMode = VideoEncodingMode.HARDWARE,
        DecodeMode = VideoDecodeMode.Software,
        RateControlMode = VideoRateControlMode.CBR
    };

    private static NativeVideoExportResult CreateEncodedResult(VideoExportGeometry geometry) => new(1, "av1_amf",
        new(VideoDecodeMode.Software, VideoDecoderBackend.Software, false, string.Empty, 1, 1),
        new(1, 1, 1, 1, 1, 0, 0, VideoCodec.Av1, 8, "yuv420p", OutputVerification: 3),
        new(VideoRateControlMode.CBR, 8000000, 0), geometry);

    private static MediaStreamInfo CreateVideoStream(int width, int height) => new()
    {
        Index = 0,
        CodecType = "video",
        CodecName = "av1",
        Video = new()
        {
            Width = width,
            Height = height,
            PixelFormat = "yuv420p",
            Color = new() { Range = "tv", Matrix = "bt709", Primaries = "bt709", Transfer = "bt709" }
        }
    };
}
