#pragma once
#include "export_metadata.h"
#include <atomic>
namespace aeginext::encode
{
void MuxExport(const char *sourcePath, const char *videoPath, const an_export_mux_request &request,
    const ExportMetadata &metadata, const ExportGeometry &geometry, AVCodecID codec, int depth,
    const std::atomic<bool> &cancelled);
}
