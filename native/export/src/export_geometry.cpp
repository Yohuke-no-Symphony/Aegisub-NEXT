#include "export_geometry.h"
#include "export_error.h"
#include <array>
#include <limits>
#include <sstream>
extern "C"
{
#include <libavutil/intreadwrite.h>
}
namespace aeginext::encode
{
namespace
{
std::array<uint32_t, 4> ReadCropping(const AVPacketSideData *data)
{
    if (!data) return {};
    Require(data->data && data->size == 16, "Encoder or container frame cropping metadata is malformed");
    return {AV_RL32(data->data), AV_RL32(data->data + 4), AV_RL32(data->data + 8), AV_RL32(data->data + 12)};
}
}
ExportGeometry ExportGeometry::Capture(uint32_t width, uint32_t height, const std::string &encoderName,
    const AVCodecContext *encoder)
{
    const auto maximum = static_cast<uint32_t>(std::numeric_limits<int>::max());
    Require(encoder && !encoderName.empty() && width > 0 && height > 0 && width <= maximum && height <= maximum &&
        encoder->width == static_cast<int>(width) && encoder->height == static_cast<int>(height),
        "Encoder input dimensions differ from the project canvas");
    const auto padding = ReadCropping(av_packet_side_data_get(encoder->coded_side_data, encoder->nb_coded_side_data,
        AV_PKT_DATA_FRAME_CROPPING));
    const bool hasPadding = padding[0] || padding[1] || padding[2] || padding[3];
    Require(!hasPadding || (encoderName == "av1_amf" && encoder->codec_id == AV_CODEC_ID_AV1 && !padding[0] && !padding[2]),
        "Only AV1 AMF right/bottom alignment padding can change export dimensions");
    const uint64_t outputWidth = static_cast<uint64_t>(width) + padding[2] + padding[3];
    const uint64_t outputHeight = static_cast<uint64_t>(height) + padding[0] + padding[1];
    Require(outputWidth <= maximum && outputHeight <= maximum, "AMF alignment exceeds supported output dimensions");
    ExportGeometry result;
    result.canvasWidth = width;
    result.canvasHeight = height;
    result.width = static_cast<uint32_t>(outputWidth);
    result.height = static_cast<uint32_t>(outputHeight);
    result.paddingTop = padding[0];
    result.paddingBottom = padding[1];
    result.paddingLeft = padding[2];
    result.paddingRight = padding[3];
    result.encoderName = encoderName;
    return result;
}
void ExportGeometry::Apply(AVCodecParameters *parameters) const
{
    Require(parameters && width > 0 && height > 0, "Export geometry is unavailable");
    parameters->width = static_cast<int>(width);
    parameters->height = static_cast<int>(height);
    av_packet_side_data_remove(parameters->coded_side_data, &parameters->nb_coded_side_data, AV_PKT_DATA_FRAME_CROPPING);
}
std::string ExportGeometry::Describe() const
{
    std::ostringstream message;
    message << "encoder=" << encoderName << ", canvas=" << canvasWidth << 'x' << canvasHeight
        << ", padding(top/bottom/left/right)=" << paddingTop << '/' << paddingBottom << '/' << paddingLeft << '/' << paddingRight
        << ", expected=" << width << 'x' << height;
    return message.str();
}
void ExportGeometry::Verify(const AVCodecParameters *parameters, const AVFrame *frame, bool finalContainer) const
{
    Require(parameters && frame && width > 0 && height > 0, "Export geometry verification requires a decoded frame");
    const auto crop = ReadCropping(av_packet_side_data_get(parameters->coded_side_data, parameters->nb_coded_side_data,
        AV_PKT_DATA_FRAME_CROPPING));
    const bool cropped = crop[0] || crop[1] || crop[2] || crop[3] ||
        frame->crop_top || frame->crop_bottom || frame->crop_left || frame->crop_right;
    if (parameters->width != static_cast<int>(width) || parameters->height != static_cast<int>(height) ||
        frame->width != static_cast<int>(width) || frame->height != static_cast<int>(height) || cropped)
    {
        std::ostringstream message;
        message << (finalContainer ? "Final container" : "Encoded bitstream") << " geometry verification failed: "
            << Describe() << ", actual stream=" << parameters->width << 'x' << parameters->height
            << ", decoded=" << frame->width << 'x' << frame->height << ", cropped=" << (cropped ? "yes" : "no");
        throw ExportFailure(2, message.str());
    }
}
void ExportGeometry::WriteResult(an_export_result_info &result) const
{
    Require(width > 0 && height > 0, "Export geometry is unavailable");
    result.output_width = width;
    result.output_height = height;
    result.padding_top = paddingTop;
    result.padding_bottom = paddingBottom;
    result.padding_left = paddingLeft;
    result.padding_right = paddingRight;
}
}
