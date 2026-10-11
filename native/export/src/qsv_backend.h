#pragma once
extern "C"
{
#include <libavcodec/avcodec.h>
}
namespace aeginext::encode
{
void ConfigureQsvDevice(AVCodecContext *encoder);
}
