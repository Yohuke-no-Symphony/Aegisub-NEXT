#pragma once
#include "aeginext_export.h"
#include "export_metadata.h"
#include <atomic>
#include <memory>
#include <string>
extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
}
namespace aeginext::encode
{
class EncoderBackend
{
public:
    EncoderBackend();
    ~EncoderBackend();
    AVCodecContext *context = nullptr;
    static std::unique_ptr<EncoderBackend> Open(const an_export_request &request, int codec,
        AVStream *source, const AVFrame *frame, const AVFormatContext *output,
        const ExportMetadata &metadata, an_export_result_info &result, const std::atomic<bool> &cancelled);
};
int SoftwareSpeed(int codec, const char *preset);
bool ValidRateControl(const an_export_request &request);
}
