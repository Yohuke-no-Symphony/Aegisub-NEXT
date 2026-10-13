#pragma once
#include "aeginext_export.h"
#include "export_geometry.h"
#include <atomic>
#include <string>
extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
}
namespace aeginext::encode
{
class ExportMetadata
{
public:
    explicit ExportMetadata(const AVFrame *frame);
    ExportMetadata() = default;
    an_export_hdr_metadata hdr{};
    AVColorRange range = AVCOL_RANGE_UNSPECIFIED;
    AVColorSpace matrix = AVCOL_SPC_UNSPECIFIED;
    AVColorPrimaries primaries = AVCOL_PRI_UNSPECIFIED;
    AVColorTransferCharacteristic transfer = AVCOL_TRC_UNSPECIFIED;
    AVChromaLocation chroma = AVCHROMA_LOC_LEFT;
    bool IsHdr() const;
    bool SameHdr(const ExportMetadata &other) const;
    void Apply(AVFrame *frame) const;
    void Apply(AVCodecContext *encoder) const;
    void Apply(AVCodecParameters *parameters) const;
    std::string X265Options() const;
    AVPixelFormat Verify(const char *path, AVCodecID codec, int depth, const ExportGeometry &geometry, bool finalContainer,
        const std::atomic<bool> &cancelled) const;
};
}
