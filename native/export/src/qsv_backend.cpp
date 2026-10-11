#include "qsv_backend.h"
#include "export_error.h"
extern "C"
{
#include <libavutil/hwcontext.h>
}
namespace aeginext::encode
{
void ConfigureQsvDevice(AVCodecContext *encoder)
{
    AVDictionary *options = nullptr;
    av_dict_set(&options, "child_device_type", "d3d11va", 0);
    int created = av_hwdevice_ctx_create(&encoder->hw_device_ctx, AV_HWDEVICE_TYPE_QSV, "hw_any", options, 0);
    av_dict_free(&options);
    CheckAv(created, "create QSV hardware device");
    // The pinned Windows FFmpeg statically links its dispatcher. Its private session
    // cannot be queried through another dispatcher or inspected through private layouts.
}
}
