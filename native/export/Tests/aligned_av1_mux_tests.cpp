#include "export_geometry.h"
#include "export_metadata.h"
#include "export_muxer.h"
#include "export_error.h"
#include <array>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <iostream>
extern "C"
{
#include <libavutil/intreadwrite.h>
}
using aeginext::encode::CheckAv;
using aeginext::encode::Require;
namespace
{
struct Fixture
{
    std::filesystem::path directory = std::filesystem::temp_directory_path() /
        ("aeginext-aligned-av1-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
    Fixture() { std::filesystem::create_directory(directory); }
    ~Fixture() { std::error_code ignored; std::filesystem::remove_all(directory, ignored); }
};
struct Encoder
{
    AVFormatContext *output = nullptr;
    AVCodecContext *encoder = nullptr;
    AVFrame *frame = av_frame_alloc();
    AVPacket *packet = av_packet_alloc();
    ~Encoder()
    {
        av_frame_free(&frame);
        av_packet_free(&packet);
        avcodec_free_context(&encoder);
        if (output)
        {
            if (output->pb) avio_closep(&output->pb);
            avformat_free_context(output);
        }
    }
};
aeginext::encode::ExportMetadata MakeVideo(const std::string &path, int width, int height)
{
    Encoder state;
    CheckAv(avformat_alloc_output_context2(&state.output, nullptr, "nut", path.c_str()), "create padded video");
    const auto *codec = avcodec_find_encoder_by_name("libsvtav1");
    Require(codec && state.frame && state.packet, "Padded AV1 fixture encoder is unavailable");
    state.encoder = avcodec_alloc_context3(codec);
    if (!state.encoder) throw std::bad_alloc();
    auto *encoder = state.encoder;
    encoder->width = width;
    encoder->height = height;
    encoder->time_base = {1, 1000};
    encoder->framerate = {25, 1};
    encoder->pix_fmt = AV_PIX_FMT_YUV420P;
    encoder->flags |= AV_CODEC_FLAG_GLOBAL_HEADER | AV_CODEC_FLAG_FRAME_DURATION;
    auto *frame = state.frame;
    frame->width = width;
    frame->height = height;
    frame->format = AV_PIX_FMT_YUV420P;
    frame->color_range = AVCOL_RANGE_MPEG;
    frame->colorspace = AVCOL_SPC_BT709;
    frame->color_primaries = AVCOL_PRI_BT709;
    frame->color_trc = AVCOL_TRC_BT709;
    frame->chroma_location = AVCHROMA_LOC_LEFT;
    const aeginext::encode::ExportMetadata metadata(frame);
    metadata.Apply(encoder);
    AVDictionary *options = nullptr;
    CheckAv(av_dict_set(&options, "preset", "12", 0), "set padded AV1 preset");
    CheckAv(av_dict_set(&options, "crf", "32", 0), "set padded AV1 quality");
    CheckAv(av_dict_set(&options, "svtav1-params", "lp=1", 0), "set padded AV1 threads");
    const auto opened = avcodec_open2(encoder, codec, &options);
    av_dict_free(&options);
    CheckAv(opened, "open padded AV1 encoder");
    auto *stream = avformat_new_stream(state.output, nullptr);
    if (!stream) throw std::bad_alloc();
    stream->time_base = encoder->time_base;
    CheckAv(avcodec_parameters_from_context(stream->codecpar, encoder), "copy padded video parameters");
    CheckAv(avio_open(&state.output->pb, path.c_str(), AVIO_FLAG_WRITE), "open padded output");
    CheckAv(avformat_write_header(state.output, nullptr), "write padded header");
    CheckAv(av_frame_get_buffer(frame, 32), "allocate padded frame");
    for (int plane = 0; plane < 3; ++plane)
    {
        for (int y = 0; y < (plane ? height / 2 : height); ++y)
        {
            for (int x = 0; x < (plane ? width / 2 : width); ++x)
            {
                const bool padding = x >= (plane ? 960 : 1920) || y >= (plane ? 540 : 1080);
                frame->data[plane][y * frame->linesize[plane] + x] = static_cast<uint8_t>(
                    plane ? 128 : padding ? 16 : 16 + ((x + y) % 220));
            }
        }
    }
    const auto drain = [&]()
    {
        while (true)
        {
            const auto received = avcodec_receive_packet(encoder, state.packet);
            if (received == AVERROR_EOF || received == AVERROR(EAGAIN)) break;
            CheckAv(received, "receive padded AV1 packet");
            av_packet_rescale_ts(state.packet, encoder->time_base, stream->time_base);
            state.packet->stream_index = stream->index;
            CheckAv(av_interleaved_write_frame(state.output, state.packet), "write padded AV1 packet");
        }
    };
    for (int index = 0; index < 6; ++index)
    {
        frame->pts = 1000 + index * 40;
        frame->duration = 40;
        CheckAv(avcodec_send_frame(encoder, frame), "send padded frame");
        drain();
    }
    CheckAv(avcodec_send_frame(encoder, nullptr), "drain padded encoder");
    drain();
    CheckAv(av_write_trailer(state.output), "finish padded video");
    CheckAv(avio_closep(&state.output->pb), "close padded video");
    return metadata;
}
aeginext::encode::ExportGeometry MakeGeometry(uint32_t right, uint32_t bottom)
{
    AVCodecContext encoder{};
    encoder.codec_id = AV_CODEC_ID_AV1;
    encoder.width = 1920;
    encoder.height = 1080;
    std::array<uint8_t, 16> data{};
    AV_WL32(data.data() + 4, bottom);
    AV_WL32(data.data() + 12, right);
    AVPacketSideData sideData{data.data(), data.size(), AV_PKT_DATA_FRAME_CROPPING};
    encoder.coded_side_data = &sideData;
    encoder.nb_coded_side_data = 1;
    return aeginext::encode::ExportGeometry::Capture(1920, 1080, "av1_amf", &encoder);
}
void RunCase(const Fixture &fixture, uint32_t right, uint32_t bottom)
{
    const auto name = std::to_string(right) + "-" + std::to_string(bottom);
    const auto intermediate = (fixture.directory / (name + ".nut")).string();
    const auto metadata = MakeVideo(intermediate, 1920 + static_cast<int>(right), 1080 + static_cast<int>(bottom));
    const auto geometry = MakeGeometry(right, bottom);
    const std::atomic<bool> cancelled{false};
    metadata.Verify(intermediate.c_str(), AV_CODEC_ID_AV1, 8, geometry, false, cancelled);
    for (const auto *extension : {".mkv", ".mp4", ".webm"})
    {
        const auto destination = (fixture.directory / (name + extension)).string();
        std::cout << "Testing " << 1920 + right << 'x' << 1080 + bottom << ' ' << extension << std::endl;
        an_export_mux_request request{sizeof(request), AN_EXPORT_ABI_VERSION, destination.c_str(), nullptr, -1, 0};
        aeginext::encode::MuxExport(intermediate.c_str(), intermediate.c_str(), request, metadata, geometry,
            AV_CODEC_ID_AV1, 8, cancelled);
        Require(std::filesystem::exists(destination), "Padded AV1 mux did not produce output");
        std::cout << "PASS " << 1920 + right << 'x' << 1080 + bottom << " untrimmed AV1 " << extension << '\n';
        const auto rejected = (fixture.directory / (name + "-rejected" + extension)).string();
        request.output_path = rejected.c_str();
        bool failed = false;
        try
        {
            aeginext::encode::MuxExport(intermediate.c_str(), intermediate.c_str(), request, metadata, MakeGeometry(0, 0),
                AV_CODEC_ID_AV1, 8, cancelled);
        }
        catch (const aeginext::encode::ExportFailure &error)
        {
            failed = std::string(error.what()).find("geometry verification failed") != std::string::npos;
        }
        Require(failed && !std::filesystem::exists(rejected), "Unproven geometry mismatch was not rejected transactionally");
    }
}
}
int main()
{
    try
    {
        av_log_set_level(AV_LOG_ERROR);
        Fixture fixture;
        RunCase(fixture, 0, 2);
        RunCase(fixture, 16, 8);
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
