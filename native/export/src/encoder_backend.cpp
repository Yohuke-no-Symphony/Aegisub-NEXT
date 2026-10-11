#include "encoder_backend.h"
#include "export_error.h"
#include "qsv_backend.h"
#include <algorithm>
#include <array>
#include <cstring>
#include <sstream>
#include <vector>
extern "C"
{
#include <libavutil/opt.h>
#include <libavutil/pixdesc.h>
}
namespace aeginext::encode
{
namespace
{
constexpr const char *PRESETS[]{"ultrafast","superfast","veryfast","faster","fast","medium","slow","slower","veryslow"};
int PresetIndex(const char *preset)
{
    if (preset) for (int i = 0; i < 9; ++i) if (!std::strcmp(preset, PRESETS[i])) return i;
    throw ExportFailure(1, "Unsupported video speed preset");
}
struct Dictionary
{
    AVDictionary *value = nullptr;
    ~Dictionary() { av_dict_free(&value); }
    void Set(const char *key, const std::string &text) { CheckAv(av_dict_set(&value, key, text.c_str(), 0), "set encoder option"); }
};
bool Supports(const AVCodec *codec, AVPixelFormat format)
{
    const void *values = nullptr; int count = 0;
    if (avcodec_get_supported_config(nullptr, codec, AV_CODEC_CONFIG_PIX_FORMAT, 0, &values, &count) < 0 || !values) return false;
    const auto *formats = static_cast<const AVPixelFormat *>(values);
    return std::find(formats, formats + count, format) != formats + count;
}
void Optional(AVCodecContext *encoder, Dictionary &options, const char *key, const char *value)
{
    if (av_opt_find(encoder->priv_data, key, nullptr, 0, 0)) options.Set(key, value);
}
void ConfigureRate(AVCodecContext *encoder, const an_export_request &request, bool svt)
{
    encoder->bit_rate = request.rate_control_mode == 1 ? 0 : request.video_bitrate / 1000 * 1000;
    if (request.rate_control_mode == 1 || request.rate_control_mode == 4) return;
    encoder->rc_min_rate = request.rate_control_mode == 3 ? encoder->bit_rate : 0;
    encoder->rc_max_rate = svt ? 0 : encoder->bit_rate * (request.rate_control_mode == 3 ? 1 : 2);
    encoder->rc_buffer_size = static_cast<int>((svt ? encoder->bit_rate : encoder->rc_max_rate) * 2);
}
void ConfigureOptions(EncoderBackend &backend, const an_export_request &request, int codec,
    const ExportMetadata &metadata, Dictionary &options)
{
    const auto *name = backend.context->codec->name;
    const int speed = PresetIndex(request.preset);
    if (request.encoding_mode == 0)
    {
        if (codec == 3 || codec == 4)
        {
            options.Set("deadline", "good"); options.Set("cpu-used", std::to_string(SoftwareSpeed(codec, request.preset)));
            backend.context->qmin = 0; backend.context->qmax = 63;
            if (codec == 4) { options.Set("row-mt", "1"); if (request.rate_control_mode == 1 && request.quality == 0) options.Set("lossless", "1"); }
        }
        else options.Set("preset", codec == 5 ? std::to_string(SoftwareSpeed(codec, request.preset)) : request.preset);
        if (request.rate_control_mode == 1 || request.rate_control_mode == 4) options.Set("crf", std::to_string(request.quality));
        if (codec == 1 && request.rate_control_mode == 3) options.Set("x264-params", "filler=1");
        if (codec == 2)
        {
            auto parameters = "pools=none:frame-threads=4:log-level=error:colorprim=" + std::to_string(metadata.primaries) +
                ":transfer=" + std::to_string(metadata.transfer) + ":colormatrix=" + std::to_string(metadata.matrix) + metadata.X265Options();
            if (request.rate_control_mode == 1 && request.quality == 0) parameters += ":lossless=1";
            else if (request.rate_control_mode == 3) parameters += ":strict-cbr=1";
            options.Set("x265-params", parameters);
        }
        if (codec == 5)
        {
            backend.context->qmin = 1; backend.context->qmax = 63;
            backend.context->err_recognition |= AV_EF_EXPLODE;
            auto parameters = std::string("lp=4:rc=") + (request.rate_control_mode == 1 ? "0" : request.rate_control_mode == 2 ? "1" : "2");
            if (request.rate_control_mode == 3) parameters += ":pred-struct=1";
            options.Set("svtav1-params", parameters);
        }
    }
    else if (std::strstr(name, "videotoolbox"))
    {
        Require(!metadata.IsHdr() && !metadata.hdr.flags, "VideoToolbox HDR encoding is not covered by the verified export contract");
        options.Set("allow_sw", "0"); options.Set("require_sw", "0");
        options.Set("constant_bit_rate", request.rate_control_mode == 3 ? "1" : "0");
        options.Set("prio_speed", speed < 5 ? "1" : "0");
        if (codec == 2) options.Set("profile", "main10");
    }
    else if (std::strstr(name, "nvenc"))
    {
        options.Set("preset", speed < 5 ? "p3" : speed > 5 ? "p5" : "p4");
        options.Set("rc", request.rate_control_mode == 3 ? "cbr" : "vbr");
        if (codec == 2) options.Set("profile", "main10");
    }
    else if (std::strstr(name, "qsv"))
    {
        ConfigureQsvDevice(backend.context);
        options.Set("preset", speed < 5 ? "fast" : speed > 5 ? "slow" : "medium");
        Optional(backend.context, options, "look_ahead", "0"); Optional(backend.context, options, "vcm", "0");
        Optional(backend.context, options, "low_delay_brc", "0");
    }
    else if (std::strstr(name, "amf"))
    {
        options.Set("quality", speed < 5 ? "speed" : speed > 5 ? "quality" : "balanced");
        options.Set("rc", request.rate_control_mode == 3 ? "cbr" : "vbr_peak");
    }
}
void ConfirmOptions(EncoderBackend &backend, const an_export_request &request, an_export_result_info &result)
{
    auto *encoder = backend.context;
    result.rate_control_mode = request.rate_control_mode;
    result.quality = request.rate_control_mode == 1 || request.rate_control_mode == 4 ? request.quality : 0;
    result.video_bitrate = request.rate_control_mode == 1 ? 0 : request.video_bitrate / 1000 * 1000;
    result.verification = 1; result.bitrate_precision = 1000;
    Require(encoder->bit_rate == result.video_bitrate, "Encoder did not preserve the configured target bitrate");
    if (request.rate_control_mode == 1 || request.rate_control_mode == 4)
    {
        double quality = -1;
        Require(av_opt_get_double(encoder->priv_data, "crf", 0, &quality) >= 0 && quality == request.quality,
            "Encoder did not preserve the selected quality parameter");
    }
    if (request.rate_control_mode == 2 || request.rate_control_mode == 3)
    {
        const bool svt = std::strcmp(encoder->codec->name, "libsvtav1") == 0;
        const int64_t peak = svt ? 0 : static_cast<int64_t>(result.video_bitrate) * (request.rate_control_mode == 3 ? 1 : 2);
        Require(encoder->rc_max_rate == peak && encoder->rc_min_rate == (request.rate_control_mode == 3 ? result.video_bitrate : 0) &&
            encoder->rc_buffer_size == (svt ? result.video_bitrate : peak) * 2,
            "Encoder did not preserve the selected bitrate and buffer configuration");
        if (request.encoding_mode == 0 && !svt)
        {
            double quality = 0;
            Require(av_opt_get_double(encoder->priv_data, "crf", 0, &quality) >= 0 && quality < 0,
                "Encoder retained constant quality instead of bitrate rate control");
        }
        if (std::strstr(encoder->codec->name, "nvenc"))
        {
            const auto *option = av_opt_find(encoder->priv_data, "rc", nullptr, 0, 0);
            int selected = 0; int64_t actual = 0;
            Require(option && av_opt_eval_int(encoder->priv_data, option, request.rate_control_mode == 3 ? "cbr" : "vbr", &selected) >= 0 &&
                av_opt_get_int(encoder->priv_data, "rc", 0, &actual) >= 0 && actual == selected,
                "NVENC did not preserve the configured rate-control option");
        }
    }
}
}
int SoftwareSpeed(int codec, const char *preset)
{
    const int index = PresetIndex(preset);
    constexpr int svt[]{9,9,8,8,7,6,4,2,0};
    return codec == 5 ? svt[index] : 8 - index;
}
bool ValidRateControl(const an_export_request &request)
{
    if (request.encoding_mode < 0 || request.encoding_mode > 1 || request.rate_control_reserved) return false;
    if (request.rate_control_mode < 1 || request.rate_control_mode > 4) return false;
    const bool quality = request.rate_control_mode == 1 || request.rate_control_mode == 4;
    if (quality && request.encoding_mode != 0) return false;
    if (request.rate_control_mode == 4 && request.codec != 3) return false;
    if (request.rate_control_mode == 1 && request.codec == 3) return false;
    if (quality && (request.quality < (request.codec == 5 ? 1 : 0) || request.quality > (request.codec >= 3 ? 63 : 51))) return false;
    if (request.rate_control_mode != 1 && (request.video_bitrate < 100000 || request.video_bitrate > 200000000)) return false;
    return true;
}
EncoderBackend::EncoderBackend() = default;
EncoderBackend::~EncoderBackend() { avcodec_free_context(&context); }
std::unique_ptr<EncoderBackend> EncoderBackend::Open(const an_export_request &request, int codec, AVStream *source,
    const AVFrame *frame, const AVFormatContext *output, const ExportMetadata &metadata,
    an_export_result_info &result, const std::atomic<bool> &cancelled)
{
    Require(!(metadata.IsHdr() || metadata.hdr.flags) || (codec != 1 && codec != 3), "HDR requires HEVC, VP9 or AV1 output");
    const auto *inputFormat = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(frame->format));
    const bool tenBit = codec == 2 || ((codec == 4 || codec == 5) && (metadata.IsHdr() || (inputFormat && inputFormat->comp[0].depth > 8)));
    const auto format = request.encoding_mode == 0 ? (tenBit ? AV_PIX_FMT_YUV420P10LE : AV_PIX_FMT_YUV420P) :
        (tenBit ? AV_PIX_FMT_P010LE : AV_PIX_FMT_NV12);
    std::vector<const char *> candidates;
    if (request.encoding_mode == 0)
    {
        constexpr const char *software[]{nullptr,"libx264","libx265","libvpx","libvpx-vp9","libsvtav1"};
        candidates.push_back(software[codec]);
    }
#if defined(__APPLE__)
    else if (codec == 1) candidates = {"h264_videotoolbox"};
    else if (codec == 2) candidates = {"hevc_videotoolbox"};
#elif defined(_WIN32)
    else if (codec == 1) candidates = {"h264_nvenc","h264_qsv","h264_amf"};
    else if (codec == 2) candidates = {"hevc_nvenc","hevc_qsv","hevc_amf"};
    else if (codec == 4) candidates = {"vp9_qsv"};
    else if (codec == 5) candidates = {"av1_nvenc","av1_qsv","av1_amf"};
#endif
    std::ostringstream failures;
    for (const auto *name : candidates)
    {
        if (cancelled.load()) throw ExportFailure(4, "Export cancelled");
        try
        {
            const auto *encoder = avcodec_find_encoder_by_name(name);
            Require(encoder != nullptr, "Encoder not compiled into the pinned FFmpeg runtime");
            Require(Supports(encoder, format), "Encoder does not accept the required output pixel format");
            auto backend = std::make_unique<EncoderBackend>();
            auto *value = backend->context = avcodec_alloc_context3(encoder);
            if (!value) throw std::bad_alloc();
            value->width = request.width; value->height = request.height; value->pix_fmt = format;
            value->time_base = source->time_base; value->sample_aspect_ratio = frame->sample_aspect_ratio;
            value->framerate = source->avg_frame_rate.num > 0 ? source->avg_frame_rate : source->r_frame_rate;
            value->thread_count = 4; value->flags |= AV_CODEC_FLAG_FRAME_DURATION;
            if (output->oformat->flags & AVFMT_GLOBALHEADER) value->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
            metadata.Apply(value);
            ConfigureRate(value, request, codec == 5 && request.encoding_mode == 0);
            Dictionary options;
            ConfigureOptions(*backend, request, codec, metadata, options);
            CheckAv(avcodec_open2(value, encoder, &options.value), "initialize video encoder");
            Require(av_dict_count(options.value) == 0, "Encoder did not consume the selected encoding options");
            ConfirmOptions(*backend, request, result);
            result.codec = codec; result.bit_depth = tenBit ? 10 : 8;
            std::strncpy(result.pixel_format, av_get_pix_fmt_name(format), sizeof(result.pixel_format) - 1);
            result.hdr = metadata.hdr;
            return backend;
        }
        catch (const ExportFailure &error) { failures << name << ": " << error.what() << "; "; }
    }
    throw ExportFailure(2, std::string(request.encoding_mode ? "GPU encoder unavailable; no CPU fallback. " : "Software encoder unavailable. ") +
        (candidates.empty() ? "No supported backend for this codec/platform" : failures.str()));
}
}
