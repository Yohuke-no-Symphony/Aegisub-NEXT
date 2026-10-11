#include "aeginext_export.h"
#include "color_pipeline.h"
#include "export_performance.h"
#include "export_versions.h"
#include "yuv_frame_pipeline.h"
#include "decode_prefetcher.h"
#include "media_core.h"
#include "color_resolution.h"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <memory>
#include <mutex>
#include <sstream>
#include <stdexcept>
#include <string>
#include <unordered_set>
#include <vector>
extern "C"
{
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
#include <libavutil/hwcontext.h>
#include <libswscale/swscale.h>
}
namespace
{
using aeginext::encode::ColorPipeline;
using aeginext::encode::ExportStage;
using aeginext::encode::YuvFramePipeline;
using aeginext::encode::PreparedOverlay;
using aeginext::encode::DecodePrefetcher;
using aeginext::encode::ExportDecodedFrame;
using aeginext::media::DecoderSession;
using aeginext::media::DecodeMode;
using aeginext::media::DecodeWorkload;
constexpr uint32_t EXPORT_ABI_VERSION = 5;
constexpr int32_t RATE_CONTROL_CRF = 1;
constexpr int32_t RATE_CONTROL_VBR = 2;
constexpr int32_t RATE_CONTROL_CBR = 3;
constexpr int64_t BITRATE_PRECISION = 1000;
constexpr int64_t VBR_PEAK_MULTIPLIER = 2;
constexpr int64_t VBV_DURATION_SECONDS = 2;
struct Failure : std::runtime_error
{
    int code;
    Failure(int c, const std::string &message) : std::runtime_error(message), code(c) {}
};
void Check(int result, const char *operation)
{
    if (result < 0)
    {
        char text[AV_ERROR_MAX_STRING_SIZE]{};
        av_strerror(result, text, sizeof(text));
        throw Failure(3, std::string(operation) + ": " + text);
    }
}
void Need(bool valid, const char *message)
{
    if (!valid) throw Failure(2, message);
}
struct FrameDeleter { void operator()(AVFrame *p) const { av_frame_free(&p); } };
using Frame = std::unique_ptr<AVFrame, FrameDeleter>;
Frame Allocate(AVPixelFormat format, int width, int height)
{
    Frame f(av_frame_alloc());
    if (!f) throw std::bad_alloc();
    f->format = format; f->width = width; f->height = height;
    Check(av_frame_get_buffer(f.get(), 32), "frame buffer");
    return f;
}
struct Context
{
    std::atomic<bool> cancelled{false};
    bool started = false;
    AVFormatContext *output = nullptr;
    AVCodecContext *encoder = nullptr;
    AVPacket *encoded = nullptr;
    std::unique_ptr<DecoderSession> decoderSession;
    std::mutex decoderMutex;
    std::mutex prefetchMutex;
    std::weak_ptr<DecodePrefetcher> prefetcher;
    std::string encoderName;
    an_export_result_info resultInfo{};
    bool completed = false;
    aeginext::encode::ExportPerformance performance;
    ~Context()
    {
        av_packet_free(&encoded);
        avcodec_free_context(&encoder);
        if (output)
        {
            if (output->pb) avio_closep(&output->pb);
            avformat_free_context(output);
        }
    }
    void CheckCancel() const { if (cancelled.load()) throw Failure(4, "Export cancelled"); }
};
std::mutex registryMutex;
std::unordered_set<Context *> registry;
Context *Get(void *handle)
{
    std::scoped_lock lock(registryMutex);
    auto *context = static_cast<Context *>(handle);
    if (!registry.contains(context)) throw Failure(1, "Invalid export context");
    return context;
}
int Interrupt(void *opaque) { return static_cast<Context *>(opaque)->cancelled.load() ? 1 : 0; }
void CopyError(char *output, uint32_t capacity, const char *text)
{
    if (!output || !capacity) return;
    const auto count = std::min<size_t>(capacity - 1, std::strlen(text));
    std::memcpy(output, text, count); output[count] = 0;
}
void Versions()
{
    static_assert(LIBAVFORMAT_VERSION_INT == AN_EXPORT_AVFORMAT && LIBAVCODEC_VERSION_INT == AN_EXPORT_AVCODEC &&
        LIBAVUTIL_VERSION_INT == AN_EXPORT_AVUTIL && LIBSWSCALE_VERSION_INT == AN_EXPORT_SWSCALE);
    Need(avformat_version() == AN_EXPORT_AVFORMAT && avcodec_version() == AN_EXPORT_AVCODEC &&
        avutil_version() == AN_EXPORT_AVUTIL && swscale_version() == AN_EXPORT_SWSCALE, "FFmpeg runtime does not match pinned export headers");
}
void ValidateFrame(const AVFrame *f)
{
    const auto *desc = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(f->format));
    Need(desc && desc->nb_components == ((desc->flags & AV_PIX_FMT_FLAG_ALPHA) ? 4 : 3) &&
        !(desc->flags & (AV_PIX_FMT_FLAG_RGB | AV_PIX_FMT_FLAG_FLOAT | AV_PIX_FMT_FLAG_HWACCEL |
        AV_PIX_FMT_FLAG_PAL | AV_PIX_FMT_FLAG_BITSTREAM | AV_PIX_FMT_FLAG_BAYER | AV_PIX_FMT_FLAG_XYZ)),
        "Export requires integer YUV with optional alpha");
    for (int i = 0; i < desc->nb_components; ++i) Need(desc->comp[i].depth >= 8 && desc->comp[i].depth <= 16, "Unsupported input depth");
    Need(f->width > 0 && f->height > 0 && static_cast<uint64_t>(f->width) * f->height <= 33177600,
        "Export input exceeds coded pixel limit");
    Need(!(f->flags & (AV_FRAME_FLAG_INTERLACED | AV_FRAME_FLAG_CORRUPT)) && !f->decode_error_flags,
        "Interlaced or corrupt export frame is unsupported");
    Need(f->color_range == AVCOL_RANGE_MPEG || f->color_range == AVCOL_RANGE_JPEG, "Source range must be explicit");
    Need(std::strncmp(desc->name, "yuvj", 4) != 0 || f->color_range == AVCOL_RANGE_JPEG, "YUVJ format conflicts with limited range");
    Need(!(desc->log2_chroma_w || desc->log2_chroma_h) || f->chroma_location != AVCHROMA_LOC_UNSPECIFIED,
        "Subsampled export requires explicit chroma location");
    Need(f->sample_aspect_ratio.num == 0 || (f->sample_aspect_ratio.den > 0 && f->sample_aspect_ratio.num == f->sample_aspect_ratio.den),
        "First export version requires square source pixels; anamorphic subtitle geometry needs an explicit project policy");
    Need(f->crop_left < static_cast<size_t>(f->width) && f->crop_right < static_cast<size_t>(f->width) - f->crop_left &&
        f->crop_top < static_cast<size_t>(f->height) && f->crop_bottom < static_cast<size_t>(f->height) - f->crop_top, "Empty frame crop");
    for (int i = 0; i < f->nb_side_data; ++i)
    {
        switch (f->side_data[i]->type)
        {
        case AV_FRAME_DATA_DISPLAYMATRIX: case AV_FRAME_DATA_STEREO3D: case AV_FRAME_DATA_DYNAMIC_HDR_PLUS:
        case AV_FRAME_DATA_DOVI_RPU_BUFFER: case AV_FRAME_DATA_DOVI_METADATA: case AV_FRAME_DATA_DYNAMIC_HDR_VIVID:
        case AV_FRAME_DATA_DYNAMIC_HDR_SMPTE_2094_APP5: case AV_FRAME_DATA_ICC_PROFILE: case AV_FRAME_DATA_RAW_COLOR_PARAMS:
        case AV_FRAME_DATA_FILM_GRAIN_PARAMS: case AV_FRAME_DATA_AMBIENT_VIEWING_ENVIRONMENT:
            throw Failure(2, "Display transform, dynamic HDR, ICC or auxiliary color metadata requires an explicit export policy");
        default: break;
        }
    }
}
void WritePackets(Context &c, AVStream *stream)
{
    while (true)
    {
        c.CheckCancel();
        const auto result = c.performance.Measure(ExportStage::ReceivePacket, [&]() { return avcodec_receive_packet(c.encoder, c.encoded); });
        if (result == AVERROR(EAGAIN) || result == AVERROR_EOF) return;
        Check(result, "receive encoded packet");
        av_packet_rescale_ts(c.encoded, c.encoder->time_base, stream->time_base);
        c.encoded->stream_index = stream->index;
        Check(c.performance.Measure(ExportStage::Mux, [&]() { return av_interleaved_write_frame(c.output, c.encoded); }), "mux encoded packet");
    }
}
std::string MasteringOption(const AVFrame *f)
{
    const auto *side = av_frame_get_side_data(f, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA);
    if (!side) return {};
    Need(side->size >= sizeof(AVMasteringDisplayMetadata), "Truncated mastering metadata");
    const auto *m = reinterpret_cast<const AVMasteringDisplayMetadata *>(side->data);
    Need(m->has_primaries && m->has_luminance, "Partial mastering display metadata cannot be faithfully emitted by this encoder");
    auto q = [](AVRational value, int scale)
    {
        Need(value.den > 0 && value.num >= 0, "Invalid mastering rational");
        return std::llround(av_q2d(value) * scale);
    };
    Need(av_q2d(m->max_luminance) > av_q2d(m->min_luminance) && av_q2d(m->max_luminance) <= 10000,
        "Invalid mastering display luminance");
    for (int p = 0; p < 3; ++p)
        for (int axis = 0; axis < 2; ++axis)
            Need(m->display_primaries[p][axis].den > 0 && m->display_primaries[p][axis].num >= 0 &&
                av_q2d(m->display_primaries[p][axis]) <= 1, "Invalid mastering primary coordinate");
    for (int axis = 0; axis < 2; ++axis)
        Need(m->white_point[axis].den > 0 && m->white_point[axis].num >= 0 && av_q2d(m->white_point[axis]) <= 1,
            "Invalid mastering white coordinate");
    std::ostringstream s;
    for (const int p : {1, 2, 0}) s << "GBR"[p == 1 ? 0 : p == 2 ? 1 : 2] << '(' << q(m->display_primaries[p][0], 50000) << ',' << q(m->display_primaries[p][1], 50000) << ')';
    s << "WP(" << q(m->white_point[0], 50000) << ',' << q(m->white_point[1], 50000) << ")L(" << q(m->max_luminance, 10000) << ',' << q(m->min_luminance, 10000) << ')';
    return s.str();
}
struct EncoderDeleter
{
    void operator()(AVCodecContext *value) const { avcodec_free_context(&value); }
};
using EncoderContext = std::unique_ptr<AVCodecContext, EncoderDeleter>;
int64_t TargetBitrate(const an_export_request &request)
{
    return request.video_bitrate / BITRATE_PRECISION * BITRATE_PRECISION;
}
int64_t PeakBitrate(const an_export_request &request)
{
    const auto target = TargetBitrate(request);
    return request.rate_control_mode == RATE_CONTROL_CBR ? target : target * VBR_PEAK_MULTIPLIER;
}
void ConfigureRateControl(AVCodecContext &encoder, const an_export_request &request)
{
    if (request.rate_control_mode == RATE_CONTROL_CRF)
    {
        encoder.bit_rate = 0;
        encoder.rc_min_rate = 0;
        encoder.rc_max_rate = 0;
        encoder.rc_buffer_size = 0;
        return;
    }

    encoder.bit_rate = TargetBitrate(request);
    encoder.rc_min_rate = request.rate_control_mode == RATE_CONTROL_CBR ? encoder.bit_rate : 0;
    encoder.rc_max_rate = PeakBitrate(request);
    encoder.rc_buffer_size = static_cast<int>(encoder.rc_max_rate * VBV_DURATION_SECONDS);
    encoder.global_quality = 0;
    encoder.flags &= ~AV_CODEC_FLAG_QSCALE;
}
bool MatchesIntegerOption(AVCodecContext &encoder, const char *name, int expected)
{
    int64_t actual = 0;
    return av_opt_get_int(encoder.priv_data, name, 0, &actual) >= 0 && actual == expected;
}
bool MatchesNamedOption(AVCodecContext &encoder, const char *name, const char *expected)
{
    const auto *option = av_opt_find(encoder.priv_data, name, nullptr, 0, 0);
    int expectedValue = 0;
    return option && av_opt_eval_int(encoder.priv_data, option, expected, &expectedValue) >= 0 &&
        MatchesIntegerOption(encoder, name, expectedValue);
}
void ConfirmRateControl(Context &context, const an_export_request &request, AVCodecContext &encoder)
{
    const auto *name = encoder.codec->name;
    if (request.rate_control_mode == RATE_CONTROL_CRF)
    {
        double quality = -1;
        Need(request.encoding_mode == 0 && av_opt_get_double(encoder.priv_data, "crf", 0, &quality) >= 0 &&
            quality == request.crf, "Software encoder did not confirm the selected CRF quality");
        context.resultInfo.rate_control_mode = RATE_CONTROL_CRF;
        context.resultInfo.video_bitrate = 0;
        context.resultInfo.crf = static_cast<int32_t>(quality);
        return;
    }

    const auto target = TargetBitrate(request);
    const auto peak = PeakBitrate(request);
    Need(encoder.bit_rate == target && encoder.rc_max_rate == peak &&
        encoder.rc_min_rate == (request.rate_control_mode == RATE_CONTROL_CBR ? target : 0) &&
        encoder.rc_buffer_size == peak * VBV_DURATION_SECONDS,
        "Encoder did not preserve the selected target bitrate or VBV configuration");
    if (request.encoding_mode == 0)
    {
        double quality = 0;
        Need(av_opt_get_double(encoder.priv_data, "crf", 0, &quality) >= 0 && quality < 0,
            "Software encoder retained CRF instead of the selected bitrate mode");
    }
    else if (std::strstr(name, "videotoolbox"))
    {
        Need(MatchesIntegerOption(encoder, "constant_bit_rate", request.rate_control_mode == RATE_CONTROL_CBR ? 1 : 0) &&
            MatchesIntegerOption(encoder, "allow_sw", 0) && MatchesIntegerOption(encoder, "require_sw", 0),
            "VideoToolbox did not confirm the selected hardware rate-control configuration");
    }
    else if (std::strstr(name, "nvenc"))
    {
        Need(MatchesNamedOption(encoder, "rc", request.rate_control_mode == RATE_CONTROL_CBR ? "cbr" : "vbr"),
            "NVENC did not confirm the selected rate-control configuration");
    }
    else if (std::strstr(name, "amf"))
    {
        Need(MatchesNamedOption(encoder, "rc", request.rate_control_mode == RATE_CONTROL_CBR ? "cbr" : "vbr_peak"),
            "AMF did not confirm the selected rate-control configuration");
    }

    context.resultInfo.rate_control_mode = request.rate_control_mode;
    context.resultInfo.video_bitrate = static_cast<int32_t>(encoder.bit_rate);
    context.resultInfo.crf = 0;
}
EncoderContext ConfigureEncoder(const AVCodec *encoder, const an_export_request &r, AVStream *source,
    const AVFrame *frame, const AVFormatContext *output, AVPixelFormat format)
{
    EncoderContext value(avcodec_alloc_context3(encoder));
    if (!value) throw std::bad_alloc();
    value->width = r.width; value->height = r.height; value->pix_fmt = format;
    value->time_base = source->time_base; value->sample_aspect_ratio = frame->sample_aspect_ratio;
    value->color_range = frame->color_range; value->colorspace = frame->colorspace;
    value->color_primaries = frame->color_primaries; value->color_trc = frame->color_trc;
    value->chroma_sample_location = AVCHROMA_LOC_LEFT;
    value->thread_count = 4; value->flags |= AV_CODEC_FLAG_FRAME_DURATION;
    ConfigureRateControl(*value, r);
    if (output->oformat->flags & AVFMT_GLOBALHEADER) value->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
    return value;
}
const char *HardwareSpeed(const char *preset)
{
    if (!std::strcmp(preset, "slow") || !std::strcmp(preset, "slower") || !std::strcmp(preset, "veryslow")) return "slow";
    if (!std::strcmp(preset, "medium")) return "medium";
    return "fast";
}
bool SupportsPixelFormat(const AVCodec *encoder, AVPixelFormat format)
{
    const void *values = nullptr;
    int count = 0;
    if (avcodec_get_supported_config(nullptr, encoder, AV_CODEC_CONFIG_PIX_FORMAT, 0, &values, &count) < 0) return false;
    if (!values) return false;
    const auto *formats = static_cast<const AVPixelFormat *>(values);
    return std::find(formats, formats + count, format) != formats + count;
}
AVCodecContext *OpenHardwareEncoder(Context &c, const an_export_request &r, AVStream *source,
    const AVFrame *frame, int codec)
{
    const bool hdr = frame->color_trc == AVCOL_TRC_SMPTE2084 || frame->color_trc == AVCOL_TRC_ARIB_STD_B67;
    Need(!hdr && !av_frame_get_side_data(frame, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA) &&
        !av_frame_get_side_data(frame, AV_FRAME_DATA_CONTENT_LIGHT_LEVEL),
        "GPU encoding cannot yet preserve the verified HDR metadata contract; select CPU software encoding for HDR export");
    const auto format = codec == 1 ? AV_PIX_FMT_NV12 : AV_PIX_FMT_P010LE;
    std::vector<const char *> candidates;
#if defined(__APPLE__)
    candidates = codec == 1 ? std::vector<const char *>{"h264_videotoolbox"} : std::vector<const char *>{"hevc_videotoolbox"};
#elif defined(_WIN32)
    candidates = codec == 1 ? std::vector<const char *>{"h264_nvenc", "h264_qsv", "h264_amf"} :
        std::vector<const char *>{"hevc_nvenc", "hevc_qsv", "hevc_amf"};
#endif
    std::ostringstream failures;
    for (const auto *name : candidates)
    {
        c.CheckCancel();
        const auto *encoder = avcodec_find_encoder_by_name(name);
        if (!encoder)
        {
            failures << name << ": not compiled into this FFmpeg runtime; ";
            continue;
        }
        if (!SupportsPixelFormat(encoder, format))
        {
            failures << name << ": required " << av_get_pix_fmt_name(format) << " format unsupported; ";
            continue;
        }
        auto value = ConfigureEncoder(encoder, r, source, frame, c.output, format);
        value->framerate = source->avg_frame_rate.num > 0 ? source->avg_frame_rate : source->r_frame_rate;
        AVDictionary *options = nullptr;
        const auto speed = std::string(HardwareSpeed(r.preset));
        if (std::strstr(name, "videotoolbox"))
        {
            av_dict_set(&options, "allow_sw", "0", 0);
            av_dict_set(&options, "require_sw", "0", 0);
            av_dict_set(&options, "constant_bit_rate", r.rate_control_mode == RATE_CONTROL_CBR ? "1" : "0", 0);
            av_dict_set(&options, "prio_speed", speed == "fast" ? "1" : "0", 0);
            if (codec == 2) av_dict_set(&options, "profile", "main10", 0);
        }
        else if (std::strstr(name, "nvenc"))
        {
            av_dict_set(&options, "preset", speed == "fast" ? "p3" : speed == "slow" ? "p5" : "p4", 0);
            av_dict_set(&options, "rc", r.rate_control_mode == RATE_CONTROL_CBR ? "cbr" : "vbr", 0);
            if (codec == 2) av_dict_set(&options, "profile", "main10", 0);
        }
        else if (std::strstr(name, "qsv"))
        {
            AVDictionary *deviceOptions = nullptr;
            av_dict_set(&deviceOptions, "child_device_type", "d3d11va", 0);
            const auto device = av_hwdevice_ctx_create(&value->hw_device_ctx, AV_HWDEVICE_TYPE_QSV,
                "hw_any", deviceOptions, 0);
            av_dict_free(&deviceOptions);
            if (device < 0)
            {
                char reason[AV_ERROR_MAX_STRING_SIZE]{};
                av_strerror(device, reason, sizeof(reason));
                failures << name << ": hardware device initialization failed (" << reason << "); ";
                av_dict_free(&options);
                continue;
            }
            av_dict_set(&options, "preset", speed.c_str(), 0);
            av_dict_set(&options, "look_ahead", "0", 0);
            av_dict_set(&options, "vcm", "0", 0);
        }
        else if (std::strstr(name, "amf"))
        {
            av_dict_set(&options, "quality", speed == "fast" ? "speed" : speed == "slow" ? "quality" : "balanced", 0);
            av_dict_set(&options, "rc", r.rate_control_mode == RATE_CONTROL_CBR ? "cbr" : "vbr_peak", 0);
        }
        const auto opened = avcodec_open2(value.get(), encoder, &options);
        const bool unusedOptions = av_dict_count(options) != 0;
        av_dict_free(&options);
        if (opened < 0 || unusedOptions)
        {
            char reason[AV_ERROR_MAX_STRING_SIZE]{};
            av_strerror(opened, reason, sizeof(reason));
            failures << name << ": initialization failed (" << (unusedOptions ? "unsupported encoder options" : reason) << "); ";
            continue;
        }
        try
        {
            ConfirmRateControl(c, r, *value);
        }
        catch (const Failure &error)
        {
            failures << name << ": " << error.what() << "; ";
            continue;
        }
        c.encoderName = name;
        return value.release();
    }
    throw Failure(2, "GPU hardware video encoder unavailable; no CPU fallback. " +
        (candidates.empty() ? std::string("No supported hardware backend on this platform") : failures.str()));
}
void Execute(Context &c, const an_export_request &r, an_export_render_callback render, void *user, uint64_t &frames)
{
    Versions(); c.CheckCancel();
    if (c.started) throw Failure(1, "Export context may run only once");
    c.started = true;
    Need(!std::filesystem::exists(std::filesystem::path(reinterpret_cast<const char8_t *>(r.output_path))), "Temporary export output already exists");
    Need(std::filesystem::is_regular_file(std::filesystem::path(reinterpret_cast<const char8_t *>(r.input_path))), "Export input must be a local regular file");
    {
        std::scoped_lock lock(c.decoderMutex);
        c.decoderSession = std::make_unique<DecoderSession>(aeginext::media::DecodeOptions{
            static_cast<DecodeMode>(r.decode_mode), DecodeWorkload::Offline});
        if (c.cancelled.load()) c.decoderSession->Cancel();
    }
    c.decoderSession->Open(r.input_path, r.video_stream_index);
    auto *sourceStream = c.decoderSession->SourceStream();
    Need(sourceStream && sourceStream->time_base.num > 0 && sourceStream->time_base.den > 0,
        "Invalid video stream/time base");
    for (int i = 0; i < sourceStream->codecpar->nb_coded_side_data; ++i)
    {
        switch (sourceStream->codecpar->coded_side_data[i].type)
        {
        case AV_PKT_DATA_DISPLAYMATRIX: case AV_PKT_DATA_STEREO3D: case AV_PKT_DATA_ICC_PROFILE:
        case AV_PKT_DATA_DOVI_CONF: case AV_PKT_DATA_DYNAMIC_HDR10_PLUS: case AV_PKT_DATA_DYNAMIC_HDR_SMPTE_2094_APP5:
        case AV_PKT_DATA_AMBIENT_VIEWING_ENVIRONMENT: case AV_PKT_DATA_3D_REFERENCE_DISPLAYS:
            throw Failure(2, "Stream display transform or auxiliary HDR/color metadata requires an explicit export policy");
        default: break;
        }
    }
    c.encoded = av_packet_alloc();
    Frame decoded;
    if (!c.encoded) throw std::bad_alloc();
    Frame outputFrame;
    AVStream *targetStream = nullptr;
    std::unique_ptr<ColorPipeline> color;
    std::unique_ptr<YuvFramePipeline> yuv;
    std::vector<float> layer(static_cast<size_t>(r.width) * r.height * 4);
    PreparedOverlay overlay(r.width, r.height);
    auto streamTimeBase = sourceStream->time_base;
    aeginext::media::DisplayTiming displayTiming;
    int64_t lastPts = AV_NOPTS_VALUE;
    std::string mastering;
    int sourceFormat = -1, sourceWidth = 0, sourceHeight = 0, matrix = -1, primaries = -1, transfer = -1, range = -1, chroma = -1;
    auto process = [&]()
    {
        c.CheckCancel(); ValidateFrame(decoded.get());
        const auto width = decoded->width - decoded->crop_left - decoded->crop_right;
        const auto height = decoded->height - decoded->crop_top - decoded->crop_bottom;
        Need(width == r.width && height == r.height, "Project canvas must equal visible video dimensions");
        Need(displayTiming.value != AV_NOPTS_VALUE && displayTiming.timeBase.num > 0 && displayTiming.timeBase.den > 0,
            "Export requires a presentation time supported by source timing evidence");
        const auto pts = av_rescale_q(displayTiming.value, displayTiming.timeBase, streamTimeBase);
        Need(pts != AV_NOPTS_VALUE && (lastPts == AV_NOPTS_VALUE || pts > lastPts),
            "Export presentation time overflowed or ceased to increase after stream time-base quantization");
        lastPts = pts;
        if (!c.encoder)
        {
            const bool hdr = decoded->color_trc == AVCOL_TRC_SMPTE2084 || decoded->color_trc == AVCOL_TRC_ARIB_STD_B67;
            const auto codec = r.codec == 0 ? (hdr ? 2 : 1) : r.codec;
            Need(!(hdr && codec == 1), "HDR requires HEVC 10-bit output");
            Check(avformat_alloc_output_context2(&c.output, nullptr, "nut", r.output_path), "create export container");
            c.output->avoid_negative_ts = AVFMT_AVOID_NEG_TS_DISABLED;
            c.output->interrupt_callback = {Interrupt, &c};
            if (r.encoding_mode == 1)
            {
                c.encoder = OpenHardwareEncoder(c, r, sourceStream, decoded.get(), codec);
            }
            else
            {
                const auto *encoder = avcodec_find_encoder_by_name(codec == 1 ? "libx264" : "libx265");
                Need(encoder != nullptr, "Requested software encoder is unavailable");
                auto value = ConfigureEncoder(encoder, r, sourceStream, decoded.get(), c.output,
                    codec == 1 ? AV_PIX_FMT_YUV420P : AV_PIX_FMT_YUV420P10LE);
                value->framerate = sourceStream->avg_frame_rate.num > 0 ? sourceStream->avg_frame_rate : sourceStream->r_frame_rate;
                AVDictionary *options = nullptr;
                av_dict_set(&options, "preset", r.preset, 0);
                if (r.rate_control_mode == RATE_CONTROL_CRF)
                {
                    av_dict_set(&options, "crf", std::to_string(r.crf).c_str(), 0);
                }
                else if (codec == 1 && r.rate_control_mode == RATE_CONTROL_CBR)
                {
                    av_dict_set(&options, "x264-params", "filler=1", 0);
                }
                if (codec == 2)
                {
                    std::string params = "pools=none:frame-threads=4:log-level=error:colorprim=" + std::to_string(decoded->color_primaries) + ":transfer=" + std::to_string(decoded->color_trc) + ":colormatrix=" + std::to_string(decoded->colorspace);
                    if (r.rate_control_mode == RATE_CONTROL_CRF && r.crf == 0)
                    {
                        params += ":lossless=1";
                    }
                    else if (r.rate_control_mode == RATE_CONTROL_CBR)
                    {
                        params += ":strict-cbr=1";
                    }
                    if (decoded->color_trc == AVCOL_TRC_SMPTE2084)
                    {
                        mastering = MasteringOption(decoded.get());
                        if (!mastering.empty()) params += ":master-display=" + mastering;
                    }
                    av_dict_set(&options, "x265-params", params.c_str(), 0);
                }
                const auto opened = avcodec_open2(value.get(), encoder, &options);
                const auto unusedOptions = av_dict_count(options) != 0;
                av_dict_free(&options);
                Check(opened, "open video encoder");
                Need(!unusedOptions, "Software encoder did not consume the selected encoding options");
                ConfirmRateControl(c, r, *value);
                c.encoderName = encoder->name;
                c.encoder = value.release();
            }
            targetStream = avformat_new_stream(c.output, nullptr);
            if (!targetStream) throw std::bad_alloc();
            targetStream->time_base = sourceStream->time_base;
            targetStream->avg_frame_rate = sourceStream->avg_frame_rate;
            targetStream->r_frame_rate = sourceStream->r_frame_rate;
            Check(avcodec_parameters_from_context(targetStream->codecpar, c.encoder), "output stream parameters");
            Check(avio_open2(&c.output->pb, r.output_path, AVIO_FLAG_WRITE, &c.output->interrupt_callback, nullptr), "open temporary output");
            Check(avformat_write_header(c.output, nullptr), "write container header");
            sourceFormat = decoded->format; sourceWidth = decoded->width; sourceHeight = decoded->height;
            matrix = decoded->colorspace; primaries = decoded->color_primaries; transfer = decoded->color_trc; range = decoded->color_range; chroma = decoded->chroma_location;
            color = std::make_unique<ColorPipeline>(decoded->colorspace, decoded->color_primaries, decoded->color_trc);
            outputFrame = Allocate(c.encoder->pix_fmt, r.width, r.height);
            yuv = std::make_unique<YuvFramePipeline>(decoded.get(), r.width, r.height, c.encoder->pix_fmt);
        }
        Need(sourceFormat == decoded->format && sourceWidth == decoded->width && sourceHeight == decoded->height &&
            matrix == decoded->colorspace && primaries == decoded->color_primaries && transfer == decoded->color_trc && range == decoded->color_range && chroma == decoded->chroma_location,
            "Midstream video format/color changes require a new export segment");
        if (decoded->color_trc == AVCOL_TRC_SMPTE2084)
            Need(MasteringOption(decoded.get()) == mastering, "Midstream mastering metadata changes require an explicit export policy");
        an_export_overlay_info overlayInfo{sizeof(overlayInfo), EXPORT_ABI_VERSION, 0, 0, 0};
        try
        {
            const auto result = c.performance.Measure(ExportStage::Render, [&]()
            {
                return render(user, pts, streamTimeBase.num, streamTimeBase.den,
                    r.width, r.height, layer.data(), layer.size(), &overlayInfo);
            });
            if (result != 0) throw Failure(result == 1 ? 4 : 3, result == 1 ? "Export cancelled" : "Project renderer failed");
            c.CheckCancel();
            c.performance.Measure(ExportStage::OverlayCoverage, [&]()
            {
                overlay.Accept(overlayInfo, layer, *color, r.reference_white_nits, [&]() { c.CheckCancel(); });
                return 0;
            });
            c.performance.RecordOverlay(overlayInfo.state, overlay);
        }
        catch (...)
        {
            overlay.Invalidate();
            throw;
        }
        Check(c.performance.Measure(ExportStage::Upsample, [&]()
        {
            return yuv->Upsample(decoded.get(), [&]() { c.CheckCancel(); });
        }), "upsample encoded YUV");
        c.performance.Measure(ExportStage::Composite, [&]()
        {
            yuv->Composite(decoded.get(), overlay, *color, r.reference_white_nits, [&]() { c.CheckCancel(); });
            return 0;
        });
        Check(c.performance.Measure(ExportStage::WritableFrame, [&]() { return av_frame_make_writable(outputFrame.get()); }), "writable encode frame");
        Check(c.performance.Measure(ExportStage::Downsample, [&]()
        {
            return yuv->Downsample(outputFrame.get());
        }), "downsample encoded YUV");
        outputFrame->pts = pts;
        const auto durationTimeBase = decoded->time_base.num > 0 && decoded->time_base.den > 0 ?
            decoded->time_base : streamTimeBase;
        outputFrame->duration = av_rescale_q(decoded->duration, durationTimeBase, streamTimeBase);
        Need(outputFrame->duration >= 0, "Export duration overflowed or is negative");
        outputFrame->time_base = streamTimeBase;
        outputFrame->color_range = decoded->color_range; outputFrame->colorspace = decoded->colorspace;
        outputFrame->color_primaries = decoded->color_primaries; outputFrame->color_trc = decoded->color_trc;
        outputFrame->chroma_location = AVCHROMA_LOC_LEFT; outputFrame->sample_aspect_ratio = decoded->sample_aspect_ratio;
        Check(c.performance.Measure(ExportStage::SendFrame, [&]() { return avcodec_send_frame(c.encoder, outputFrame.get()); }), "send composited frame");
        WritePackets(c, targetStream); ++frames;
    };
    const auto readFrame = [&]() -> ExportDecodedFrame
    {
        c.CheckCancel();
        auto raw = c.performance.Measure(ExportStage::Decode, [&]() { return c.decoderSession->ReadFrame(); });
        if (!raw) return {};
        const auto timing = c.decoderSession->OutputDisplayTiming();
        const auto *stream = c.decoderSession->SourceStream();
        Need(stream && stream->time_base.num > 0 && stream->time_base.den > 0,
            "Invalid video stream/time base after decoder selection");
        const auto resolved = aeginext::media::ResolveColor(raw.get(), c.decoderSession->ColorContext());
        aeginext::media::FramePointer frame(av_frame_clone(raw.get()));
        if (!frame) throw std::bad_alloc();
        aeginext::media::ApplyColor(frame.get(), resolved);
        return {std::move(frame), stream->time_base, resolved.inferredFields, timing};
    };
    auto first = readFrame();
    if (first.frame)
    {
        sourceStream = c.decoderSession->SourceStream();
        streamTimeBase = first.timeBase;
        decoded.reset(first.frame.release());
        displayTiming = first.displayTiming;
        process();
        c.resultInfo.inferred_fields |= first.inferredFields;
    }
    std::shared_ptr<DecodePrefetcher> prefetch;
    const auto *prefetchOption = std::getenv("AEGINEXT_EXPORT_PREFETCH");
    const auto hardwareConfirmed = c.decoderSession->Info().hardwareConfirmed;
    const auto activeBackend = c.decoderSession->Info().activeBackend;
    const auto prefetchEnabled = prefetchOption ? std::strcmp(prefetchOption, "1") == 0 :
        hardwareConfirmed && activeBackend == aeginext::media::DecoderBackend::VideoToolbox;
    if (frames && prefetchEnabled)
    {
        prefetch = std::make_shared<DecodePrefetcher>(readFrame, [&]() noexcept
        {
            std::scoped_lock lock(c.decoderMutex);
            if (c.decoderSession) c.decoderSession->Cancel();
        });
        std::scoped_lock lock(c.prefetchMutex);
        c.prefetcher = prefetch;
    }
    try
    {
        while (frames)
        {
            c.CheckCancel();
            auto item = prefetch ? c.performance.Measure(ExportStage::PrefetchWait, [&]() { return prefetch->Next(); }) : readFrame();
            if (!item.frame) break;
            Need(item.timeBase.num == streamTimeBase.num && item.timeBase.den == streamTimeBase.den,
                "Midstream video time base changes require a new export segment");
            decoded.reset(item.frame.release());
            displayTiming = item.displayTiming;
            process();
            c.resultInfo.inferred_fields |= item.inferredFields;
        }
        if (prefetch)
        {
            prefetch->Stop();
            c.performance.RecordPrefetch(prefetch->Statistics());
        }
    }
    catch (...)
    {
        if (prefetch) prefetch->Stop();
        throw;
    }
    Need(c.encoder != nullptr && frames > 0, "No decoded video frames");
    Check(c.performance.Measure(ExportStage::SendFrame, [&]() { return avcodec_send_frame(c.encoder, nullptr); }), "drain encoder"); WritePackets(c, targetStream);
    Check(av_write_trailer(c.output), "write output trailer");
    Check(avio_closep(&c.output->pb), "close output file");
    const auto &session = c.decoderSession->Info();
    c.resultInfo.struct_size = sizeof(an_export_result_info);
    c.resultInfo.abi_version = EXPORT_ABI_VERSION;
    c.resultInfo.core_version = aeginext::media::CORE_VERSION;
    c.resultInfo.capabilities = aeginext::media::CAPABILITIES;
    c.resultInfo.requested_decode_mode = static_cast<uint32_t>(session.requestedMode);
    c.resultInfo.active_decode_backend = static_cast<uint32_t>(session.activeBackend);
    c.resultInfo.hardware_confirmed = session.hardwareConfirmed ? 1 : 0;
    c.resultInfo.generation = session.generation;
    c.resultInfo.delivered_frames = session.deliveredFrames;
    c.resultInfo.color_range = range;
    c.resultInfo.color_matrix = matrix;
    c.resultInfo.color_primaries = primaries;
    c.resultInfo.color_transfer = transfer;
    c.resultInfo.chroma_location = AVCHROMA_LOC_LEFT;
    c.resultInfo.alpha_mode = AVALPHA_MODE_UNSPECIFIED;
    CopyError(c.resultInfo.fallback_reason, sizeof(c.resultInfo.fallback_reason), session.fallbackReason.c_str());
    c.completed = true;
    c.performance.Report(frames, session.decodeNanoseconds, session.downloadNanoseconds);
}
}
extern "C"
{
uint32_t AN_EXPORT_CALL an_export_abi_version(void) { return EXPORT_ABI_VERSION; }
uint32_t AN_EXPORT_CALL an_export_core_version(void) { return aeginext::media::CORE_VERSION; }
uint32_t AN_EXPORT_CALL an_export_capabilities(void) { return aeginext::media::CAPABILITIES; }
int32_t AN_EXPORT_CALL an_export_get_result_info(void *context, an_export_result_info *info,
    char *error, uint32_t capacity)
{
    try
    {
        if (!info || info->struct_size != sizeof(*info) || info->abi_version != EXPORT_ABI_VERSION || info->rate_control_reserved)
            throw Failure(1, "Invalid export result information ABI");
        const auto *value = Get(context);
        if (!value->completed) throw Failure(1, "Export result is available only after successful completion");
        *info = value->resultInfo;
        CopyError(error, capacity, "");
        return 0;
    }
    catch (const Failure &e) { CopyError(error, capacity, e.what()); return e.code; }
    catch (const std::exception &e) { CopyError(error, capacity, e.what()); return 3; }
}
const char *AN_EXPORT_CALL an_export_encoder_name(void *context)
{
    try { return Get(context)->encoderName.c_str(); }
    catch (...) { return ""; }
}
int32_t AN_EXPORT_CALL an_export_create(void **context, char *error, uint32_t capacity)
{
    try
    {
        if (!context) throw Failure(1, "Missing context output");
        *context = nullptr; Versions(); auto result = std::make_unique<Context>();
        { std::scoped_lock lock(registryMutex); registry.insert(result.get()); }
        *context = result.release(); CopyError(error, capacity, ""); return 0;
    }
    catch (const Failure &e) { CopyError(error, capacity, e.what()); return e.code; }
    catch (const std::exception &e) { CopyError(error, capacity, e.what()); return 3; }
    catch (...) { CopyError(error, capacity, "Unknown native failure"); return 3; }
}
void AN_EXPORT_CALL an_export_cancel(void *context)
{
    try
    {
        auto *value = Get(context);
        value->cancelled.store(true);
        {
            std::scoped_lock lock(value->decoderMutex);
            if (value->decoderSession) value->decoderSession->Cancel();
        }
        std::shared_ptr<DecodePrefetcher> prefetch;
        {
            std::scoped_lock lock(value->prefetchMutex);
            prefetch = value->prefetcher.lock();
        }
        if (prefetch) prefetch->RequestStop();
    }
    catch (...) {}
}
void AN_EXPORT_CALL an_export_destroy(void *context)
{
    try
    {
        auto *value = static_cast<Context *>(context);
        { std::scoped_lock lock(registryMutex); if (!registry.erase(value)) return; }
        delete value;
    }
    catch (...) {}
}
int32_t AN_EXPORT_CALL an_export_run(void *context, const an_export_request *request,
    an_export_render_callback render, void *user, uint64_t *frames, char *error, uint32_t capacity)
{
    try
    {
        if (!request || request->struct_size != sizeof(*request) || request->abi_version != EXPORT_ABI_VERSION || !render || !frames ||
            !request->input_path || !request->output_path || !request->preset || request->flags || request->reserved || request->decode_reserved || request->decode_mode > 2 ||
            request->width == 0 || request->height == 0 || (request->width % 2) || (request->height % 2) ||
            static_cast<uint64_t>(request->width)*request->height > 33177600 || request->codec < 0 || request->codec > 2 ||
            !std::isfinite(request->reference_white_nits) || request->reference_white_nits <= 0)
            throw Failure(1, "Invalid export request ABI, dimensions, codec or reference white");
        if (request->encoding_mode < 0 || request->encoding_mode > 1 || request->rate_control_reserved ||
            (request->rate_control_mode != RATE_CONTROL_CRF && request->rate_control_mode != RATE_CONTROL_VBR &&
                request->rate_control_mode != RATE_CONTROL_CBR) ||
            (request->rate_control_mode == RATE_CONTROL_CRF &&
                (request->encoding_mode != 0 || request->crf < 0 || request->crf > 51)) ||
            (request->rate_control_mode != RATE_CONTROL_CRF &&
                (request->video_bitrate < 100000 || request->video_bitrate > 200000000)))
        {
            throw Failure(1, "Invalid video encoding mode, rate-control mode or active quality parameter");
        }
        *frames = 0; auto *value = Get(context); Execute(*value, *request, render, user, *frames);
        CopyError(error, capacity, ""); return 0;
    }
    catch (const Failure &e) { CopyError(error, capacity, e.what()); return e.code; }
    catch (const aeginext::media::CoreError &e)
    {
        CopyError(error, capacity, e.what());
        if (e.Code() == aeginext::media::ErrorCode::Cancelled) return 4;
        if (e.Code() == aeginext::media::ErrorCode::Unsupported) return 2;
        if (e.Code() == aeginext::media::ErrorCode::InvalidArgument) return 1;
        return 3;
    }
    catch (const std::exception &e) { CopyError(error, capacity, e.what()); return 3; }
    catch (...) { CopyError(error, capacity, "Unknown native export failure"); return 3; }
}
}
