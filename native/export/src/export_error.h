#pragma once
#include <stdexcept>
#include <string>
extern "C"
{
#include <libavutil/error.h>
}
namespace aeginext::encode
{
struct ExportFailure : std::runtime_error
{
    int code;
    ExportFailure(int value, const std::string &message) : std::runtime_error(message), code(value) {}
};
inline void Require(bool valid, const char *message)
{
    if (!valid) throw ExportFailure(2, message);
}
inline void CheckAv(int result, const char *operation)
{
    if (result >= 0) return;
    char reason[AV_ERROR_MAX_STRING_SIZE]{};
    av_strerror(result, reason, sizeof(reason));
    throw ExportFailure(3, std::string(operation) + ": " + reason);
}
}
