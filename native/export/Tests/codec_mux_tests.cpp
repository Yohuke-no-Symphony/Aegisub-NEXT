#include "aeginext_export.h"
#include "export_error.h"
#include "export_metadata.h"
#include <algorithm>
#include <array>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <memory>
#include <string>
extern "C"
{
#include <libavutil/mastering_display_metadata.h>
#include <libavutil/mem.h>
}
using aeginext::encode::CheckAv;
using aeginext::encode::Require;
namespace
{
struct Fixture
{
    std::filesystem::path directory = std::filesystem::temp_directory_path() /
        ("aeginext-codecs-" + std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()));
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
        av_frame_free(&frame); av_packet_free(&packet); avcodec_free_context(&encoder);
        if (output) { if (output->pb) avio_closep(&output->pb); avformat_free_context(output); }
    }
};
void MakeSource(const std::string &path, int transfer, int chapterStart = 0)
{
    Encoder state;
    CheckAv(avformat_alloc_output_context2(&state.output, nullptr, "matroska", path.c_str()), "create fixture container");
    const auto *codec = avcodec_find_encoder(AV_CODEC_ID_FFV1);
    Require(codec && state.frame && state.packet, "Fixture codec or allocation unavailable");
    state.encoder = avcodec_alloc_context3(codec);
    auto *encoder = state.encoder;
    encoder->width = 128; encoder->height = 128; encoder->time_base = {1, 1000}; encoder->framerate = {25,1};
    encoder->pix_fmt = transfer == AVCOL_TRC_BT709 ? AV_PIX_FMT_YUV420P : AV_PIX_FMT_YUV420P10LE;
    encoder->flags |= AV_CODEC_FLAG_GLOBAL_HEADER;
    auto *frame = state.frame;
    frame->width = 128; frame->height = 128; frame->format = encoder->pix_fmt;
    frame->color_range = AVCOL_RANGE_MPEG; frame->chroma_location = AVCHROMA_LOC_LEFT;
    frame->colorspace = transfer == AVCOL_TRC_BT709 ? AVCOL_SPC_BT709 : AVCOL_SPC_BT2020_NCL;
    frame->color_primaries = transfer == AVCOL_TRC_BT709 ? AVCOL_PRI_BT709 : AVCOL_PRI_BT2020;
    frame->color_trc = static_cast<AVColorTransferCharacteristic>(transfer);
    if (transfer == AVCOL_TRC_SMPTE2084)
    {
        auto *mastering = av_mastering_display_metadata_create_side_data(frame);
        mastering->has_primaries = mastering->has_luminance = 1;
        mastering->display_primaries[0][0] = {34000,50000}; mastering->display_primaries[0][1] = {16000,50000};
        mastering->display_primaries[1][0] = {13250,50000}; mastering->display_primaries[1][1] = {34500,50000};
        mastering->display_primaries[2][0] = {7500,50000}; mastering->display_primaries[2][1] = {3000,50000};
        mastering->white_point[0] = {15635,50000}; mastering->white_point[1] = {16450,50000};
        mastering->min_luminance = {1,10000}; mastering->max_luminance = {1000,1};
        auto *light = av_content_light_metadata_create_side_data(frame);
        light->MaxCLL = 1000; light->MaxFALL = 400;
    }
    aeginext::encode::ExportMetadata metadata(frame);
    metadata.Apply(encoder);
    CheckAv(avcodec_open2(encoder, codec, nullptr), "open fixture encoder");
    auto *video = avformat_new_stream(state.output, nullptr);
    CheckAv(avcodec_parameters_from_context(video->codecpar, encoder), "fixture video parameters");
    metadata.Apply(video->codecpar); video->time_base = {1,1000};
    auto *audio = avformat_new_stream(state.output, nullptr);
    audio->time_base = {1,48000}; audio->codecpar->codec_type = AVMEDIA_TYPE_AUDIO;
    audio->codecpar->codec_id = AV_CODEC_ID_PCM_S16LE; audio->codecpar->sample_rate = 48000;
    audio->codecpar->bits_per_coded_sample = 16; audio->codecpar->block_align = 2;
    av_channel_layout_default(&audio->codecpar->ch_layout, 1);
    av_dict_set(&state.output->metadata, "title", "Codec mux contract", 0);
    state.output->chapters = static_cast<AVChapter **>(av_calloc(1, sizeof(AVChapter *)));
    state.output->chapters[0] = static_cast<AVChapter *>(av_mallocz(sizeof(AVChapter)));
    state.output->nb_chapters = 1;
    state.output->chapters[0]->time_base = {1,1000}; state.output->chapters[0]->start = chapterStart;
    state.output->chapters[0]->end = 1240;
    av_dict_set(&state.output->chapters[0]->metadata, "title", "Preserved chapter", 0);
    CheckAv(avio_open(&state.output->pb, path.c_str(), AVIO_FLAG_WRITE), "open fixture output");
    CheckAv(avformat_write_header(state.output, nullptr), "write fixture header");
    CheckAv(av_frame_get_buffer(frame, 32), "allocate fixture frame");
    auto drain = [&]()
    {
        while (true)
        {
            const int received = avcodec_receive_packet(encoder, state.packet);
            if (received == AVERROR(EAGAIN) || received == AVERROR_EOF) break;
            CheckAv(received, "receive fixture packet");
            av_packet_rescale_ts(state.packet, encoder->time_base, video->time_base);
            state.packet->stream_index = video->index;
            CheckAv(av_interleaved_write_frame(state.output, state.packet), "write fixture video");
        }
    };
    for (int i = 0; i < 6; ++i)
    {
        CheckAv(av_frame_make_writable(frame), "writable fixture frame");
        for (int plane = 0; plane < 3; ++plane)
            for (int y = 0; y < (plane ? 64 : 128); ++y)
                for (int x = 0; x < (plane ? 64 : 128); ++x)
                {
                    const int sample = plane ? 128 : 16 + ((x + y + i * 7) % 220);
                    if (transfer == AVCOL_TRC_BT709) frame->data[plane][y * frame->linesize[plane] + x] = static_cast<uint8_t>(sample);
                    else reinterpret_cast<uint16_t *>(frame->data[plane] + y * frame->linesize[plane])[x] = static_cast<uint16_t>(sample * 4);
                }
        frame->pts = 1000 + i * 40; frame->duration = 40;
        CheckAv(avcodec_send_frame(encoder, frame), "send fixture frame"); drain();
        CheckAv(av_new_packet(state.packet, 1920 * 2), "allocate fixture audio");
        std::memset(state.packet->data, 0, state.packet->size);
        state.packet->pts = state.packet->dts = 48000 + i * 1920; state.packet->duration = 1920;
        av_packet_rescale_ts(state.packet, AVRational{1,48000}, audio->time_base);
        state.packet->stream_index = audio->index;
        CheckAv(av_interleaved_write_frame(state.output, state.packet), "write fixture audio");
    }
    CheckAv(avcodec_send_frame(encoder, nullptr), "drain fixture"); drain();
    CheckAv(av_write_trailer(state.output), "finalize fixture");
}
int32_t AN_EXPORT_CALL Render(void *, int64_t, int32_t, int32_t, uint32_t, uint32_t, float *, uint64_t, an_export_overlay_info *info)
{
    info->state = AN_EXPORT_OVERLAY_EMPTY; info->revision = 1; return 0;
}
void CheckResult(int code, const char *error)
{
    if (code) throw std::runtime_error(std::to_string(code) + ": " + error);
}
void Export(const std::string &source, const std::string &intermediate, const std::string &destination, int codec, int mode,
    int transfer, bool audio, int expectedMuxCode = 0)
{
    void *context = nullptr;
    std::array<char, 2048> error{};
    CheckResult(an_export_create(&context, error.data(), error.size()), error.data());
    try
    {
        an_export_request request{sizeof(request),6,0,codec,32,128,128,0,source.c_str(),intermediate.c_str(),"ultrafast",203,0,0,400000,1,0,mode,0};
        uint64_t frames = 0;
        CheckResult(an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size()), error.data());
        Require(frames == 6, "Encoded frame count changed");
        an_export_result_info info{}; info.struct_size = sizeof(info); info.abi_version = 6;
        CheckResult(an_export_get_result_info(context, &info, error.data(), error.size()), error.data());
        Require(info.output_verification == 1 && info.verification == 1 && info.bitrate_precision == 1000 &&
            info.codec == codec && info.rate_control_mode == mode, "Encoded receipt is incomplete");
        Require(info.bit_depth == (transfer == AVCOL_TRC_BT709 ? 8 : 10) && info.hdr.flags == (transfer == AVCOL_TRC_SMPTE2084 ? 7u : 0u),
            "Encoded HDR receipt differs from input");
        an_export_mux_request mux{sizeof(mux),6,destination.c_str(),audio ? source.c_str() : nullptr,audio ? 1 : -1,0};
        const int muxResult = an_export_mux(context, &mux, error.data(), error.size());
        if (expectedMuxCode)
        {
            Require(muxResult == expectedMuxCode && !std::filesystem::exists(destination), "Unfaithful mux did not fail transactionally");
            CheckResult(an_export_get_result_info(context, &info, error.data(), error.size()), error.data());
            Require(info.output_verification == 1, "Rejected mux was marked verified");
            an_export_destroy(context);
            return;
        }
        CheckResult(muxResult, error.data());
        CheckResult(an_export_get_result_info(context, &info, error.data(), error.size()), error.data());
        Require(info.output_verification == 3, "Final file validation was not recorded");
        Require(an_export_mux(context, &mux, error.data(), error.size()) == 1, "Context permitted duplicate final mux");
        AVFormatContext *output = nullptr;
        CheckAv(avformat_open_input(&output, destination.c_str(), nullptr, nullptr), "read final file");
        try
        {
            CheckAv(avformat_find_stream_info(output, nullptr), "probe final file");
            unsigned videos = 0, audios = 0;
            for (unsigned i = 0; i < output->nb_streams; ++i)
            {
                videos += output->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_VIDEO;
                audios += output->streams[i]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO;
            }
            Require(videos == 1 && audios == (audio ? 1u : 0u), "Unexpected final media stream count");
            auto *title = av_dict_get(output->metadata, "title", nullptr, 0);
            Require(title && std::strcmp(title->value, "Codec mux contract") == 0, "Source title metadata lost");
            Require(output->nb_chapters == 1, "Source chapter lost");
            Require(av_rescale_q(output->chapters[0]->start, output->chapters[0]->time_base, {1,1000}) == 0,
                ("Chapter timestamp changed: " + std::to_string(output->chapters[0]->start) + "/" + std::to_string(output->chapters[0]->time_base.den)).c_str());
            if (audio) Require(output->streams[1]->codecpar->codec_id == AV_CODEC_ID_PCM_S16LE, "Copied audio was transcoded");
        }
        catch (...) { avformat_close_input(&output); throw; }
        avformat_close_input(&output);
    }
    catch (...) { an_export_destroy(context); throw; }
    an_export_destroy(context);
}
}
int main()
{
    try
    {
        av_log_set_level(AV_LOG_ERROR);
        Fixture fixture;
        const auto path = [&](const std::string &name) { return (fixture.directory / name).string(); };
        for (const int transfer : {AVCOL_TRC_BT709, AVCOL_TRC_SMPTE2084, AVCOL_TRC_ARIB_STD_B67})
        {
            const auto source = path("source-" + std::to_string(transfer) + ".mkv");
            MakeSource(source, transfer);
            for (const int codec : {3,4,5})
            {
                if (codec == 3 && transfer != AVCOL_TRC_BT709) continue;
                for (const int mode : {codec == 3 ? 4 : 1, 2, 3})
                {
                    const auto name = std::to_string(codec) + "-" + std::to_string(mode) + "-" + std::to_string(transfer);
                    const bool copyAudio = mode == 2;
                    std::cout << "Testing " << name << std::endl;
                    Export(source, path(name + ".nut"), path(name + (copyAudio ? ".mkv" : mode == 3 && codec != 3 ? ".mp4" : ".webm")), codec, mode, transfer, copyAudio);
                }
            }
        }
        const auto lateChapter = path("late-chapter.mkv");
        MakeSource(lateChapter, AVCOL_TRC_BT709, 1000);
        Export(lateChapter, path("late-chapter.nut"), path("late-chapter.mp4"), 4, 1, AVCOL_TRC_BT709, false, 2);
        const auto pqSource = path("source-16.mkv");
        void *context = nullptr;
        std::array<char,2048> error{};
        CheckResult(an_export_create(&context, error.data(), error.size()), error.data());
        const auto rejected = path("hdr-vp8.nut");
        an_export_request request{sizeof(request),6,0,3,32,128,128,0,pqSource.c_str(),rejected.c_str(),"ultrafast",203,0,0,400000,1,0,4,0};
        uint64_t frames = 0;
        const int unsupported = an_export_run(context, &request, Render, nullptr, &frames, error.data(), error.size());
        an_export_destroy(context);
        Require(unsupported == 2, "VP8 accepted HDR without a tone-mapping policy");
        AVFrame expected{};
        expected.color_range = AVCOL_RANGE_MPEG; expected.colorspace = AVCOL_SPC_BT2020_NCL;
        expected.color_primaries = AVCOL_PRI_BT2020; expected.color_trc = AVCOL_TRC_SMPTE2084;
        aeginext::encode::ExportMetadata missingHdr(&expected);
        bool rejectedMetadata = false;
        try { missingHdr.Verify(pqSource.c_str(), AV_CODEC_ID_FFV1, 10, true, std::atomic<bool>{false}); }
        catch (const aeginext::encode::ExportFailure &) { rejectedMetadata = true; }
        Require(rejectedMetadata, "Actual HDR mismatch was accepted by final output verification");
        std::cout << "PASS VP8/VP9/AV1 quality/VBR/CBR; SDR/PQ/HLG; native mux and receipts" << std::endl;
        return 0;
    }
    catch (const std::exception &error) { std::cerr << error.what() << std::endl; return 1; }
}
