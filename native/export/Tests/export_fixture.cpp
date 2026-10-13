#include "aeginext_export.h"
#include <cstdio>
#include <cstdlib>
#include <array>
#include <algorithm>
int32_t AN_EXPORT_CALL Render(void *user, int64_t, int32_t, int32_t, uint32_t w, uint32_t h,
    float *rgba, uint64_t channels, an_export_overlay_info *info)
{
    auto &updated = *static_cast<bool *>(user);
    info->revision = 1;
    info->state = updated ? AN_EXPORT_OVERLAY_UNCHANGED : AN_EXPORT_OVERLAY_UPDATED;
    if (updated) return 0;
    std::fill_n(rgba, channels, 0);
    for (uint32_t y = 8; y < 16 && y < h; ++y)
        for (uint32_t x = 8; x < 16 && x < w; ++x)
            for (int c = 0; c < 4; ++c) rgba[(y*w+x)*4+c] = 0.5f;
    updated = true;
    return 0;
}
int main(int argc, char **argv)
{
    if (argc < 3)
    {
        std::fprintf(stderr, "Usage: export_fixture input output [encoding_mode] [decode_mode] [width] [height] "
            "[rate_control_mode] [video_bitrate_bps] [crf] [codec] [preset]\n");
        return 2;
    }
    void *context = nullptr;
    std::array<char, 1024> error{};
    auto result = an_export_create(&context, error.data(), error.size());
    if (result) { std::fprintf(stderr,"%s\n",error.data()); return result; }
    const auto encodingMode = argc > 3 ? std::atoi(argv[3]) : 0;
    an_export_request request{sizeof(request),AN_EXPORT_ABI_VERSION,0,argc > 10 ? std::atoi(argv[10]) : 0,
        argc > 9 ? std::atoi(argv[9]) : 0,
        argc > 5 ? static_cast<uint32_t>(std::atoi(argv[5])) : 64,
        argc > 6 ? static_cast<uint32_t>(std::atoi(argv[6])) : 48,0,argv[1],argv[2],argc > 11 ? argv[11] : "ultrafast",203,0,
        encodingMode,argc > 8 ? std::atoi(argv[8]) : 8000000,
        argc > 4 ? static_cast<uint32_t>(std::atoi(argv[4])) : 0,0,
        argc > 7 ? std::atoi(argv[7]) : encodingMode == 0 ? 1 : 2,0};
    uint64_t frames = 0;
    auto updated = false;
    result = an_export_run(context, &request, Render, &updated, &frames, error.data(), error.size());
    std::printf("result=%d frames=%llu encoder=%s error=%s\n",result,(unsigned long long)frames,an_export_encoder_name(context),error.data());
    if (!result && argc > 12)
    {
        an_export_mux_request mux{sizeof(mux), AN_EXPORT_ABI_VERSION, argv[12], argc > 13 ? argv[13] : nullptr,
            argc > 14 ? std::atoi(argv[14]) : -1, 0};
        result = an_export_mux(context, &mux, error.data(), error.size());
        std::printf("mux=%d error=%s\n", result, error.data());
    }
    if (!result)
    {
        an_export_result_info info{};
        info.struct_size = sizeof(info);
        info.abi_version = AN_EXPORT_ABI_VERSION;
        result = an_export_get_result_info(context, &info, error.data(), error.size());
        std::printf("core=%u capabilities=%u requested=%u decoder=%u hardware=%u generation=%llu delivered=%llu "
            "range=%d matrix=%d primaries=%d transfer=%d inferred=%u rate_control=%d video_bitrate=%d crf=%d "
            "output=%ux%u padding=%u/%u/%u/%u fallback=%s error=%s\n",
            info.core_version,info.capabilities,info.requested_decode_mode,info.active_decode_backend,
            info.hardware_confirmed,(unsigned long long)info.generation,(unsigned long long)info.delivered_frames,
            info.color_range,info.color_matrix,info.color_primaries,info.color_transfer,info.inferred_fields,
            info.rate_control_mode,info.video_bitrate,info.quality,info.output_width,info.output_height,
            info.padding_top,info.padding_bottom,info.padding_left,info.padding_right,info.fallback_reason,error.data());
        if (!result && (info.rate_control_mode != request.rate_control_mode || info.verification != 1 ||
            info.video_bitrate != (request.rate_control_mode == 1 ? 0 : request.video_bitrate / 1000 * 1000) ||
            info.quality != ((request.rate_control_mode == 1 || request.rate_control_mode == 4) ? request.quality : 0)))
        {
            std::fprintf(stderr, "Initialized rate-control receipt does not match the active normalized configuration\n");
            result = 1;
        }
    }
    an_export_destroy(context);
    return result;
}
