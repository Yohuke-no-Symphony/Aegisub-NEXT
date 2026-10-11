#include "export_metadata.h"
#include "export_error.h"
#include <cmath>
#include <cstring>
#include <memory>
#include <sstream>
extern "C"
{
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/pixdesc.h>
}
namespace aeginext::encode
{
namespace
{
an_export_rational Capture(AVRational value)
{
    Require(value.den > 0 && value.num >= 0, "Invalid static HDR rational");
    return {value.num, value.den};
}
AVRational Restore(an_export_rational value) { return {value.numerator, value.denominator}; }
double Number(an_export_rational value) { return av_q2d(Restore(value)); }
AVMasteringDisplayMetadata Mastering(const an_export_hdr_metadata &hdr)
{
    AVMasteringDisplayMetadata value{};
    const an_export_rational coordinates[]{hdr.red_x, hdr.red_y, hdr.green_x, hdr.green_y, hdr.blue_x, hdr.blue_y};
    for (int primary = 0; primary < 3; ++primary)
        for (int axis = 0; axis < 2; ++axis) value.display_primaries[primary][axis] = Restore(coordinates[primary * 2 + axis]);
    value.white_point[0] = Restore(hdr.white_x); value.white_point[1] = Restore(hdr.white_y);
    value.min_luminance = Restore(hdr.min_luminance); value.max_luminance = Restore(hdr.max_luminance);
    value.has_primaries = (hdr.flags & 1) != 0; value.has_luminance = (hdr.flags & 2) != 0;
    return value;
}
void CopySideData(AVFrame *frame, AVFrameSideDataType type, const void *data, size_t size)
{
    av_frame_remove_side_data(frame, type);
    auto *side = av_frame_new_side_data(frame, type, size);
    if (!side) throw std::bad_alloc();
    std::memcpy(side->data, data, size);
}
void CopySideData(AVCodecParameters *parameters, AVPacketSideDataType type, const void *data, size_t size)
{
    auto *side = av_packet_side_data_new(&parameters->coded_side_data, &parameters->nb_coded_side_data, type, size, 0);
    if (!side) throw std::bad_alloc();
    std::memcpy(side->data, data, size);
}
struct Input
{
    AVFormatContext *format = nullptr;
    AVCodecContext *decoder = nullptr;
    AVPacket *packet = av_packet_alloc();
    AVFrame *frame = av_frame_alloc();
    ~Input() { av_frame_free(&frame); av_packet_free(&packet); avcodec_free_context(&decoder); avformat_close_input(&format); }
};
int Interrupt(void *value) { return static_cast<const std::atomic<bool> *>(value)->load() ? 1 : 0; }
bool Near(an_export_rational first, an_export_rational second, double tolerance)
{
    return first.denominator > 0 && second.denominator > 0 && std::abs(Number(first) - Number(second)) <= tolerance;
}
bool HdrEquivalent(const an_export_hdr_metadata &expected, const an_export_hdr_metadata &actual, AVCodecID codec)
{
    if (expected.flags != actual.flags) return false;
    if ((expected.flags & 4) && (expected.max_cll != actual.max_cll || expected.max_fall != actual.max_fall)) return false;
    const double coordinateTolerance = codec == AV_CODEC_ID_AV1 ? 1.0 / 65536 : 1.0 / 50000;
    if (expected.flags & 1)
    {
        const an_export_rational left[]{expected.red_x,expected.red_y,expected.green_x,expected.green_y,expected.blue_x,expected.blue_y,expected.white_x,expected.white_y};
        const an_export_rational right[]{actual.red_x,actual.red_y,actual.green_x,actual.green_y,actual.blue_x,actual.blue_y,actual.white_x,actual.white_y};
        for (int i = 0; i < 8; ++i) if (!Near(left[i], right[i], coordinateTolerance)) return false;
    }
    return !(expected.flags & 2) ||
        (Near(expected.min_luminance, actual.min_luminance, codec == AV_CODEC_ID_AV1 ? 1.0 / 16384 : 0.0001) &&
        Near(expected.max_luminance, actual.max_luminance, codec == AV_CODEC_ID_AV1 ? 1.0 / 256 : 0.0001));
}
}
ExportMetadata::ExportMetadata(const AVFrame *frame)
{
    range = frame->color_range; matrix = frame->colorspace; primaries = frame->color_primaries;
    transfer = frame->color_trc;
    if (auto *side = av_frame_get_side_data(frame, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA))
    {
        Require(side->size >= sizeof(AVMasteringDisplayMetadata), "Truncated mastering metadata");
        const auto &value = *reinterpret_cast<const AVMasteringDisplayMetadata *>(side->data);
        Require(value.has_primaries && value.has_luminance, "Partial mastering metadata requires an explicit encoding policy");
        hdr.flags |= 3;
        hdr.red_x = Capture(value.display_primaries[0][0]); hdr.red_y = Capture(value.display_primaries[0][1]);
        hdr.green_x = Capture(value.display_primaries[1][0]); hdr.green_y = Capture(value.display_primaries[1][1]);
        hdr.blue_x = Capture(value.display_primaries[2][0]); hdr.blue_y = Capture(value.display_primaries[2][1]);
        hdr.white_x = Capture(value.white_point[0]); hdr.white_y = Capture(value.white_point[1]);
        hdr.min_luminance = Capture(value.min_luminance); hdr.max_luminance = Capture(value.max_luminance);
        const an_export_rational coordinates[]{hdr.red_x,hdr.red_y,hdr.green_x,hdr.green_y,hdr.blue_x,hdr.blue_y,hdr.white_x,hdr.white_y};
        for (auto coordinate : coordinates) Require(Number(coordinate) <= 1, "Invalid mastering display coordinate");
        Require(Number(hdr.max_luminance) > Number(hdr.min_luminance) && Number(hdr.max_luminance) <= 10000,
            "Invalid mastering display luminance");
    }
    if (auto *side = av_frame_get_side_data(frame, AV_FRAME_DATA_CONTENT_LIGHT_LEVEL))
    {
        Require(side->size >= sizeof(AVContentLightMetadata), "Truncated content light metadata");
        const auto &value = *reinterpret_cast<const AVContentLightMetadata *>(side->data);
        hdr.flags |= 4; hdr.max_cll = value.MaxCLL; hdr.max_fall = value.MaxFALL;
    }
}
bool ExportMetadata::IsHdr() const { return transfer == AVCOL_TRC_SMPTE2084 || transfer == AVCOL_TRC_ARIB_STD_B67; }
bool ExportMetadata::SameHdr(const ExportMetadata &other) const { return HdrEquivalent(hdr, other.hdr, AV_CODEC_ID_NONE); }
void ExportMetadata::Apply(AVFrame *frame) const
{
    frame->color_range = range; frame->colorspace = matrix; frame->color_primaries = primaries;
    frame->color_trc = transfer; frame->chroma_location = chroma;
    if (hdr.flags & 3) { auto value = Mastering(hdr); CopySideData(frame, AV_FRAME_DATA_MASTERING_DISPLAY_METADATA, &value, sizeof(value)); }
    if (hdr.flags & 4) { AVContentLightMetadata value{hdr.max_cll, hdr.max_fall}; CopySideData(frame, AV_FRAME_DATA_CONTENT_LIGHT_LEVEL, &value, sizeof(value)); }
}
void ExportMetadata::Apply(AVCodecContext *encoder) const
{
    encoder->color_range = range; encoder->colorspace = matrix; encoder->color_primaries = primaries;
    encoder->color_trc = transfer; encoder->chroma_sample_location = chroma;
    auto *frame = av_frame_alloc();
    if (!frame) throw std::bad_alloc();
    try
    {
        Apply(frame);
        for (int i = 0; i < frame->nb_side_data; ++i)
            CheckAv(av_frame_side_data_clone(&encoder->decoded_side_data, &encoder->nb_decoded_side_data,
                frame->side_data[i], 0), "copy encoder HDR metadata");
    }
    catch (...) { av_frame_free(&frame); throw; }
    av_frame_free(&frame);
}
void ExportMetadata::Apply(AVCodecParameters *parameters) const
{
    parameters->color_range = range; parameters->color_space = matrix; parameters->color_primaries = primaries;
    parameters->color_trc = transfer; parameters->chroma_location = chroma;
    if (hdr.flags & 3) { auto value = Mastering(hdr); CopySideData(parameters, AV_PKT_DATA_MASTERING_DISPLAY_METADATA, &value, sizeof(value)); }
    if (hdr.flags & 4) { AVContentLightMetadata value{hdr.max_cll,hdr.max_fall}; CopySideData(parameters, AV_PKT_DATA_CONTENT_LIGHT_LEVEL, &value, sizeof(value)); }
}
std::string ExportMetadata::X265Options() const
{
    std::ostringstream result;
    if (hdr.flags & 3)
    {
        const auto value = Mastering(hdr);
        auto quantize = [](AVRational number, int scale) { return std::llround(av_q2d(number) * scale); };
        result << ":master-display=";
        for (int p : {1, 2, 0}) result << "RGB"[p] << '(' << quantize(value.display_primaries[p][0], 50000) << ',' << quantize(value.display_primaries[p][1], 50000) << ')';
        result << "WP(" << quantize(value.white_point[0], 50000) << ',' << quantize(value.white_point[1], 50000)
            << ")L(" << quantize(value.max_luminance, 10000) << ',' << quantize(value.min_luminance, 10000) << ')';
    }
    if (hdr.flags & 4) result << ":max-cll=" << hdr.max_cll << ',' << hdr.max_fall;
    return result.str();
}
AVPixelFormat ExportMetadata::Verify(const char *path, AVCodecID codec, int depth, bool finalContainer,
    const std::atomic<bool> &cancelled) const
{
    Input input;
    if (!input.packet || !input.frame) throw std::bad_alloc();
    input.format = avformat_alloc_context();
    if (!input.format) throw std::bad_alloc();
    input.format->interrupt_callback = {Interrupt, const_cast<std::atomic<bool> *>(&cancelled)};
    CheckAv(avformat_open_input(&input.format, path, nullptr, nullptr), "open exported video for verification");
    CheckAv(avformat_find_stream_info(input.format, nullptr), "read exported stream information");
    const int index = av_find_best_stream(input.format, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
    CheckAv(index, "locate exported video");
    const auto *parameters = input.format->streams[index]->codecpar;
    Require(parameters->codec_id == codec, "Encoded codec differs from the selected codec");
    const auto *decoder = avcodec_find_decoder(codec);
    Require(decoder != nullptr, "Encoded video decoder is unavailable for output verification");
    input.decoder = avcodec_alloc_context3(decoder);
    if (!input.decoder) throw std::bad_alloc();
    CheckAv(avcodec_parameters_to_context(input.decoder, parameters), "prepare verification decoder");
    input.decoder->thread_count = 1;
    CheckAv(avcodec_open2(input.decoder, decoder, nullptr), "open verification decoder");
    bool found = false;
    while (!found)
    {
        if (cancelled.load()) throw ExportFailure(4, "Export cancelled");
        int received = avcodec_receive_frame(input.decoder, input.frame);
        if (received == 0) { found = true; break; }
        if (received == AVERROR_EOF) break;
        CheckAv(received == AVERROR(EAGAIN) ? 0 : received, "decode verification frame");
        const int read = av_read_frame(input.format, input.packet);
        if (read == AVERROR_EOF) { CheckAv(avcodec_send_packet(input.decoder, nullptr), "drain verification decoder"); continue; }
        CheckAv(read, "read verification packet");
        if (input.packet->stream_index == index) CheckAv(avcodec_send_packet(input.decoder, input.packet), "send verification packet");
        av_packet_unref(input.packet);
    }
    Require(found, "Encoded output has no decodable video frame");
    const auto *description = av_pix_fmt_desc_get(static_cast<AVPixelFormat>(input.frame->format));
    Require(description && description->comp[0].depth == depth, "Encoded bit depth differs from the requested output");
    ExportMetadata actual(input.frame);
    if (codec != AV_CODEC_ID_VP8)
        Require(actual.range == range && actual.matrix == matrix, "Encoded color matrix/range did not survive output verification");
    if (codec != AV_CODEC_ID_VP8 && (finalContainer || codec != AV_CODEC_ID_VP9))
    {
        Require(actual.primaries == primaries && actual.transfer == transfer, "Encoded color primaries/transfer did not survive output verification");
        Require(HdrEquivalent(hdr, actual.hdr, codec), "Encoded static HDR metadata did not survive output verification");
    }
    if (finalContainer)
    {
        Require(parameters->color_range == range && parameters->color_space == matrix &&
            parameters->color_primaries == primaries && parameters->color_trc == transfer,
            "Final container did not preserve the effective video color metadata");
        if (hdr.flags & 3) Require(av_packet_side_data_get(parameters->coded_side_data, parameters->nb_coded_side_data,
            AV_PKT_DATA_MASTERING_DISPLAY_METADATA), "Final container lost mastering display metadata");
        if (hdr.flags & 4) Require(av_packet_side_data_get(parameters->coded_side_data, parameters->nb_coded_side_data,
            AV_PKT_DATA_CONTENT_LIGHT_LEVEL), "Final container lost content light metadata");
    }
    return static_cast<AVPixelFormat>(input.frame->format);
}
}
