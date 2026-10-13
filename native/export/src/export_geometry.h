#pragma once
#include "aeginext_export.h"
#include <string>
extern "C"
{
#include <libavcodec/avcodec.h>
}
namespace aeginext::encode
{
class ExportGeometry
{
public:
    static ExportGeometry Capture(uint32_t width, uint32_t height, const std::string &encoderName,
        const AVCodecContext *encoder);
    void Apply(AVCodecParameters *parameters) const;
    void Verify(const AVCodecParameters *parameters, const AVFrame *frame, bool finalContainer) const;
    void WriteResult(an_export_result_info &result) const;
private:
    uint32_t canvasWidth = 0, canvasHeight = 0;
    uint32_t width = 0, height = 0;
    uint32_t paddingTop = 0, paddingBottom = 0, paddingLeft = 0, paddingRight = 0;
    std::string encoderName;
    std::string Describe() const;
};
}
