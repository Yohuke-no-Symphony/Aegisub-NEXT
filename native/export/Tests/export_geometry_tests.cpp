#include "export_geometry.h"
#include "export_error.h"
#include <array>
#include <iostream>
#include <limits>
#include <memory>
extern "C"
{
#include <libavutil/intreadwrite.h>
}
using aeginext::encode::ExportGeometry;
using aeginext::encode::Require;
namespace
{
struct EncoderDeleter
{
    void operator()(AVCodecContext *value) const { avcodec_free_context(&value); }
};
using Encoder = std::unique_ptr<AVCodecContext, EncoderDeleter>;
Encoder MakeEncoder(const std::array<uint32_t, 4> *padding = nullptr, size_t sideDataSize = 16)
{
    Encoder encoder(avcodec_alloc_context3(nullptr));
    if (!encoder) throw std::bad_alloc();
    encoder->codec_id = AV_CODEC_ID_AV1;
    encoder->width = 1920;
    encoder->height = 1080;
    if (padding)
    {
        auto *data = av_packet_side_data_new(&encoder->coded_side_data, &encoder->nb_coded_side_data,
            AV_PKT_DATA_FRAME_CROPPING, sideDataSize, 0);
        if (!data) throw std::bad_alloc();
        for (size_t i = 0; i < padding->size() && (i + 1) * 4 <= sideDataSize; ++i)
        {
            AV_WL32(data->data + i * 4, (*padding)[i]);
        }
    }
    return encoder;
}
template<class Action> void Reject(Action action, const char *message)
{
    bool rejected = false;
    try { action(); }
    catch (const aeginext::encode::ExportFailure &) { rejected = true; }
    Require(rejected, message);
}
void CheckGeometry(const std::array<uint32_t, 4> &padding, uint32_t width, uint32_t height)
{
    auto encoder = MakeEncoder(&padding);
    const auto geometry = ExportGeometry::Capture(1920, 1080, "av1_amf", encoder.get());
    an_export_result_info result{};
    geometry.WriteResult(result);
    Require(result.output_width == width && result.output_height == height &&
        result.padding_top == padding[0] && result.padding_bottom == padding[1] &&
        result.padding_left == padding[2] && result.padding_right == padding[3], "AMF alignment receipt changed");
    auto *parameters = avcodec_parameters_alloc();
    if (!parameters) throw std::bad_alloc();
    try
    {
        aeginext::encode::CheckAv(avcodec_parameters_from_context(parameters, encoder.get()), "copy geometry fixture");
        geometry.Apply(parameters);
        Require(parameters->width == static_cast<int>(width) && parameters->height == static_cast<int>(height) &&
            !av_packet_side_data_get(parameters->coded_side_data, parameters->nb_coded_side_data, AV_PKT_DATA_FRAME_CROPPING),
            "Uncropped output retained container cropping or canvas dimensions");
        AVFrame frame{};
        frame.width = static_cast<int>(width);
        frame.height = static_cast<int>(height);
        geometry.Verify(parameters, &frame, false);
        geometry.Verify(parameters, &frame, true);
        frame.height += 2;
        Reject([&]() { geometry.Verify(parameters, &frame, false); }, "Unexpected encoded dimensions were accepted");
        frame.height -= 2;
        parameters->height -= 2;
        Reject([&]() { geometry.Verify(parameters, &frame, true); }, "Unexpected container dimensions were accepted");
        parameters->height += 2;
        frame.crop_bottom = 2;
        Reject([&]() { geometry.Verify(parameters, &frame, true); }, "Decoded output cropping was accepted");
        frame.crop_bottom = 0;
        auto *data = av_packet_side_data_new(&parameters->coded_side_data, &parameters->nb_coded_side_data,
            AV_PKT_DATA_FRAME_CROPPING, 16, 0);
        if (!data) throw std::bad_alloc();
        for (int i = 0; i < 4; ++i) AV_WL32(data->data + i * 4, i == 1 ? 2 : 0);
        Reject([&]() { geometry.Verify(parameters, &frame, true); }, "Final container cropping was accepted");
    }
    catch (...)
    {
        avcodec_parameters_free(&parameters);
        throw;
    }
    avcodec_parameters_free(&parameters);
}
void CheckRejections()
{
    for (const auto padding : {std::array<uint32_t, 4>{1, 2, 0, 0}, std::array<uint32_t, 4>{0, 2, 1, 0},
        std::array<uint32_t, 4>{0, std::numeric_limits<uint32_t>::max(), 0, 0},
        std::array<uint32_t, 4>{0, 0, 0, std::numeric_limits<uint32_t>::max()}})
    {
        auto encoder = MakeEncoder(&padding);
        Reject([&]() { ExportGeometry::Capture(1920, 1080, "av1_amf", encoder.get()); }, "Invalid AMF alignment was accepted");
    }
    const std::array<uint32_t, 4> padding{0, 2, 0, 0};
    auto encoder = MakeEncoder(&padding);
    for (const auto *name : {"libsvtav1", "av1_qsv", "av1_nvenc"})
    {
        Reject([&]() { ExportGeometry::Capture(1920, 1080, name, encoder.get()); }, "Other encoders accepted AMF padding");
    }
    encoder->codec_id = AV_CODEC_ID_H264;
    Reject([&]() { ExportGeometry::Capture(1920, 1080, "av1_amf", encoder.get()); }, "Non-AV1 encoder accepted AMF padding");
    for (const size_t size : {0u, 12u, 20u})
    {
        auto malformed = MakeEncoder(&padding, size);
        Reject([&]() { ExportGeometry::Capture(1920, 1080, "av1_amf", malformed.get()); }, "Malformed padding metadata was accepted");
    }
    auto unpadded = MakeEncoder();
    Reject([&]() { ExportGeometry::Capture(1920, 1078, "av1_amf", unpadded.get()); }, "Encoder input dimension change was accepted");
    Reject([&]() { ExportGeometry::Capture(0, 1080, "av1_amf", unpadded.get()); }, "Empty canvas was accepted");
    const auto geometry = ExportGeometry::Capture(1920, 1080, "av1_amf", unpadded.get());
    AVCodecParameters parameters{};
    parameters.width = 1920; parameters.height = 1082;
    AVFrame frame{};
    frame.width = 1920; frame.height = 1082;
    Reject([&]() { geometry.Verify(&parameters, &frame, true); }, "AMF size drift without metadata was accepted");
}
}
int main()
{
    try
    {
        CheckGeometry({0, 0, 0, 0}, 1920, 1080);
        CheckGeometry({0, 2, 0, 0}, 1920, 1082);
        CheckGeometry({0, 8, 0, 16}, 1936, 1088);
        CheckRejections();
        std::cout << "PASS AMF alignment evidence, untrimmed output, malformed metadata and strict geometry verification\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
